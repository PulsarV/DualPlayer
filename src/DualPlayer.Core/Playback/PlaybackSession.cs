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

/// <summary>
/// Use case: starts a lip-synced film, ends it at its natural end or on exit. Every command, timer callback, engine
/// event and renderer event runs on the session's own control thread, one at a time in posting order (sad.md §8,
/// threading): a command waits for the one before it, and an event or timer never waits for a command.
/// </summary>
public sealed partial class PlaybackSession : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly IOutputLagControl _lags;
    private readonly IVideoRenderer _renderer;
    private readonly IAudioDecoder _decoder;
    private readonly IPictureProbe _pictureProbe;
    private readonly IDeviceLatencyProbe _probe;
    private readonly OffsetBook _offsets;
    private readonly IAudioBackend _backend;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaybackSession> _logger;
    private readonly SessionEventPublisher _events = new();
    private readonly Lock _gate = new();
    private readonly EngineControlLoop _loop;

    // Which run the engine's events belong to: a summary or loss from a run the session already left is ignored.
    private long _runToken;
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
    private static readonly TimeSpan PictureProbeLimit = TimeSpan.FromSeconds(2);
    private readonly HashSet<string> _lost = new(StringComparer.Ordinal);
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(1);

    // "Lip-synced again": the picture presented within this of due (sad.md §10, quality scenario 2b).
    private const double LipSyncedMs = 20;

    // A frame presented more than this after its due time is late (spec §6 picture presentation).
    private const double LateMs = 20;
    private long _lateFrames;
    private TimeSpan? _lastFrameSeen;

    // How often the picture-sync loop looks at the presented frame and steers it (the D1 spike ticked every few ms;
    // mpv's snapshot changes once a frame, so a tick every 5 ms sees every frame).
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMilliseconds(5);
    private ITimer? _syncTimer;
    private int _syncTickPending;
    private bool _timingPending;

    // The command whose "lip-synced again" record is still owed (sad.md flow 4), and when it was given.
    private (string Command, long At)? _resync;

    // Start timings measured as they happen: the command time, then the first frame presented and each output's first audio.
    private (long CommandAt, IReadOnlyList<OutputLatency> Outputs)? _startTimings;

    // Offset changes not yet saved: recorded again with their save time once the debounced save lands.
    private readonly List<OffsetChanged> _unsaved = [];
    private const int TicksPerCounterReport = 60;
    private const string PictureFailedNote = "The picture could not be decoded further - film ended";
    private ITimer? _healthTimer;
    private bool _pictureFailed;
    private bool _starting;
    private string? _failureDuringStart;
    private int _healthTicks;
    private long _damagedSeen;
    private TimeSpan _previousTickClock;
    private (TimeSpan Start, TimeSpan LastGrowth, long Frames)? _stretch;

    // A resume that came before the picture reached the freeze point: the picture aligns when it freezes there.
    private (TimeSpan ClockPosition, long ClockTimestamp)? _pendingAlign;

    public PlaybackSession(
        IAudioEngine engine,
        IOutputLagControl lags,
        IVideoRenderer renderer,
        IAudioDecoder decoder,
        IPictureProbe pictureProbe,
        IDeviceLatencyProbe probe,
        OffsetBook offsets,
        IAudioBackend backend,
        TimeProvider time,
        ILogger<PlaybackSession> logger)
    {
        _engine = engine;
        _lags = lags;
        _renderer = renderer;
        _decoder = decoder;
        _pictureProbe = pictureProbe;
        _probe = probe;
        _offsets = offsets;
        _backend = backend;
        _time = time;
        _logger = logger;
        _loop = new EngineControlLoop(static () => { }, LogLoopFault, "PlaybackSession");
        _engine.Events.Subscribe(new EngineObserver(this));
        _renderer.DecodeFailed += error => _loop.Post(() => OnPictureFailed(error.Message));
        _renderer.HardwareDecodingInactive += () => _loop.Post(() => OnPictureFailed("Hardware decoding is not active"));
        _offsets.Saved += savedAt => _loop.Post(() => OnOffsetsSaved(savedAt));
    }

    public IObservable<SessionEvent> Events => _events;

    /// <summary>
    /// Gets the current file's frame counters: dropped and skipped-damaged from the renderer, late counted here — a frame
    /// presented more than 20 ms after its due time, which follows the picture delay (spec §6).
    /// </summary>
    public FrameCounters Counters
    {
        get
        {
            lock (_gate)
            {
                return _renderer.Counters with { Late = _lateFrames };
            }
        }
    }

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
        return Run(() => StartCore(request));
    }

    /// <summary>Waits until every command and event posted so far has run.</summary>
    public void Flush() => _loop.TryExecute(static () => { });

    public void Dispose() => _loop.Dispose();

    private Result Run(Func<Result> command) =>
        _loop.TryExecute(command, out var result) ? result : Result.Failure("playback.no_run", "No film is playing.");

    /// <summary>
    /// Starts <paramref name="request"/>. A device named by its list number is resolved once, on the first start; the
    /// session then keeps its OS id, so a later start (a file change) plays each track on the same device even when the
    /// list has been renumbered. With <paramref name="keepLost"/> the outputs lost so far stay lost and keep their numbers.
    /// </summary>
    private Result StartCore(StartRequest request, bool keepLost = false)
    {
        var commandAt = request.CommandTimestamp ?? _time.GetTimestamp();

        LoadOffsetsOnce();

        HashSet<string> lost;
        LatencyEstimate? previous;
        lock (_gate)
        {
            lost = keepLost ? new HashSet<string>(_lost, StringComparer.Ordinal) : [];
            previous = _estimate;
        }

        var catalog = new DeviceCatalog(_backend.ListDevices());
        var resolved = new List<OutputAssignment>();
        var playing = new List<OutputAssignment>();
        var outputs = new List<(string DeviceId, DeviceType Type, int ReportedMs, int OffsetMs)>();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assignment in request.Outputs)
        {
            if (assignment.Device.Id is { } lostId && lost.Contains(lostId))
            {
                resolved.Add(assignment);
                continue;
            }

            if (catalog.Find(assignment.Device) is not { } device)
            {
                return Result.Failure("outputs.device_not_found", $"{assignment.Device} is not a connected playback device");
            }

            var byId = assignment with { Device = DeviceRef.ById(device.Id) };
            resolved.Add(byId);
            playing.Add(byId);
            var probed = _probe.Probe(device.Id);
            if (probed.IsSuccess)
            {
                sources[device.Id] = probed.Value.Source;
            }
            else
            {
                // ADR-0004: no reported part and an unknown type; the fallback is said, not silent.
                sources[device.Id] = $"probe failed ({probed.Error.Code}): reported 0 ms, type Other";
                LogProbeFailed(device.Id, probed.Error.Code, probed.Error.Message);
            }

            var (type, reportedMs) = probed.IsSuccess
                ? (DeviceTypeClassifier.Classify(probed.Value.Connection, probed.Value.FormFactor), (int)Math.Round(probed.Value.ReportedLatency.TotalMilliseconds))
                : (DeviceType.Other, 0);
            outputs.Add((device.Id, type, reportedMs, _offsets.Get(device.Id)));
        }

        if (playing.Count == 0)
        {
            return Result.Failure("outputs.all_lost", "Every output has been lost.");
        }

        var estimated = LatencyModel.Estimate(outputs);
        var estimate = new LatencyEstimate([.. estimated.Outputs.Select(o => o with { Source = sources[o.DeviceId] })], estimated.PictureDelayMs);

        // Lost outputs keep their place (and so their output number and keys) with the estimate they had when lost.
        var full = new LatencyEstimate(
            [.. resolved.Select(a => estimate.Outputs.FirstOrDefault(o => o.DeviceId == a.Device.Id) ?? previous!.Outputs.First(o => o.DeviceId == a.Device.Id))],
            estimate.PictureDelayMs);
        _request = request with { Outputs = resolved };
        lock (_gate)
        {
            _estimate = full;
        }

        _events.Publish(new LatencyEstimated(full));
        foreach (var output in estimate.Outputs)
        {
            LogLatency(output.DeviceId, output.Type, output.ReportedMs, output.Source, output.TypeDefaultMs, output.OffsetMs, output.AssumedMs, output.LagMs, estimate.PictureDelayMs);
        }

        return Starting(() => StartRun(request with { Outputs = playing }, estimate, commandAt, lost, returning: false));
    }

    // A picture failure the renderer raises while the run is still starting is only recorded; the start ends the run.
    private Result Starting(Func<Result> start)
    {
        lock (_gate)
        {
            _starting = true;
            _pictureFailed = false;
            _failureDuringStart = null;
        }

        try
        {
            return start();
        }
        finally
        {
            lock (_gate)
            {
                _starting = false;
                _failureDuringStart = null;
            }
        }
    }

    /// <summary>
    /// After the end, a new engine run for the same file and outputs (the engine cannot reopen a run that has ended). The
    /// file stays loaded in the renderer and its picture stays paused, so no frame 0 is shown and no start lines repeat;
    /// the caller seeks the new run to its target at once.
    /// </summary>
    private Result ReturnIntoFilm()
    {
        StartRequest? request;
        LatencyEstimate? estimate;
        HashSet<string> lost;
        lock (_gate)
        {
            (request, estimate) = (_request, _estimate);
            lost = new HashSet<string>(_lost, StringComparer.Ordinal);
        }

        if (request is null || estimate is null)
        {
            return Result.Failure("playback.no_run", "No film is playing.");
        }

        var playing = request.Outputs.Where(a => a.Device.Id is not { } id || !lost.Contains(id)).ToList();
        if (playing.Count == 0)
        {
            return Result.Failure("outputs.all_lost", "Every output has been lost.");
        }

        var playingEstimate = new LatencyEstimate([.. estimate.Outputs.Where(o => !lost.Contains(o.DeviceId))], estimate.PictureDelayMs);
        return Starting(() => StartRun(request with { Outputs = playing }, playingEstimate, _time.GetTimestamp(), lost, returning: true));
    }

    private Result StartRun(StartRequest request, LatencyEstimate estimate, long commandAt, HashSet<string> lost, bool returning)
    {
        if (!returning)
        {
            var loaded = _renderer.Load(request.File, nint.Zero, startPaused: true);
            if (!loaded.IsSuccess)
            {
                return loaded;
            }
        }

        var lags = estimate.Outputs.ToDictionary(o => o.DeviceId, o => TimeSpan.FromMilliseconds(o.LagMs), StringComparer.Ordinal);
        Interlocked.Increment(ref _runToken);
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
        if (!returning)
        {
            sync.AlignAt(TimeSpan.Zero, clockStart);
            lock (_gate)
            {
                // Measured by the sync ticks: the first frame on screen and each output's first audio plus its latency.
                _startTimings = (commandAt, estimate.Outputs);
                _resync = ("start", commandAt);
            }
        }

        PictureSync? previous;
        string? failure;
        lock (_gate)
        {
            previous = _pictureSync;
            _pictureSync = sync;
            _clock = clock;
            _pictureFrozen = false;
            _lost.Clear();
            _lost.UnionWith(lost);
            _healthTicks = 0;
            _damagedSeen = _renderer.Counters.SkippedDamaged;
            if (!returning)
            {
                _lateFrames = 0; // per file, as the renderer's own counters
                _lastFrameSeen = null;
            }

            _previousTickClock = clock.PositionAt(_time.GetTimestamp());
            _stretch = null;
            _healthTimer?.Dispose();
            _healthTimer = _time.CreateTimer(_ => _loop.Post(OnHealthTick), null, HealthInterval, HealthInterval);
            _syncTimer?.Dispose();
            _syncTimer = _time.CreateTimer(_ => PostSyncTick(), null, SyncInterval, SyncInterval);
            if (_state != PlaybackState.Ended)
            {
                _state = PlaybackState.Playing;
            }

            _starting = false;
            failure = _failureDuringStart;
            _failureDuringStart = null;
        }

        previous?.Dispose();
        if (failure is not null)
        {
            EndOnPictureFailure(failure);
        }

        return Result.Success();
    }

    /// <summary>Moves to the next playable file in the folder (T17).</summary>
    public Result NextFile() => Run(() => ChangeFile(NeighbourDirection.Next));

    /// <summary>Moves to the previous playable file in the folder (T17).</summary>
    public Result PreviousFile() => Run(() => ChangeFile(NeighbourDirection.Previous));

    private Result ChangeFile(NeighbourDirection direction)
    {
        var commandAt = _time.GetTimestamp();
        StartRequest? request;
        LatencyEstimate? estimateBefore;
        lock (_gate)
        {
            request = _state == PlaybackState.Idle ? null : _request;
            estimateBefore = _estimate;
        }

        if (request is null)
        {
            return Result.Failure("playback.no_run", "No film is playing.");
        }

        // The current film is untouched while the search runs.
        string? target = null;
        string? lastSkipped = null;
        FileSkipped? lastSkip = null;
        var skipped = 0;
        try
        {
            foreach (var candidate in FolderNeighbours.Candidates(request.File, direction))
            {
                var skip = CheckPlayable(candidate, request);
                if (skip is null)
                {
                    target = candidate;
                    break;
                }

                skipped++;
                lastSkipped = Path.GetFileName(candidate);
                lastSkip = skip;
                _events.Publish(skip);
                LogSkipped(lastSkipped, skip.Reason, skip.OutputNumber, skip.Track);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder has no neighbours.
        }

        if (skipped > 0)
        {
            var reason = SkipText(lastSkip!);
            _events.Publish(new NoteShown(
                skipped == 1 ? $"Skipped {lastSkipped}: {reason}" : $"Skipped {skipped} files; last: {lastSkipped} - {reason}",
                NoteStyle.Warning));
        }

        var isNext = direction == NeighbourDirection.Next;
        if (target is null)
        {
            _events.Publish(new NoNeighbour(isNext));
            _events.Publish(new NoteShown(isNext ? "No next file" : "No previous file", NoteStyle.Info));
            return Result.Success();
        }

        lock (_gate)
        {
            CancelFreeze();
        }

        var previousDelay = estimateBefore?.PictureDelayMs ?? 0;
        _engine.Stop(RunEndReason.FileChange);
        lock (_gate)
        {
            _state = PlaybackState.Idle;
        }

        var started = StartCore(request with { File = target }, keepLost: true);
        if (!started.IsSuccess)
        {
            // The old film has stopped and the new one cannot play: end here, say why, and keep answering keys.
            var note = $"Could not play {Path.GetFileName(target)}: {started.Error.Message}";
            lock (_gate)
            {
                _endPosition = TimeSpan.Zero;
                _state = PlaybackState.Ended;
            }

            _renderer.Pause();
            LogFileChangeFailed(target, started.Error.Code, started.Error.Message);
            _events.Publish(new NoteShown(note, NoteStyle.Warning));
            return started;
        }

        _events.Publish(new FileChanged(target, request.File));
        LatencyEstimate? now;
        lock (_gate)
        {
            now = _estimate;
            _resync = ("file-change", commandAt);
        }

        if (now is not null)
        {
            _events.Publish(new PictureDelayChanged(TimeSpan.Zero, PictureDelayCause.FileChange, previousDelay, now.PictureDelayMs, now.Outputs));
        }

        return Result.Success();
    }

    private FileSkipped? CheckPlayable(string path, StartRequest request)
    {
        var name = Path.GetFileName(path);
        var tracks = _decoder.ListTracks(path);
        if (!tracks.IsSuccess)
        {
            return new FileSkipped(name, ReasonOf(tracks.Error.Code), null, null);
        }

        for (var i = 0; i < request.Outputs.Count; i++)
        {
            var track = request.Outputs[i].TrackNumber;
            if (!tracks.Value.Any(t => t.Number == track))
            {
                return new FileSkipped(name, "track-missing", i + 1, track);
            }
        }

        var picture = _pictureProbe.Probe(path, PictureProbeLimit);
        return picture.IsSuccess ? null : new FileSkipped(name, ReasonOf(picture.Error.Code), null, null);
    }

    private static string ReasonOf(string code) => code switch
    {
        "playback.file_access_denied" => "access-denied",
        "video.no_picture" => "no-picture",
        "video.not_hardware_decodable" => "not-hardware-decodable",
        _ => "unreadable",
    };

    private static string SkipText(FileSkipped skip) => skip.Reason switch
    {
        "track-missing" => $"no track {skip.Track} for output {skip.OutputNumber}",
        "access-denied" => "access denied",
        "no-picture" => "no picture",
        "not-hardware-decodable" => "picture cannot be played on this PC",
        _ => "file cannot be read",
    };

    /// <summary>Steps an output's offset by <paramref name="deltaMs"/> (outputs are numbered from 1).</summary>
    public Result ChangeOffset(int outputNumber, int deltaMs) => Run(() => ChangeOffsetCore(outputNumber, deltaMs));

    private Result ChangeOffsetCore(int outputNumber, int deltaMs)
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
        lock (_gate)
        {
            if (_lost.Contains(deviceId))
            {
                return Result.Success();
            }
        }

        var oldOffset = estimate.Outputs[outputNumber - 1].OffsetMs;
        var step = _offsets.Step(deviceId, deltaMs);
        if (step.AtLimit)
        {
            _events.Publish(new NoteShown($"Output {outputNumber}: offset cannot go beyond ±{OffsetBook.LimitMs} ms", NoteStyle.Warning));
            return Result.Success();
        }

        LatencyEstimate updated;
        lock (_gate)
        {
            updated = LatencyModel.Estimate([.. estimate.Outputs.Where(o => !_lost.Contains(o.DeviceId)).Select(o => (o.DeviceId, o.Type, o.ReportedMs, o.DeviceId == deviceId ? step.OffsetMs : o.OffsetMs))]);
            _estimate = new LatencyEstimate([.. estimate.Outputs.Select(o => updated.Outputs.FirstOrDefault(u => u.DeviceId == o.DeviceId) ?? o)], updated.PictureDelayMs);
        }

        var changed = new OffsetChanged(outputNumber, deviceId, oldOffset, step.OffsetMs, null);
        lock (_gate)
        {
            _unsaved.RemoveAll(c => c.DeviceId == deviceId);
            _unsaved.Add(changed);
        }

        _events.Publish(changed);
        _events.Publish(new NoteShown($"Output {outputNumber}: {step.OffsetMs:+0;-0;0} ms", NoteStyle.Info));
        var output = updated.Outputs.First(o => o.DeviceId == deviceId);
        LogLatency(output.DeviceId, output.Type, output.ReportedMs, output.Source, output.TypeDefaultMs, output.OffsetMs, output.AssumedMs, output.LagMs, updated.PictureDelayMs);

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
        ReportTiming(sync);
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

    public Result Pause() => Run(PauseCore);

    private Result PauseCore()
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
        var atKey = sync.Measure();
        clock.StopAt(_time.GetTimestamp());
        clock.JumpTo(freeze);
        var delay = sync.PictureDelay;

        // The sound already inside the devices plays out; the picture runs on until it reaches the freeze point. The
        // snapshot is from when the frame was presented: project it to now.
        var remaining = _renderer.Presented is { } presented ? freeze - ProjectToNow(presented) : TimeSpan.Zero;
        long version;
        lock (_gate)
        {
            _state = PlaybackState.Paused;
            version = ++_freezeVersion;
            _freezeTimer?.Dispose();
            _freezeTimer = null;

            // A resume that has not yet aligned the picture is overtaken by this pause: the next resume aligns it.
            _pendingAlign = null;
            if (remaining > TimeSpan.Zero)
            {
                _freezeTimer = _time.CreateTimer(_ => _loop.Post(() => FreezePicture(version, freeze)), null, remaining, Timeout.InfiniteTimeSpan);
            }
        }

        if (remaining <= TimeSpan.Zero)
        {
            FreezePicture(version, freeze);
        }

        PublishDelay(freeze, PictureDelayCause.Pause, delay);
        if (atKey is { } timing)
        {
            Publish(timing);
        }
        return Result.Success();
    }

    public Result Resume() => Run(ResumeCore);

    private Result ResumeCore()
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

        lock (_gate)
        {
            _resync = ("resume", now);
        }

        PublishDelay(position, PictureDelayCause.Resume, delay);
        ReportTiming(sync);
        return Result.Success();
    }

    /// <summary>Seeks by <paramref name="step"/> relative to the current position (negative = backward).</summary>
    public Result Seek(TimeSpan step) => Run(() => SeekCore(step));

    private Result SeekCore(TimeSpan step)
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

                // A backward seek at the end returns into the film at the target, its picture held until then.
                lock (_gate)
                {
                    _state = PlaybackState.Idle;
                }

                var target = current + step;
                var restarted = ReturnIntoFilm();
                return restarted.IsSuccess ? SeekTo(target < TimeSpan.Zero ? TimeSpan.Zero : target) : restarted;
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
        if (seeked.Value.OutsideFile && target > TimeSpan.Zero)
        {
            // Past the end: the engine has ended the run; stop the picture as at the natural end.
            lock (_gate)
            {
                _endPosition = seeked.Value.Position;
                _state = PlaybackState.Ended;
                CancelFreeze();
            }

            clock.StopAt(_time.GetTimestamp());
            _renderer.Pause();
            _renderer.Seek(seeked.Value.Position); // the last frame (mpv keep-open), not the frame shown at the key
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
            lock (_gate)
            {
                _resync = ("seek", now);
            }

            ReportTiming(sync);
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

    private TimeSpan ProjectToNow(PresentedPicture presented)
    {
        var now = _time.GetTimestamp();
        return presented.Timestamp >= now
            ? presented.Position
            : presented.Position + _time.GetElapsedTime(presented.Timestamp, now);
    }

    // The picture is measured against the due position at the snapshot's own time, never against now (review F11).
    // While the picture is still held for a scheduled start there is nothing to measure yet: the first tick that can
    // measure it reports it.
    private void ReportTiming(PictureSync sync)
    {
        if (sync.Measure() is { } timing)
        {
            Publish(timing);
            return;
        }

        lock (_gate)
        {
            _timingPending = true;
        }
    }

    private void Publish(PictureTiming timing) =>
        _events.Publish(new PictureTimingReported(timing.Clock, timing.Due, timing.Presented, timing.ErrorMs));

    // At most one tick waits on the session thread: ticks that fall due while a command runs are not queued up.
    private void PostSyncTick()
    {
        if (Interlocked.Exchange(ref _syncTickPending, 1) == 0)
        {
            _loop.Post(OnSyncTick);
        }
    }

    private void OnSyncTick()
    {
        Volatile.Write(ref _syncTickPending, 0);
        PictureSync? sync;
        lock (_gate)
        {
            sync = _state == PlaybackState.Playing ? _pictureSync : null;
        }

        if (sync?.Tick() is not { } timing)
        {
            return;
        }

        bool report;
        (string Command, long At)? resynced = null;
        (long CommandAt, IReadOnlyList<OutputLatency> Outputs)? starting;
        lock (_gate)
        {
            report = _timingPending;
            _timingPending = false;

            // Every frame is judged once, by the first tick that sees it on screen.
            if (timing.Presented != _lastFrameSeen)
            {
                _lastFrameSeen = timing.Presented;
                if (timing.ErrorMs < -LateMs)
                {
                    _lateFrames++;
                }
            }

            if (_resync is { } owed && Math.Abs(timing.ErrorMs) <= LipSyncedMs && timing.Timestamp >= owed.At)
            {
                resynced = owed;
                _resync = null;
            }

            starting = _startTimings;
        }

        if (report)
        {
            Publish(timing);
        }

        if (resynced is var (command, at))
        {
            _events.Publish(new LipSyncedAgain(timing.Clock, command, _time.GetElapsedTime(at, timing.Timestamp).TotalMilliseconds, timing.ErrorMs));
        }

        if (starting is var (commandAt, outputs) && timing.Presented > TimeSpan.Zero)
        {
            PublishStartTimings(commandAt, outputs, timing);
        }
    }

    // The picture moving from frame 0 dates its first present; each output is heard its assumed latency after its
    // first audio. Published once every output has handed over its first audio.
    private void PublishStartTimings(long commandAt, IReadOnlyList<OutputLatency> outputs, PictureTiming timing)
    {
        var heard = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            if (_lags.FirstAudioTimestamp(output.DeviceId) is not { } given)
            {
                return; // not every output has started yet: a later tick tries again
            }

            heard[output.DeviceId] = given + ToTimestamp(TimeSpan.FromMilliseconds(output.AssumedMs));
        }

        lock (_gate)
        {
            _startTimings = null;
        }

        var firstFrame = timing.Timestamp - ToTimestamp(timing.Presented);
        _events.Publish(new StartTimings(commandAt, firstFrame, heard));
    }

    private void OnOffsetsSaved(DateTimeOffset savedAt)
    {
        OffsetChanged[] saved;
        lock (_gate)
        {
            saved = [.. _unsaved];
            _unsaved.Clear();
        }

        foreach (var change in saved)
        {
            _events.Publish(change with { SavedAt = savedAt });
        }
    }

    private void OnPictureFailed(string reason)
    {
        lock (_gate)
        {
            if (_pictureFailed || _state == PlaybackState.Ended)
            {
                return;
            }

            if (_starting)
            {
                _pictureFailed = true;
                _failureDuringStart = reason;
                return;
            }
        }

        EndOnPictureFailure(reason);
    }

    private void EndOnPictureFailure(string reason)
    {
        TimeSpan position;
        lock (_gate)
        {
            if (_state == PlaybackState.Ended)
            {
                return;
            }

            _pictureFailed = true;
            position = _clock?.PositionAt(_time.GetTimestamp()) ?? TimeSpan.Zero;
            _endPosition = position;
            _state = PlaybackState.Ended;
            CancelFreeze();
            _healthTimer?.Dispose();
            _healthTimer = null;
            _syncTimer?.Dispose();
            _syncTimer = null;
        }

        _renderer.Pause();
        _events.Publish(new PictureFailed(position, reason));
        _events.Publish(new NoteShown(PictureFailedNote, NoteStyle.Warning));
        _renderer.ShowNote(PictureFailedNote, NoteStyle.Warning);
        _engine.Stop(RunEndReason.PictureFailed);
    }

    // Once a second: the picture timing, the damaged-stretch book-keeping and, every minute, the frame counters.
    private void OnHealthTick()
    {
        MasterClock? clock;
        PictureSync? sync;
        PlaybackState state;
        int ticks;
        lock (_gate)
        {
            (clock, sync, state) = (_clock, _pictureSync, _state);
            ticks = ++_healthTicks;
        }

        if (clock is null || sync is null || state == PlaybackState.Ended)
        {
            return;
        }

        var now = _time.GetTimestamp();
        var position = clock.PositionAt(now);
        var counters = Counters;
        DamagedStretchSkipped? closed = null;
        lock (_gate)
        {
            if (counters.SkippedDamaged > _damagedSeen)
            {
                var frames = (_stretch?.Frames ?? 0) + counters.SkippedDamaged - _damagedSeen;
                _stretch = (_stretch?.Start ?? _previousTickClock, position, frames);
            }
            else if (_stretch is var (start, lastGrowth, total))
            {
                closed = new DamagedStretchSkipped(start, lastGrowth - start, total);
                _stretch = null;
            }

            _damagedSeen = counters.SkippedDamaged;
            _previousTickClock = position;
        }

        if (closed is not null)
        {
            _events.Publish(closed);
        }

        if (state == PlaybackState.Playing)
        {
            ReportTiming(sync);
        }

        if (ticks % TicksPerCounterReport == 0)
        {
            _events.Publish(new FrameCountersReported(position, counters));
        }
    }

    public void Exit() => _loop.TryExecute(ExitCore);

    private void ExitCore()
    {
        _engine.Stop(RunEndReason.Stop);
        PictureSync? sync;
        lock (_gate)
        {
            CancelFreeze();
            _healthTimer?.Dispose();
            _healthTimer = null;
            _syncTimer?.Dispose();
            _syncTimer = null;
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

    private void OnEngineEvent(EngineEvent engineEvent, long runToken)
    {
        if (runToken != Interlocked.Read(ref _runToken))
        {
            return; // from a run the session has already left
        }

        if (engineEvent is DeviceLost lost)
        {
            OnDeviceLost(lost);
            return;
        }

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

    private void OnDeviceLost(DeviceLost lost)
    {
        LatencyEstimate? estimate;
        PictureSync? sync;
        MasterClock? clock;
        int number;
        lock (_gate)
        {
            (estimate, sync, clock) = (_estimate, _pictureSync, _clock);
            number = estimate is null ? lost.OutputIndex + 1 : estimate.Outputs.ToList().FindIndex(o => o.DeviceId == lost.DeviceId) + 1;
            if (number < 1)
            {
                number = lost.OutputIndex + 1;
            }

            _lost.Add(lost.DeviceId);
        }

        _events.Publish(new OutputLost(number, lost.DeviceId, lost.ClockPosition, lost.OthersContinue));

        if (!lost.OthersContinue)
        {
            lock (_gate)
            {
                CancelFreeze();
            }

            _renderer.Pause();
            _events.Publish(new NoteShown($"Output {number} lost - last output, film ended", NoteStyle.Warning));
            return;
        }

        _events.Publish(new NoteShown($"Output {number} lost", NoteStyle.Warning));
        if (estimate is null || sync is null || clock is null)
        {
            return;
        }

        LatencyEstimate updated;
        PlaybackState state;
        lock (_gate)
        {
            updated = LatencyModel.Estimate([.. estimate.Outputs.Where(o => !_lost.Contains(o.DeviceId)).Select(o => (o.DeviceId, o.Type, o.ReportedMs, o.OffsetMs))]);
            _estimate = new LatencyEstimate([.. estimate.Outputs.Select(o => updated.Outputs.FirstOrDefault(u => u.DeviceId == o.DeviceId) ?? o)], updated.PictureDelayMs);
            state = _state;
        }

        if (updated.PictureDelayMs == estimate.PictureDelayMs)
        {
            return;
        }

        _events.Publish(new PictureDelayChanged(lost.ClockPosition, PictureDelayCause.DeviceLost, estimate.PictureDelayMs, updated.PictureDelayMs, updated.Outputs));
        if (state != PlaybackState.Playing)
        {
            lock (_gate)
            {
                _targetsPending = true;
            }

            return;
        }

        _lags.SetLagTargets(updated.Outputs.ToDictionary(o => o.DeviceId, o => TimeSpan.FromMilliseconds(o.LagMs), StringComparer.Ordinal), LagChangeMode.Ramp);
        sync.MoveTo(TimeSpan.FromMilliseconds(updated.PictureDelayMs));
        ReportTiming(sync);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Latency for {DeviceId}: type {Type}, reported {ReportedMs} ms ({Source}), default {DefaultMs} ms, offset {OffsetMs} ms, assumed {AssumedMs} ms, lag {LagMs} ms; picture delay {PictureDelayMs} ms")]
    private partial void LogLatency(string deviceId, DeviceType type, int reportedMs, string? source, int defaultMs, int offsetMs, int assumedMs, int lagMs, int pictureDelayMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Latency probe failed for {DeviceId} ({Code}): {Message}; reported 0 ms, type Other")]
    private partial void LogProbeFailed(string deviceId, string code, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Playback session command or callback failed")]
    private partial void LogLoopFault(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not change to {File}: {Code} {Message}")]
    private partial void LogFileChangeFailed(string file, string code, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {File}: {Reason} (output {OutputNumber}, track {Track})")]
    private partial void LogSkipped(string file, string reason, int? outputNumber, int? track);

    private sealed class EngineObserver(PlaybackSession owner) : IObserver<EngineEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        // Runs on the engine's thread: read which run the event belongs to now, handle it on the session's thread.
        public void OnNext(EngineEvent value)
        {
            var runToken = Interlocked.Read(ref owner._runToken);
            owner._loop.Post(() => owner.OnEngineEvent(value, runToken));
        }
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
