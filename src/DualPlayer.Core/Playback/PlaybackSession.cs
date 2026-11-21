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

    // Which film the renderer's events belong to: one raised by the film a file change left is dropped. It changes when
    // the old film is left, not when the engine run starts, so a failure of the new film during its start still counts.
    private long _filmToken;
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

    // One Page Down/Up press is answered within this, however many neighbours are slow (spec: 3 s key budget).
    private static readonly TimeSpan NeighbourScanBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProbeTimingSlack = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MinProbeTime = TimeSpan.FromMilliseconds(100);
    private readonly HashSet<string> _lost = new(StringComparer.Ordinal);

    // Files whose listing or probe the session gave up on since the last successful file change (AC-09b); the next press
    // steps past them instead of rescanning them. Touched only on the session's control thread.
    private readonly HashSet<string> _timedOut = new(StringComparer.Ordinal);
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(1);

    // "Lip-synced again": the picture presented within this of due (sad.md §10, quality scenario 2b).
    private const double LipSyncedMs = 20;

    // ... and every output still playing within this of the clock (spec §6 recovery row, AC-17).
    private const double OutputLipSyncedMs = 10;

    // The engine's output readings change about every 100 ms: asked no oftener than that while a record is owed.
    private static readonly TimeSpan OutputPollInterval = TimeSpan.FromMilliseconds(100);

    // Recovery gives up after this (spec §6): 1 s; the 3 s start budget for a start or a file change.
    private static readonly TimeSpan RecoveryBudget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StartRecoveryBudget = TimeSpan.FromSeconds(3);
    private long _outputsPolledAt;

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
    // At is the key's arrival (the origin of the elapsed time and of the give-up budget).
    private (string Command, long At)? _resync;

    // Output readings count as fresh only when taken at or after this: the instant the engine's command returned, not the
    // key's arrival, since the engine never clears a reading on a seek and one taken in between is still from before it.
    private long _readingsAfter;

    // The latest picture error measured (timestamp, ms): a give-up reports it when it was taken after the command.
    private (long At, double ErrorMs)? _lastMeasured;

    // Start timings measured as they happen: the command time, then the first frame presented and each output's first audio.
    private (long CommandAt, IReadOnlyList<OutputLatency> Outputs)? _startTimings;

    // Offset changes not yet saved: recorded again with their save time once the debounced save lands.
    private readonly List<OffsetChanged> _unsaved = [];
    private const int TicksPerCounterReport = 60;
    private static readonly TimeSpan ReloadTimeout = TimeSpan.FromSeconds(3);
    private const string PictureFailedNote = "The picture could not be decoded further - film ended";
    private ITimer? _healthTimer;
    private bool _pictureFailed;
    private bool _lastFrameReloaded;
    private bool _exited;

    // Whether a run of the film _request names has started (so ← at the end can return into it): cleared as soon as a
    // file change stops the old run, set once the engine has started the new file, so a change that fails before the
    // picture loads, to load or to start leaves none.
    private bool _filmLoaded;
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
        _renderer.DecodeFailed += error => PostRendererEvent(() => OnPictureFailed(error.Message, reloadLastFrame: true));
        _renderer.HardwareDecodingInactive += () => PostRendererEvent(() => OnPictureFailed("Hardware decoding is not active"));
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

    // Exit is terminal: a command still queued behind it (or behind an Alt+F4 exit) does nothing, not even a state read.
    private Result Run(Func<Result> command) =>
        _loop.TryExecute(() => _exited ? NoRun() : command(), out var result) ? result : NoRun();

    private static Result NoRun() => Result.Failure("playback.no_run", "No film is playing.");

    /// <summary>
    /// Starts <paramref name="request"/>. A device named by its list number is resolved once, on the first start; the
    /// session then keeps its OS id, so a later start (a file change) plays each track on the same device even when the
    /// list has been renumbered. With <paramref name="missingIsLost"/> a device missing from the list stays lost instead of failing the start. With <paramref name="keepLost"/> the outputs lost so far stay lost and keep their numbers.
    /// </summary>
    private Result StartCore(StartRequest request, bool keepLost = false, TimeSpan? returnTo = null, bool missingIsLost = false)
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
                // After every output was lost, a device still missing stays lost (with its number and estimate) instead of failing.
                if (missingIsLost && assignment.Device.Id is { } missingId && previous?.Outputs.Any(o => o.DeviceId == missingId) == true)
                {
                    lost.Add(missingId);
                    resolved.Add(assignment);
                    continue;
                }

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
                sources[device.Id] = $"probe failed ({probed.Error.Code}): reported 0 ms, type {DeviceTypeNames.Of(DeviceType.Other)}";
                LogProbeFailed(device.Id, probed.Error.Code, probed.Error.Message, DeviceTypeNames.Of(DeviceType.Other));
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

        // Lost outputs keep their place (and so their output number and keys), marked lost: the picture delay is worked from the
        // playing outputs only, so the assumed latency and lag they had when lost would not agree with it.
        var full = new LatencyEstimate(
            [.. resolved.Select((a, i) => estimate.Outputs.FirstOrDefault(o => o.DeviceId == a.Device.Id) is { } playingOutput
                ? playingOutput with { OutputNumber = i + 1 }
                : previous!.Outputs.First(o => o.DeviceId == a.Device.Id) with { OutputNumber = i + 1, IsLost = true })],
            estimate.PictureDelayMs);
        _request = request with { Outputs = resolved };
        lock (_gate)
        {
            _estimate = full;
        }

        _events.Publish(new LatencyEstimated(full));
        foreach (var output in estimate.Outputs)
        {
            LogLatency(output.DeviceId, DeviceTypeNames.Of(output.Type), output.ReportedMs, output.Source, output.TypeDefaultMs, output.OffsetMs, output.AssumedMs, output.LagMs, estimate.PictureDelayMs);
        }

        return returnTo is { } target
            ? StartRun(request with { Outputs = playing }, estimate, commandAt, lost, returning: true, target)
            : StartRun(request with { Outputs = playing }, estimate, commandAt, lost, returning: false);
    }

    // A renderer event is stamped with the film it was raised for when it is posted: by the time the session thread
    // gets to it the film may have changed, and it is then about a film that is gone.
    private void PostRendererEvent(Action handler)
    {
        var token = Interlocked.Read(ref _filmToken);
        _loop.Post(() =>
        {
            if (token == Interlocked.Read(ref _filmToken))
            {
                handler();
            }
        });
    }

    /// <summary>
    /// After the end, a new engine run for the same file and outputs (the engine cannot reopen a run that has ended),
    /// started at <paramref name="target"/>: the decoder seeks there before the pre-roll, so no audio of the film's opening
    /// is heard. The picture stays paused until the scheduled start, so no frame 0 is shown: after a picture failure whose
    /// last-frame reload worked the renderer already holds the last good frame and is not loaded again; if that reload
    /// failed (or there was none) the file is loaded again first. The engine carries the earlier run's summary over and
    /// prints no second start line.
    /// </summary>
    private Result ReturnIntoFilm(TimeSpan target, long commandAt)
    {
        StartRequest? request;
        LatencyEstimate? estimate;
        HashSet<string> lost;
        bool pictureFailed;
        bool lastFrameReloaded;
        lock (_gate)
        {
            (request, estimate, pictureFailed, lastFrameReloaded) = (_request, _estimate, _pictureFailed, _lastFrameReloaded);
            lost = new HashSet<string>(_lost, StringComparer.Ordinal);
        }

        if (request is null || estimate is null)
        {
            return Result.Failure("playback.no_run", "No film is playing.");
        }

        if (pictureFailed)
        {
            // The old picture's events are stale from here. When the last frame was reloaded at the failure, mpv already
            // holds it; otherwise mpv holds no file and it is loaded paused before any audio starts.
            Interlocked.Increment(ref _filmToken);
            if (!lastFrameReloaded)
            {
                var reloaded = _renderer.Load(request.File, nint.Zero, startPaused: true);
                if (!reloaded.IsSuccess)
                {
                    return reloaded;
                }
            }
        }

        var playing = request.Outputs.Where(a => a.Device.Id is not { } id || !lost.Contains(id)).ToList();
        lock (_gate)
        {
            CancelFreeze();
        }

        // Every output lost: resolve all devices again against the current list, as a file change does.
        var started = playing.Count == 0
            ? StartCore(request with { CommandTimestamp = commandAt }, keepLost: false, returnTo: target, missingIsLost: true)
            : StartRun(request with { Outputs = playing }, new LatencyEstimate([.. estimate.Outputs.Where(o => !lost.Contains(o.DeviceId))], estimate.PictureDelayMs), commandAt, lost, returning: true, target);
        if (!started.IsSuccess)
        {
            return started;
        }

        PictureSync sync;
        lock (_gate)
        {
            sync = _pictureSync!;
        }

        ApplyPendingTargets(sync);
        PublishDelay(target, PictureDelayCause.Seek, sync.PictureDelay);
        ReportTiming(sync);
        return Result.Success();
    }

    private Result StartRun(StartRequest request, LatencyEstimate estimate, long commandAt, HashSet<string> lost, bool returning, TimeSpan startPosition = default)
    {
        lock (_gate)
        {
            _pictureFailed = false;
            _lastFrameReloaded = false;
        }

        if (!returning)
        {
            lock (_gate)
            {
                _filmLoaded = false;
            }

            // The old film plays on until Load: from here its renderer events are stale.
            Interlocked.Increment(ref _filmToken);
            var loaded = _renderer.Load(request.File, nint.Zero, startPaused: true);
            if (!loaded.IsSuccess)
            {
                return loaded;
            }
        }

        var lags = estimate.Outputs.ToDictionary(o => o.DeviceId, o => TimeSpan.FromMilliseconds(o.LagMs), StringComparer.Ordinal);
        Interlocked.Increment(ref _runToken);
        var started = _engine.StartRun(request.File, request.Outputs, new EngineOptions { InitialLags = lags, StartPosition = startPosition, ContinuesPreviousRun = returning });
        if (!started.IsSuccess)
        {
            return Result.Failure(started.Error);
        }

        // Only now is there a run of this file to return into.
        lock (_gate)
        {
            _filmLoaded = true;
        }

        // A lag command replies once the pre-roll is released and the master clock has started.
        var settled = _lags.SetLagTargets(lags, LagChangeMode.Ramp);
        if (!settled.IsSuccess)
        {
            _engine.Stop(RunEndReason.Stop);
            return settled;
        }

        var maxLag = TimeSpan.FromMilliseconds(estimate.Outputs.Select(o => o.LagMs).DefaultIfEmpty(0).Max());
        // The clock the engine started: its frame zero follows only the outputs still connected at the release, so a
        // largest lag recomputed here would be wrong after a loss in the pre-roll (review F24).
        var clockStart = _lags.ClockStartTimestamp ?? ((_lags.FrameZeroTimestamp ?? _time.GetTimestamp()) - ToTimestamp(maxLag));
        var clock = new MasterClock(_time);
        clock.JumpTo(startPosition);
        clock.StartAt(clockStart);
        var pictureDelay = TimeSpan.FromMilliseconds(estimate.PictureDelayMs);
        var sync = new PictureSync(clock, _renderer, _time, pictureDelay, action => _loop.Post(action));
        sync.AlignAt(startPosition, clockStart);
        lock (_gate)
        {
            if (returning)
            {
                _resync = ("return", commandAt);
                _readingsAfter = commandAt;
            }
            else
            {
                // Measured by the sync ticks: the first frame on screen and each output's first audio plus its latency.
                _startTimings = (commandAt, estimate.Outputs);
                _resync = ("start", commandAt);
                _readingsAfter = commandAt;
            }
        }

        PictureSync? previous;
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
        }

        previous?.Dispose();
        return Result.Success();
    }

    /// <summary>
    /// Moves to the next playable file in the folder (T17). <paramref name="commandTimestamp"/> is when the key arrived,
    /// stamped by the caller before the command waited its turn; none means now.
    /// </summary>
    public Result NextFile(long? commandTimestamp) => Run(() => ChangeFile(NeighbourDirection.Next, commandTimestamp));

    /// <summary>Moves to the next playable file in the folder (T17), timed from now.</summary>
    public Result NextFile() => NextFile(null);

    /// <summary>Moves to the previous playable file in the folder (T17); <paramref name="commandTimestamp"/> as for <see cref="NextFile"/>.</summary>
    public Result PreviousFile(long? commandTimestamp) => Run(() => ChangeFile(NeighbourDirection.Previous, commandTimestamp));

    /// <summary>Moves to the previous playable file in the folder (T17), timed from now.</summary>
    public Result PreviousFile() => PreviousFile(null);

    private Result ChangeFile(NeighbourDirection direction, long? commandTimestamp)
    {
        var commandAt = commandTimestamp ?? _time.GetTimestamp();
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
        var outOfTime = false;
        try
        {
            foreach (var candidate in FolderNeighbours.Candidates(request.File, direction))
            {
                var (skip, expired) = _timedOut.Contains(candidate)
                    ? (new FileSkipped(Path.GetFileName(candidate), "unreadable", null, null), false)
                    : CheckPlayable(candidate, request, commandAt);
                if (expired)
                {
                    outOfTime = true;
                    break;
                }

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

        var isNext = direction == NeighbourDirection.Next;
        var reason = skipped > 0 ? SkipText(lastSkip!) : null;
        if (target is null && outOfTime)
        {
            // The budget ran out with files unchecked: not the AC-08 "no neighbour" outcome.
            _events.Publish(new NeighbourSearchTimedOut(isNext, skipped));
            _events.Publish(new NoteShown(
                $"{(isNext ? "Next" : "Previous")} file not found within {NeighbourScanBudget.TotalSeconds:0} s (checked {skipped})",
                NoteStyle.Info));
            return Result.Success();
        }

        if (target is null)
        {
            // One note per press: when every neighbour was skipped, it says so together with the last skipped file.
            var noFile = isNext ? "No next file" : "No previous file";
            _events.Publish(new NoNeighbour(isNext));
            _events.Publish(new NoteShown(
                skipped > 0 ? $"{noFile} (skipped {skipped}; last: {lastSkipped} - {reason})" : noFile,
                NoteStyle.Info));
            return Result.Success();
        }

        if (skipped > 0)
        {
            _events.Publish(new NoteShown(
                skipped == 1 ? $"Skipped {lastSkipped}: {reason}" : $"Skipped {skipped} files; last: {lastSkipped} - {reason}",
                NoteStyle.Warning));
        }

        lock (_gate)
        {
            CancelFreeze();
        }

        var previousDelay = estimateBefore?.PictureDelayMs ?? 0;
        _engine.Stop(RunEndReason.FileChange);
        lock (_gate)
        {
            _filmLoaded = false;
        }

        lock (_gate)
        {
            _state = PlaybackState.Idle;
        }

        // Outputs lost while others played stay lost; once every output is lost (AC-15b) the next film tries every device
        // again, so one that is back plays.
        bool someStillPlay;
        lock (_gate)
        {
            someStillPlay = request.Outputs.Any(a => a.Device.Id is not { } id || !_lost.Contains(id));
        }

        // Said as soon as the target is chosen, ahead of the new file's output and picture delay lines, which the start
        // publishes; a start that then fails still ends with its note and log line.
        _events.Publish(new FileChanged(target, request.File) { ToPrevious = direction == NeighbourDirection.Previous });

        // Timed from the key, not from the command line the first file was started with.
        var started = StartCore(request with { File = target, CommandTimestamp = commandAt }, keepLost: someStillPlay, missingIsLost: !someStillPlay);
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

        _timedOut.Clear();
        LatencyEstimate? now;
        lock (_gate)
        {
            now = _estimate;
            _resync = ("file-change", commandAt);
            _readingsAfter = commandAt;
        }

        if (now is not null)
        {
            _events.Publish(new PictureDelayChanged(TimeSpan.Zero, PictureDelayCause.FileChange, previousDelay, now.PictureDelayMs, now.Outputs));
        }

        return Result.Success();
    }

    // Skip = null and Expired = false: playable. Expired: the press budget ran out before this file could be judged.
    // A file the session gives up on (budget out in its listing or probe, or its probe used up its limit) has its worker
    // cancelled and is remembered, so the next press steps past it (AC-09b).
    private (FileSkipped? Skip, bool Expired) CheckPlayable(string path, StartRequest request, long commandAt)
    {
        var cancel = new CancellationTokenSource();
        var gaveUp = false;
        try
        {
            return CheckPlayable(path, request, commandAt, cancel.Token, out gaveUp);
        }
        finally
        {
            if (gaveUp)
            {
                // Left undisposed: the abandoned worker may still be waiting on the token's handle.
                _timedOut.Add(path);
                cancel.Cancel();
            }
            else
            {
                cancel.Dispose();
            }
        }
    }

    private (FileSkipped? Skip, bool Expired) CheckPlayable(string path, StartRequest request, long commandAt, CancellationToken cancel, out bool gaveUp)
    {
        gaveUp = false;
        var name = Path.GetFileName(path);
        var remaining = NeighbourScanBudget - _time.GetElapsedTime(commandAt);
        if (remaining < MinProbeTime)
        {
            return (null, true);
        }

        // The track listing runs on a worker under the same deadline as the probe.
        var listing = Task.Run(() => _decoder.ListTracks(path, cancel));
        while (!listing.IsCompleted)
        {
            if (NeighbourScanBudget - _time.GetElapsedTime(commandAt) <= TimeSpan.Zero)
            {
                gaveUp = true;
                return (null, true);
            }

            listing.Wait(1);
        }

        var tracks = listing.GetAwaiter().GetResult();
        if (!tracks.IsSuccess)
        {
            return (new FileSkipped(name, ReasonOf(tracks.Error.Code), null, null), false);
        }

        for (var i = 0; i < request.Outputs.Count; i++)
        {
            var track = request.Outputs[i].TrackNumber;
            if (!tracks.Value.Any(t => t.Number == track))
            {
                return (new FileSkipped(name, "track-missing", i + 1, track), false);
            }
        }

        // The limit is re-derived after the listing.
        remaining = NeighbourScanBudget - _time.GetElapsedTime(commandAt);
        if (remaining < MinProbeTime)
        {
            return (null, true);
        }

        var cut = remaining < PictureProbeLimit;
        var probeLimit = cut ? remaining : PictureProbeLimit;
        var probeStart = _time.GetTimestamp();
        var picture = _pictureProbe.Probe(path, probeLimit, cancel);
        if (picture.IsSuccess)
        {
            return (null, false);
        }

        // A probe that used up its limit: the file is given up on, and remembered, whatever the limit was.
        var usedUp = _time.GetElapsedTime(probeStart) + ProbeTimingSlack >= probeLimit
            && picture.Error.Code is "video.not_hardware_decodable" or "video.probe_timeout";
        gaveUp = usedUp;

        // A probe that used up a limit the budget had cut proves nothing about the file.
        if (cut && usedUp)
        {
            return (null, true);
        }

        var fields = picture.Error.Fields;
        return (new FileSkipped(name, ReasonOf(picture.Error.Code), null, null)
        {
            Codec = fields?.GetValueOrDefault("codec"),
            Profile = fields?.GetValueOrDefault("profile"),
        }, false);
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
            updated = Reestimate(estimate, deviceId, step.OffsetMs);
            _estimate = Merge(estimate, updated);
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
        LogLatency(output.DeviceId, DeviceTypeNames.Of(output.Type), output.ReportedMs, output.Source, output.TypeDefaultMs, output.OffsetMs, output.AssumedMs, output.LagMs, updated.PictureDelayMs);

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

        var targets = LagTargets(updated);
        var duration = MoveDuration(sync, updated, targets);
        var from = sync.PictureDelay;
        var applied = _lags.SetLagTargets(targets, LagChangeMode.Ramp, duration);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        sync.MoveTo(TimeSpan.FromMilliseconds(updated.PictureDelayMs), duration);
        _events.Publish(MoveRecord(PictureDelayCause.Offset, clockPosition, from, updated, targets, duration));
        ReportTiming(sync);
        return Result.Success();
    }

    private static Dictionary<string, TimeSpan> LagTargets(LatencyEstimate estimate) =>
        estimate.Outputs.ToDictionary(o => o.DeviceId, o => TimeSpan.FromMilliseconds(o.LagMs), StringComparer.Ordinal);

    /// <summary>
    /// How long a move of the picture delay and every output's lag to <paramref name="updated"/> lasts (AC-14): as long
    /// as the largest change needs at 0.2 %. The picture and each output spread their own change over it, so they all
    /// arrive together at its end and none moves faster than 2000 ppm.
    /// </summary>
    private TimeSpan MoveDuration(PictureSync sync, LatencyEstimate updated, IReadOnlyDictionary<string, TimeSpan> targets)
    {
        var largest = (TimeSpan.FromMilliseconds(updated.PictureDelayMs) - sync.PictureDelay).Duration();
        foreach (var (deviceId, target) in targets)
        {
            if (_lags.CurrentLag(deviceId) is { } current && (target - current).Duration() > largest)
            {
                largest = (target - current).Duration();
            }
        }

        return PictureSync.MoveDurationFor(largest);
    }

    /// <summary>
    /// The record of a ramped move, once it is applied: from the in-flight picture delay <paramref name="from"/> to
    /// <paramref name="to"/> over <paramref name="duration"/>, with the speeds read back as applied, not recomputed or
    /// clamped, so the log shows a speed beyond 0.2 % if one runs: the picture's as the renderer runs it (positive = faster
    /// than real time, cli.md §3), each output's ramp, drift correction and total as its drift controller runs them.
    /// </summary>
    private PictureDelayMoved MoveRecord(
        PictureDelayCause cause,
        TimeSpan clock,
        TimeSpan from,
        LatencyEstimate to,
        IReadOnlyDictionary<string, TimeSpan> targets,
        TimeSpan duration)
    {
        var picturePpm = (_renderer.Speed - 1.0) * 1e6;
        var applied = _lags.AppliedSpeeds();
        var ramps = new List<OutputRamp>();
        foreach (var deviceId in targets.Keys)
        {
            if (!applied.TryGetValue(deviceId, out var speed))
            {
                continue; // lost meanwhile
            }

            ramps.Add(new OutputRamp(deviceId, speed.RampPpm)
            {
                OutputNumber = to.Outputs.First(o => o.DeviceId == deviceId).OutputNumber,
                CorrectionPpm = speed.CorrectionPpm,
                TotalPpm = speed.TotalPpm,
            });
        }

        return new PictureDelayMoved(clock, cause, (int)Math.Round(from.TotalMilliseconds), to.PictureDelayMs, duration, picturePpm, ramps);
    }

    /// <summary>
    /// The estimate over the outputs still playing, with <paramref name="deviceId"/>'s offset replaced; each output keeps
    /// where its reported part came from. The caller holds the gate.
    /// </summary>
    private LatencyEstimate Reestimate(LatencyEstimate estimate, string? deviceId, int offsetMs)
    {
        var playing = estimate.Outputs.Where(o => !_lost.Contains(o.DeviceId)).ToList();
        var fresh = LatencyModel.Estimate([.. playing.Select(o => (o.DeviceId, o.Type, o.ReportedMs, o.DeviceId == deviceId ? offsetMs : o.OffsetMs))]);
        return new LatencyEstimate([.. fresh.Outputs.Select(u => u with { Source = playing.First(o => o.DeviceId == u.DeviceId).Source, OutputNumber = playing.First(o => o.DeviceId == u.DeviceId).OutputNumber })], fresh.PictureDelayMs);
    }

    // Every output keeps its place: the playing ones from the new estimate, the lost ones as they were.
    private static LatencyEstimate Merge(LatencyEstimate all, LatencyEstimate playing) =>
        new([.. all.Outputs.Select(o => playing.Outputs.FirstOrDefault(u => u.DeviceId == o.DeviceId) ?? o)], playing.PictureDelayMs);

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
        bool aligning;
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
            aligning = _pendingAlign is not null;
        }

        var paused = _engine.Pause();
        if (!paused.IsSuccess)
        {
            return Result.Failure(paused.Error);
        }

        var freeze = paused.Value;
        var atKey = aligning ? null : sync.Measure();
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

    /// <summary>Resumes; <paramref name="commandTimestamp"/> is when the key arrived (none: now).</summary>
    public Result Resume(long? commandTimestamp = null) => Run(() => ResumeCore(commandTimestamp));

    private Result ResumeCore(long? commandTimestamp)
    {
        var commandAt = commandTimestamp ?? _time.GetTimestamp();
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
        var now = _time.GetTimestamp(); // the engine has resumed: only readings from here on say anything about the outputs now
        ApplyPendingTargets(sync);
        clock.JumpTo(position);
        clock.Start();
        var delay = sync.TargetPictureDelay;
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
            _resync = ("resume", commandAt);
            _readingsAfter = now;
        }

        PublishDelay(position, PictureDelayCause.Resume, delay);
        ReportTiming(sync);
        return Result.Success();
    }

    /// <summary>
    /// Seeks by <paramref name="step"/> relative to the current position (negative = backward). <paramref name="commandTimestamp"/>
    /// is when the key arrived (none: now).
    /// </summary>
    public Result Seek(TimeSpan step, long? commandTimestamp = null) => Run(() => SeekCore(step, commandTimestamp));

    private Result SeekCore(TimeSpan step, long? commandTimestamp)
    {
        var commandAt = commandTimestamp ?? _time.GetTimestamp();
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
                if (step >= TimeSpan.Zero || _request is null || !_filmLoaded)
                {
                    // Nothing to return into: no run of this file started (a file change failed to load or to start).
                    return Result.Success();
                }

                // A backward seek at the end returns into the film at the target, its picture held until then.
                lock (_gate)
                {
                    _state = PlaybackState.Idle;
                }

                var restarted = ReturnIntoFilm(current + step < TimeSpan.Zero ? TimeSpan.Zero : current + step, commandAt);
                if (!restarted.IsSuccess)
                {
                    // The film stays ended and the keys keep answering; say why.
                    lock (_gate)
                    {
                        _state = PlaybackState.Ended;
                    }

                    LogReturnFailed(restarted.Error.Code, restarted.Error.Message);
                    _events.Publish(new NoteShown($"Could not go back: {restarted.Error.Message}", NoteStyle.Warning));
                    return restarted;
                }

                return restarted;
            default:
                return SeekTo(current + step, commandAt);
        }
    }

    private Result SeekTo(TimeSpan target, long commandAt)
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

        var engineDone = _time.GetTimestamp(); // readings taken before this are from before the seek
        ApplyPendingTargets(sync);
        var delay = sync.TargetPictureDelay;
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
            PublishDelay(seeked.Value.Position, PictureDelayCause.Seek, delay);
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
                _resync = ("seek", commandAt);
                _readingsAfter = engineDone;
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
        // A resume still waiting for the picture to align holds it like a scheduled start: the hold times are stale.
        bool aligning;
        lock (_gate)
        {
            aligning = _pendingAlign is not null;
        }

        if (!aligning && sync.Measure() is { } timing)
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
            // Resumed before the picture reached the freeze point: it runs ahead of the clock until the align, so there is
            // nothing to steer or measure yet.
            sync = _state == PlaybackState.Playing && _pendingAlign is null ? _pictureSync : null;
        }

        if (sync is null)
        {
            return;
        }

        if (sync.Tick() is not { } timing)
        {
            GiveUpWithoutPicture();
            return;
        }

        bool report;
        (string Command, long At)? resynced = null;
        (string Command, long At)? due = null;
        (string Command, long At)? gaveUp = null;
        long readingsAfter = 0;
        Dictionary<string, double>? outputErrors = null;
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

            _lastMeasured = (timing.Timestamp, timing.ErrorMs);

            if (_resync is { } owed)
            {
                var budget = owed.Command is "start" or "file-change" or "return" ? StartRecoveryBudget : RecoveryBudget;
                if (timing.Timestamp >= owed.At && _time.GetElapsedTime(owed.At, timing.Timestamp) > budget)
                {
                    gaveUp = owed;
                }
                else if (Math.Abs(timing.ErrorMs) <= LipSyncedMs
                    && timing.Timestamp >= owed.At
                    && (_outputsPolledAt < owed.At || _time.GetElapsedTime(_outputsPolledAt, timing.Timestamp) >= OutputPollInterval))
                {
                    _outputsPolledAt = timing.Timestamp;
                    due = owed;
                    readingsAfter = _readingsAfter;
                }
            }

            starting = _startTimings;
        }

        if (report)
        {
            Publish(timing);
        }

        // Asked of the engine only while a record is owed and the picture already qualifies.
        if (gaveUp is { } failed)
        {
            PublishNotLipSynced(failed, timing.Clock, timing.Timestamp, timing.ErrorMs);
        }
        else if (due is { } candidate && OutputsLipSynced(readingsAfter) is { } errors)
        {
            lock (_gate)
            {
                if (_resync == candidate)
                {
                    resynced = candidate;
                    outputErrors = errors;
                    _resync = null;
                }
            }
        }

        if (resynced is var (command, at))
        {
            _events.Publish(new LipSyncedAgain(timing.Clock, command, _time.GetElapsedTime(at, timing.Timestamp).TotalMilliseconds, timing.ErrorMs)
            {
                OutputErrorsMs = outputErrors ?? [],
            });
        }

        if (starting is var (commandAt, outputs) && timing.Presented > TimeSpan.Zero)
        {
            PublishStartTimings(commandAt, outputs, timing);
        }
    }

    // Every output still playing has taken, since the command, a position reading within 10 ms: those errors, else null.
    // No frame was measurable after the command: a record that is owed past its budget is still given up, with no picture error.
    private void GiveUpWithoutPicture()
    {
        (string Command, long At)? owed;
        TimeSpan clock;
        (long At, double ErrorMs)? measured;
        var now = _time.GetTimestamp();
        lock (_gate)
        {
            owed = _resync;
            measured = _lastMeasured;
            clock = _clock?.PositionAt(now) ?? TimeSpan.Zero;
        }

        if (owed is { } failed && now >= failed.At
            && _time.GetElapsedTime(failed.At, now) > (failed.Command is "start" or "file-change" or "return" ? StartRecoveryBudget : RecoveryBudget))
        {
            PublishNotLipSynced(failed, clock, now, measured is { } m && m.At >= failed.At ? m.ErrorMs : null);
        }
    }

    private void PublishNotLipSynced((string Command, long At) failed, TimeSpan clock, long timestamp, double? errorMs)
    {
        var latest = OutputErrors();
        lock (_gate)
        {
            if (_resync != failed)
            {
                return;
            }

            _resync = null;
        }

        _events.Publish(new NotLipSynced(clock, failed.Command, _time.GetElapsedTime(failed.At, timestamp).TotalMilliseconds, errorMs)
        {
            OutputErrorsMs = latest.ToDictionary(e => e.Key, e => e.Value.ErrorMs, StringComparer.Ordinal),
        });
    }

    // The readings are the engine's latest 100 ms ones (not the per-second record's average, which lags a seek's
    // transient); a reading from before the command says nothing about the outputs now.
    private Dictionary<string, OutputPositionError> OutputErrors()
    {
        var lost = new HashSet<string>(_lost, StringComparer.Ordinal);
        var readings = new Dictionary<string, OutputPositionError>(StringComparer.Ordinal);
        foreach (var (deviceId, reading) in _lags.PositionErrors())
        {
            if (!lost.Contains(deviceId))
            {
                readings[deviceId] = reading;
            }
        }

        return readings;
    }

    private Dictionary<string, double>? OutputsLipSynced(long readingsAfter)
    {
        var errors = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (deviceId, reading) in OutputErrors())
        {
            if (reading.Timestamp < readingsAfter || Math.Abs(reading.ErrorMs) > OutputLipSyncedMs)
            {
                return null;
            }

            errors[deviceId] = reading.ErrorMs;
        }

        return errors;
    }

    // The picture moving from frame 0 dates its first present; each output is heard its assumed latency after its
    // first audio. Published once every output still connected has handed over its first audio.
    private void PublishStartTimings(long commandAt, IReadOnlyList<OutputLatency> outputs, PictureTiming timing)
    {
        HashSet<string> lost;
        lock (_gate)
        {
            lost = new HashSet<string>(_lost, StringComparer.Ordinal);
        }

        var heard = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            if (lost.Contains(output.DeviceId))
            {
                continue; // lost before its first audio: it never will hand one over, and nothing is waited for
            }

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

    private void OnPictureFailed(string reason, bool reloadLastFrame = false)
    {
        lock (_gate)
        {
            if (_pictureFailed || _state == PlaybackState.Ended)
            {
                return;
            }
        }

        EndOnPictureFailure(reason, reloadLastFrame);
    }

    private void EndOnPictureFailure(string reason, bool reloadLastFrame)
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

        // The sound stops and the failure is published first: the reload below can block, and the run must end at the
        // failure position, not where the reload finishes.
        _renderer.Pause();
        _events.Publish(new PictureFailed(position, reason));
        _engine.Stop(RunEndReason.PictureFailed);

        var reloaded = reloadLastFrame && ReloadLastFrame(position);
        lock (_gate)
        {
            _lastFrameReloaded = reloaded;
        }

        _events.Publish(new NoteShown(PictureFailedNote, NoteStyle.Warning));
        _renderer.ShowNote(PictureFailedNote, NoteStyle.Warning);
    }

    // An error end-file unloads the file in mpv, leaving an empty window: load it again paused at the last presented
    // position so the note sits on the last good frame (SCR-02). The wait is bounded (ReloadTimeout); a failed or late
    // reload leaves the empty window and the note follows. Returns whether the last frame is up.
    private bool ReloadLastFrame(TimeSpan fallback)
    {
        string? file;
        lock (_gate)
        {
            file = _request?.File;
        }

        if (file is null)
        {
            return false;
        }

        var position = _renderer.Presented?.Position ?? fallback;
        if (!_renderer.Load(file, nint.Zero, startPaused: true, ReloadTimeout).IsSuccess)
        {
            return false;
        }

        _renderer.Seek(position);
        return true;
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
        if (_exited)
        {
            return;
        }

        _exited = true;
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

        LatencyEstimate? updated = null;
        PlaybackState state;
        lock (_gate)
        {
            if (lost.OthersContinue && estimate is not null)
            {
                updated = Reestimate(estimate, deviceId: null, offsetMs: 0);
                _estimate = Merge(estimate, updated);
            }

            state = _state;
        }

        _events.Publish(new OutputLost(number, lost.DeviceId, lost.ClockPosition, lost.OthersContinue)
        {
            OldPictureDelayMs = updated is null ? null : estimate!.PictureDelayMs,
            NewPictureDelayMs = updated?.PictureDelayMs,
        });

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
        if (estimate is null || updated is null || sync is null || clock is null)
        {
            return;
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

        var lostTargets = LagTargets(updated);
        var lostDuration = MoveDuration(sync, updated, lostTargets);
        var lostFrom = sync.PictureDelay;
        _lags.SetLagTargets(lostTargets, LagChangeMode.Ramp, lostDuration);
        sync.MoveTo(TimeSpan.FromMilliseconds(updated.PictureDelayMs), lostDuration);
        _events.Publish(MoveRecord(PictureDelayCause.DeviceLost, lost.ClockPosition, lostFrom, updated, lostTargets, lostDuration));
        ReportTiming(sync);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Latency for {DeviceId}: type {Type}, reported {ReportedMs} ms ({Source}), default {DefaultMs} ms, offset {OffsetMs} ms, assumed {AssumedMs} ms, lag {LagMs} ms; picture delay {PictureDelayMs} ms")]
    private partial void LogLatency(string deviceId, string type, int reportedMs, string? source, int defaultMs, int offsetMs, int assumedMs, int lagMs, int pictureDelayMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Latency probe failed for {DeviceId} ({Code}): {Message}; reported 0 ms, type {Type}")]
    private partial void LogProbeFailed(string deviceId, string code, string message, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Playback session command or callback failed")]
    private partial void LogLoopFault(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not change to {File}: {Code} {Message}")]
    private partial void LogFileChangeFailed(string file, string code, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not return into the film: {Code} {Message}")]
    private partial void LogReturnFailed(string code, string message);

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
