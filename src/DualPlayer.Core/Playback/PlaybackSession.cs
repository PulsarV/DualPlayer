using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Settings;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Core.Playback;

public enum PlaybackState
{
    Idle,
    Playing,
    Paused,
    Ended,
}

/// <summary>Use case: starts a lip-synced film, ends it at its natural end or on exit.</summary>
public sealed partial class PlaybackSession
{
    private readonly IAudioEngine _engine;
    private readonly IOutputLagControl _lags;
    private readonly IVideoRenderer _renderer;
    private readonly IDeviceLatencyProbe _probe;
    private readonly OffsetBook _offsets;
    private readonly IAudioBackend _backend;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaybackSession> _logger;
    private readonly SessionEventPublisher _events = new();
    private readonly Lock _gate = new();
    private PictureSync? _pictureSync;
    private PlaybackState _state = PlaybackState.Idle;
    private bool _offsetsLoaded;
    private MasterClock? _clock;
    private StartRequest? _request;
    private LatencyEstimate? _estimate;
    private TimeSpan _endPosition;
    private ITimer? _freezeTimer;
    private long _freezeVersion;
    private bool _pictureFrozen;
    private bool _targetsPending;

    // A resume that came before the picture reached the freeze point: the picture aligns when it freezes there.
    private (TimeSpan ClockPosition, long ClockTimestamp)? _pendingAlign;

    public PlaybackSession(
        IAudioEngine engine,
        IOutputLagControl lags,
        IVideoRenderer renderer,
        IDeviceLatencyProbe probe,
        OffsetBook offsets,
        IAudioBackend backend,
        TimeProvider time,
        ILogger<PlaybackSession> logger)
    {
        _engine = engine;
        _lags = lags;
        _renderer = renderer;
        _probe = probe;
        _offsets = offsets;
        _backend = backend;
        _time = time;
        _logger = logger;
        _engine.Events.Subscribe(new EngineObserver(this));
    }

    public IObservable<SessionEvent> Events => _events;

    public PlaybackState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public Result Start(StartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var commandAt = _time.GetTimestamp();

        LoadOffsetsOnce();

        var catalog = new DeviceCatalog(_backend.ListDevices());
        var outputs = new List<(string DeviceId, DeviceType Type, int ReportedMs, int OffsetMs)>();
        foreach (var assignment in request.Outputs)
        {
            if (catalog.Find(assignment.Device) is not { } device)
            {
                return Result.Failure("outputs.device_not_found", $"{assignment.Device} is not a connected playback device");
            }

            var probed = _probe.Probe(device.Id);
            var (type, reportedMs) = probed.IsSuccess
                ? (DeviceTypeClassifier.Classify(probed.Value.Connection, probed.Value.FormFactor), (int)Math.Round(probed.Value.ReportedLatency.TotalMilliseconds))
                : (DeviceType.Other, 0);
            outputs.Add((device.Id, type, reportedMs, _offsets.Get(device.Id)));
        }

        var estimate = LatencyModel.Estimate(outputs);
        _request = request;
        _estimate = estimate;
        _events.Publish(new LatencyEstimated(estimate));
        foreach (var output in estimate.Outputs)
        {
            LogLatency(output.DeviceId, output.Type, output.ReportedMs, output.TypeDefaultMs, output.OffsetMs, output.AssumedMs, output.LagMs, estimate.PictureDelayMs);
        }

        var loaded = _renderer.Load(request.File, nint.Zero, startPaused: true);
        if (!loaded.IsSuccess)
        {
            return loaded;
        }

        var lags = estimate.Outputs.ToDictionary(o => o.DeviceId, o => TimeSpan.FromMilliseconds(o.LagMs), StringComparer.Ordinal);
        var started = _engine.StartRun(request.File, request.Outputs, new EngineOptions { InitialLags = lags });
        if (!started.IsSuccess)
        {
            return Result.Failure(started.Error);
        }

        // A lag command replies once the pre-roll is released and the master clock has started.
        var settled = _lags.SetLagTargets(lags, LagChangeMode.Ramp);
        if (!settled.IsSuccess)
        {
            _engine.Stop(RunEndReason.Stop);
            return settled;
        }

        var maxLag = TimeSpan.FromMilliseconds(estimate.Outputs.Select(o => o.LagMs).DefaultIfEmpty(0).Max());
        var frameZero = _lags.FrameZeroTimestamp ?? _time.GetTimestamp();
        var clockStart = frameZero - ToTimestamp(maxLag);
        var clock = new MasterClock(_time);
        clock.StartAt(clockStart);
        var pictureDelay = TimeSpan.FromMilliseconds(estimate.PictureDelayMs);
        var sync = new PictureSync(clock, _renderer, _time, pictureDelay);
        sync.AlignAt(TimeSpan.Zero, clockStart);

        var pictureAt = clockStart + ToTimestamp(pictureDelay);
        var heard = estimate.Outputs.ToDictionary(o => o.DeviceId, _ => pictureAt, StringComparer.Ordinal);
        _events.Publish(new StartTimings(commandAt, pictureAt, heard));

        PictureSync? previous;
        lock (_gate)
        {
            previous = _pictureSync;
            _pictureSync = sync;
            _clock = clock;
            _pictureFrozen = false;
            if (_state != PlaybackState.Ended)
            {
                _state = PlaybackState.Playing;
            }
        }

        previous?.Dispose();
        return Result.Success();
    }

    /// <summary>Steps an output's offset by <paramref name="deltaMs"/> (outputs are numbered from 1).</summary>
    public Result ChangeOffset(int outputNumber, int deltaMs)
    {
        PlaybackState state;
        LatencyEstimate? estimate;
        PictureSync? sync;
        MasterClock? clock;
        lock (_gate)
        {
            (state, estimate, sync, clock) = (_state, _estimate, _pictureSync, _clock);
        }

        if (state == PlaybackState.Idle || estimate is null || sync is null || clock is null)
        {
            return Result.Failure("playback.no_run", "No film is playing.");
        }

        if (outputNumber < 1 || outputNumber > estimate.Outputs.Count)
        {
            return Result.Success();
        }

        var deviceId = estimate.Outputs[outputNumber - 1].DeviceId;
        var oldOffset = estimate.Outputs[outputNumber - 1].OffsetMs;
        var step = _offsets.Step(deviceId, deltaMs);
        if (step.AtLimit)
        {
            _events.Publish(new NoteShown($"Output {outputNumber}: offset cannot go beyond ±{OffsetBook.LimitMs} ms", NoteStyle.Warning));
            return Result.Success();
        }

        var updated = LatencyModel.Estimate([.. estimate.Outputs.Select(o => (o.DeviceId, o.Type, o.ReportedMs, o.DeviceId == deviceId ? step.OffsetMs : o.OffsetMs))]);
        lock (_gate)
        {
            _estimate = updated;
        }

        _events.Publish(new OffsetChanged(outputNumber, deviceId, oldOffset, step.OffsetMs, null));
        _events.Publish(new NoteShown($"Output {outputNumber}: {step.OffsetMs:+0;-0;0} ms", NoteStyle.Info));
        var output = updated.Outputs[outputNumber - 1];
        LogLatency(output.DeviceId, output.Type, output.ReportedMs, output.TypeDefaultMs, output.OffsetMs, output.AssumedMs, output.LagMs, updated.PictureDelayMs);

        var clockPosition = clock.PositionAt(_time.GetTimestamp());
        _events.Publish(new PictureDelayChanged(clockPosition, PictureDelayCause.Offset, estimate.PictureDelayMs, updated.PictureDelayMs, updated.Outputs));

        if (state != PlaybackState.Playing)
        {
            lock (_gate)
            {
                _targetsPending = true;
            }

            return Result.Success();
        }

        var targets = updated.Outputs.ToDictionary(o => o.DeviceId, o => TimeSpan.FromMilliseconds(o.LagMs), StringComparer.Ordinal);
        var applied = _lags.SetLagTargets(targets, LagChangeMode.Ramp);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        sync.MoveTo(TimeSpan.FromMilliseconds(updated.PictureDelayMs));
        PublishTiming(clock, sync, _time.GetTimestamp());
        return Result.Success();
    }

    /// <summary>Applies offsets pressed while paused or ended at once; called on resume and seek.</summary>
    private void ApplyPendingTargets(PictureSync sync)
    {
        LatencyEstimate? estimate;
        lock (_gate)
        {
            if (!_targetsPending)
            {
                return;
            }

            _targetsPending = false;
            estimate = _estimate;
        }

        if (estimate is null)
        {
            return;
        }

        _lags.SetLagTargets(estimate.Outputs.ToDictionary(o => o.DeviceId, o => TimeSpan.FromMilliseconds(o.LagMs), StringComparer.Ordinal), LagChangeMode.AtOnce);
        sync.SetAtOnce(TimeSpan.FromMilliseconds(estimate.PictureDelayMs));
    }

    public Result Pause()
    {
        PictureSync sync;
        MasterClock clock;
        lock (_gate)
        {
            switch (_state)
            {
                case PlaybackState.Ended:
                case PlaybackState.Paused:
                    return Result.Success();
                case PlaybackState.Idle:
                    return Result.Failure("playback.no_run", "No film is playing.");
            }

            (sync, clock) = (_pictureSync!, _clock!);
        }

        var paused = _engine.Pause();
        if (!paused.IsSuccess)
        {
            return Result.Failure(paused.Error);
        }

        var freeze = paused.Value;
        clock.StopAt(_time.GetTimestamp());
        clock.JumpTo(freeze);
        var delay = sync.PictureDelay;

        // The sound already inside the devices plays out; the picture runs on until it reaches the freeze point.
        var remaining = _renderer.Presented is { } presented ? freeze - presented.Position : TimeSpan.Zero;
        long version;
        lock (_gate)
        {
            _state = PlaybackState.Paused;
            version = ++_freezeVersion;
            _freezeTimer?.Dispose();
            _freezeTimer = null;
            if (remaining > TimeSpan.Zero)
            {
                _freezeTimer = _time.CreateTimer(_ => FreezePicture(version, freeze), null, remaining, Timeout.InfiniteTimeSpan);
            }
        }

        if (remaining <= TimeSpan.Zero)
        {
            FreezePicture(version, freeze);
        }

        PublishDelay(freeze, PictureDelayCause.Pause, delay);
        return Result.Success();
    }

    public Result Resume()
    {
        PictureSync sync;
        MasterClock clock;
        lock (_gate)
        {
            switch (_state)
            {
                case PlaybackState.Ended:
                case PlaybackState.Playing:
                    return Result.Success();
                case PlaybackState.Idle:
                    return Result.Failure("playback.no_run", "No film is playing.");
            }

            (sync, clock) = (_pictureSync!, _clock!);
        }

        var resumed = _engine.Resume();
        if (!resumed.IsSuccess)
        {
            return Result.Failure(resumed.Error);
        }

        var position = resumed.Value;
        var now = _time.GetTimestamp();
        ApplyPendingTargets(sync);
        clock.JumpTo(position);
        clock.Start();
        var delay = sync.PictureDelay;
        lock (_gate)
        {
            // The freeze may have happened, or not, while the engine was resuming: decide on the state as it is now.
            // Sound from the freeze point is heard one picture delay from now, and so is the frame at the freeze point.
            var align = (position + delay, now + ToTimestamp(delay));
            if (_pictureFrozen)
            {
                CancelFreeze();
                sync.AlignAt(align.Item1, align.Item2);
            }
            else
            {
                // The picture is still running up to the freeze point: keep the freeze, align when it gets there.
                _pendingAlign = align;
            }

            _state = PlaybackState.Playing;
        }

        PublishDelay(position, PictureDelayCause.Resume, delay);
        PublishTiming(clock, sync, now);
        return Result.Success();
    }

    /// <summary>Seeks by <paramref name="step"/> relative to the current position (negative = backward).</summary>
    public Result Seek(TimeSpan step)
    {
        PlaybackState state;
        TimeSpan current;
        lock (_gate)
        {
            state = _state;
            current = state == PlaybackState.Ended ? _endPosition : _clock?.PositionAt(_time.GetTimestamp()) ?? TimeSpan.Zero;
        }

        switch (state)
        {
            case PlaybackState.Idle:
                return Result.Failure("playback.no_run", "No film is playing.");
            case PlaybackState.Ended:
                if (step >= TimeSpan.Zero || _request is null)
                {
                    return Result.Success();
                }

                // A backward seek at the end returns into the film: a new run that starts at the target.
                lock (_gate)
                {
                    _state = PlaybackState.Idle;
                }

                var restarted = Start(_request);
                return restarted.IsSuccess ? SeekTo(current + step) : restarted;
            default:
                return SeekTo(current + step);
        }
    }

    private Result SeekTo(TimeSpan target)
    {
        PictureSync sync;
        MasterClock clock;
        lock (_gate)
        {
            (sync, clock) = (_pictureSync!, _clock!);
        }

        var seeked = _engine.Seek(target);
        if (!seeked.IsSuccess)
        {
            return Result.Failure(seeked.Error);
        }

        ApplyPendingTargets(sync);
        var delay = sync.PictureDelay;
        if (State == PlaybackState.Ended)
        {
            // Past the end: stop the picture as at the natural end.
            clock.StopAt(_time.GetTimestamp());
            _renderer.Pause();
            return Result.Success();
        }

        var position = seeked.Value.Position;
        var now = _time.GetTimestamp();
        clock.JumpTo(position);
        if (seeked.Value.Paused)
        {
            long version;
            lock (_gate)
            {
                version = CancelFreeze();
            }

            FreezePicture(version, position);
        }
        else
        {
            lock (_gate)
            {
                CancelFreeze();
            }

            sync.AlignAt(position, now);
            PublishTiming(clock, sync, now);
        }

        PublishDelay(position, PictureDelayCause.Seek, delay);
        return Result.Success();
    }

    /// <summary>Cancels a pending picture freeze; the caller holds the gate. Returns the new version.</summary>
    private long CancelFreeze()
    {
        _freezeTimer?.Dispose();
        _freezeTimer = null;
        _pictureFrozen = false;
        _pendingAlign = null;
        return ++_freezeVersion;
    }

    // The renderer is driven under the gate (its calls do not block), so a freeze that fires on a timer thread
    // cannot interleave with a resume, a seek or another pause.
    private void FreezePicture(long version, TimeSpan position)
    {
        lock (_gate)
        {
            if (version != _freezeVersion)
            {
                return;
            }

            _renderer.Pause();
            _renderer.Seek(position);
            if (_pendingAlign is var (clockPosition, clockTimestamp) && _pictureSync is { } sync)
            {
                // Resumed before the picture got here: it holds this frame and starts it together with the sound.
                _pictureFrozen = false;
                _pendingAlign = null;
                _freezeTimer?.Dispose();
                _freezeTimer = null;
                sync.AlignAt(clockPosition, clockTimestamp);
                return;
            }

            _pictureFrozen = true;
        }
    }

    private void PublishDelay(TimeSpan clock, PictureDelayCause cause, TimeSpan delay)
    {
        var ms = (int)Math.Round(delay.TotalMilliseconds);
        _events.Publish(new PictureDelayChanged(clock, cause, ms, ms, _estimate?.Outputs ?? []));
    }

    private void PublishTiming(MasterClock clock, PictureSync sync, long now)
    {
        if (_renderer.Presented is not { } presented)
        {
            return;
        }

        var due = clock.PositionAt(now) - sync.PictureDelay;
        _events.Publish(new PictureTimingReported(clock.PositionAt(now), due, presented.Position, (presented.Position - due).TotalMilliseconds));
    }

    public void Exit()
    {
        _engine.Stop(RunEndReason.Stop);
        PictureSync? sync;
        lock (_gate)
        {
            CancelFreeze();
            sync = _pictureSync;
            _pictureSync = null;
            _state = PlaybackState.Ended;
        }

        sync?.Dispose();
        _renderer.Dispose();
    }

    /// <summary>Loads the remembered offsets on the first start of the process; later starts keep the in-memory book.</summary>
    private void LoadOffsetsOnce()
    {
        lock (_gate)
        {
            if (_offsetsLoaded)
            {
                return;
            }

            _offsetsLoaded = true;
        }

        foreach (var warning in _offsets.Load())
        {
            _events.Publish(new OffsetsWarning(warning));
        }
    }

    private long ToTimestamp(TimeSpan span) => (long)Math.Round(span.TotalSeconds * _time.TimestampFrequency);

    private void OnEngineEvent(EngineEvent engineEvent)
    {
        if (engineEvent is not RunSummary summary)
        {
            return;
        }

        lock (_gate)
        {
            _endPosition = summary.ClockPosition;
            _state = PlaybackState.Ended;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Latency for {DeviceId}: type {Type}, reported {ReportedMs} ms, default {DefaultMs} ms, offset {OffsetMs} ms, assumed {AssumedMs} ms, lag {LagMs} ms; picture delay {PictureDelayMs} ms")]
    private partial void LogLatency(string deviceId, DeviceType type, int reportedMs, int defaultMs, int offsetMs, int assumedMs, int lagMs, int pictureDelayMs);

    private sealed class EngineObserver(PlaybackSession owner) : IObserver<EngineEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(EngineEvent value) => owner.OnEngineEvent(value);
    }

    private sealed class SessionEventPublisher : IObservable<SessionEvent>
    {
        private readonly Lock _gate = new();
        private IObserver<SessionEvent>[] _observers = [];

        public IDisposable Subscribe(IObserver<SessionEvent> observer)
        {
            lock (_gate)
            {
                _observers = [.. _observers, observer];
            }

            return new Subscription(this, observer);
        }

        public void Publish(SessionEvent value)
        {
            IObserver<SessionEvent>[] snapshot;
            lock (_gate)
            {
                snapshot = _observers;
            }

            foreach (var observer in snapshot)
            {
                observer.OnNext(value);
            }
        }

        private void Remove(IObserver<SessionEvent> observer)
        {
            lock (_gate)
            {
                _observers = [.. _observers.Where(o => !ReferenceEquals(o, observer))];
            }
        }

        private sealed class Subscription(SessionEventPublisher owner, IObserver<SessionEvent> observer) : IDisposable
        {
            public void Dispose() => owner.Remove(observer);
        }
    }
}
