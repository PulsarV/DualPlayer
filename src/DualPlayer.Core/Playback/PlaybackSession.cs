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

        lock (_gate)
        {
            _pictureSync = sync;
            if (_state != PlaybackState.Ended)
            {
                _state = PlaybackState.Playing;
            }
        }

        return Result.Success();
    }

    public Result Pause()
    {
        if (State == PlaybackState.Ended)
        {
            return Result.Success();
        }

        return Result.Failure("playback.pause_unsupported", "Pausing is not available yet.");
    }

    public void Exit()
    {
        _engine.Stop(RunEndReason.Stop);
        PictureSync? sync;
        lock (_gate)
        {
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
        if (engineEvent is not RunSummary)
        {
            return;
        }

        lock (_gate)
        {
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
