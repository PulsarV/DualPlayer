using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualPlayer.Core.Playback;

/// <summary>
/// Joins the Core pieces into a run: starts every output from one master clock, keeps the others playing when a
/// device is lost and always ends with a run summary. Every command and backend event goes through one
/// <see cref="EngineControlLoop"/>.
/// </summary>
public sealed partial class AudioEngine : IAudioEngine, IOutputLagControl, IDisposable
{
    private static readonly TimeSpan StartLevelTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AlignTolerance = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private const int MaxAlignRounds = 16;

    /// <summary>
    /// Added after the moment the slowest output hears the next frame it is handed (so the clock starts ≈ L_max + this after
    /// the release), so every output still gets its first film frame on time.
    /// </summary>
    private static readonly TimeSpan StartMargin = TimeSpan.FromMilliseconds(30);

    /// <summary>How long after a seek or resume each output keeps being re-measured, so a later hole is corrected too.</summary>
    private static readonly TimeSpan AlignWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StartLevelPollInterval = TimeSpan.FromMilliseconds(2);

    /// <summary>How often the control loop takes a drift reading from every output and updates its rate correction.</summary>
    private static readonly TimeSpan SyncSampleInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Readings per sync record: one record per device per second (AC-11).</summary>
    private const int SamplesPerRecord = 10;

    private readonly IAudioBackend _backend;
    private readonly IAudioDecoder _decoder;
    private readonly TimeProvider _time;
    private readonly ILogger<AudioEngine> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly EngineControlLoop _loop;
    private readonly EventPublisher _events;
    private Run? _run; // control-loop thread only
    private string? _lastSummaryFile; // the file that summary belongs to
    private RunSummary? _lastSummary; // the summary of the run that ended last; carried into a run that continues it
    private int _disposed;

    public AudioEngine(
        IAudioBackend backend,
        IAudioDecoder decoder,
        TimeProvider time,
        ILogger<AudioEngine> logger,
        ILoggerFactory? loggerFactory = null)
    {
        _backend = backend;
        _decoder = decoder;
        _time = time;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _events = new EventPublisher(LogObserverFault);
        _loop = new EngineControlLoop(Poll, LogLoopFault);
    }

    public IObservable<EngineEvent> Events => _events;

    public Result<RunStarted> StartRun(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(options);
        return _loop.Execute(() => StartCore(file, assignments, options));
    }

    public Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode, TimeSpan moveDuration = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return _loop.Execute(() => SetLagTargetsCore(targets, mode, moveDuration));
    }

    public TimeSpan? CurrentLag(string deviceId) => _loop.Execute(() => FindOutput(deviceId)?.Sync?.Lag);

    public long? FirstAudioTimestamp(string deviceId) => _loop.Execute(() => FindOutput(deviceId)?.Gate.FirstAudioTimestamp);

    public long? FrameZeroTimestamp => _loop.Execute(() => _run?.FrameZeroTimestamp);

    public long? ClockStartTimestamp => _loop.Execute(() => _run is { Released: true } run ? run.ClockStartTimestamp : (long?)null);

    public IReadOnlyDictionary<string, OutputPositionError> PositionErrors() => _loop.Execute(() =>
        (IReadOnlyDictionary<string, OutputPositionError>)(_run?.Outputs
            .Where(o => !o.Lost)
            .ToDictionary(o => o.DeviceId, o => new OutputPositionError(o.LatestErrorMs, o.LatestErrorAt), StringComparer.Ordinal)
            ?? []));

    public IReadOnlyDictionary<string, OutputSpeed> AppliedSpeeds() => _loop.Execute(() =>
        (IReadOnlyDictionary<string, OutputSpeed>)(_run?.Outputs
            .Where(o => !o.Lost && o.Sync is not null)
            .ToDictionary(o => o.DeviceId, o => new OutputSpeed(o.Sync!.RampPpm, o.Sync.CorrectionPpm), StringComparer.Ordinal)
            ?? []));

    public bool IsRamping => _loop.Execute(() => _run is { } run && run.Outputs.Any(o => !o.Lost && o.Sync is { IsLagMoving: true }));

    public Result<TimeSpan> Pause() => _loop.Execute(PauseCore);

    public Result<TimeSpan> Resume() => _loop.Execute(ResumeCore);

    public Result<SeekResult> Seek(TimeSpan target) => _loop.Execute(() => SeekCore(target));

    public void Stop(RunEndReason reason)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // A dispose racing past the check above leaves the loop closed: nothing to stop then.
        _ = _loop.TryExecute(() =>
        {
            if (_run is { } run)
            {
                EndRun(run, reason);
            }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _loop.Execute(() =>
        {
            if (_run is { } run)
            {
                EndRun(run, RunEndReason.Interrupt);
            }
        });
        _loop.Dispose();
        _events.Complete();
    }

    private Result<RunStarted> StartCore(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options)
    {
        if (_run is not null)
        {
            return Result<RunStarted>.Failure("playback.run_active", "A run is already active; stop it first.");
        }

        var startTimestamp = _time.GetTimestamp();
        var tracks = _decoder.ListTracks(file);
        if (!tracks.IsSuccess)
        {
            return Result<RunStarted>.Failure(tracks.Error);
        }

        // One device list for validation and names, so a device leaving in between cannot break the start.
        var catalog = new DeviceCatalog(_backend.ListDevices());
        var resolved = OutputAssignmentValidator.Validate(tracks.Value, catalog, assignments);
        if (!resolved.IsSuccess)
        {
            return Result<RunStarted>.Failure(resolved.Error);
        }

        var run = new Run(startTimestamp) { StopWaitMaxPolls = options.StopWaitMaxPolls, File = file };
        foreach (var output in resolved.Value)
        {
            var name = catalog.Devices.First(d => string.Equals(d.Id, output.DeviceId, StringComparison.Ordinal)).Name;
            var opened = OpenOutput(run, run.Outputs.Count, output, name, options);
            if (!opened.IsSuccess)
            {
                Teardown(run);
                return Result<RunStarted>.Failure(opened.Error);
            }
        }

        var requests = run.Outputs.Select(o => new DecodeRequest(o.TrackNumber, o.Format)).ToArray();
        var session = _decoder.Open(file, requests);
        if (!session.IsSuccess)
        {
            Teardown(run);
            return Result<RunStarted>.Failure(session.Error);
        }

        run.FileDuration = session.Value.Duration;
        run.Pump = new DecodePump(
            session.Value,
            [.. run.Outputs.Select(o => o.Ring)],
            _time,
            _loggerFactory?.CreateLogger<DecodePump>() ?? NullLogger<DecodePump>.Instance,
            [.. run.Outputs.Select(o => o.Format.SampleRate)]);
        run.Pump.Ended += () =>
        {
            foreach (var output in run.Outputs)
            {
                output.Source.MarkEndOfStream(run.Pump.EndedEpoch);
            }

            OnPumpEnded(run);
        };
        run.Pump.DecodeFailed += error => OnDecodeFailed(run, error);
        run.Pump.Faulted += _ => OnDecodeFailed(run, new Error("playback.decoder_fault", "The decoder failed unexpectedly."));
        run.Pump.Start(0, options.StartPosition);

        var filled = WaitForStartLevel(run);
        if (!filled.IsSuccess)
        {
            Teardown(run);
            return Result<RunStarted>.Failure(filled.Error);
        }

        // Silent pre-roll (ADR-0008): every device plays silence until its position moves; the control loop then
        // releases the film so that every output hears frame 0 when the master clock starts (AdvanceStart).
        run.Clock = new MasterClock(_time);
        run.Clock.JumpTo(options.StartPosition);
        run.ContinuesPreviousRun = options.ContinuesPreviousRun;
        if (options.ContinuesPreviousRun && _lastSummary is { } carried && string.Equals(_lastSummaryFile, file, StringComparison.Ordinal))
        {
            run.Summary.CarryOver(carried);
        }

        foreach (var output in run.Outputs)
        {
            output.Source.BeginEpoch(0, (long)Math.Round(options.StartPosition.TotalSeconds * output.Format.SampleRate));
            output.Sync = new OutputSync(run.Clock, options.DriftTolerance);
            if (options.InitialLags.TryGetValue(output.DeviceId, out var initialLag))
            {
                output.Sync.SetLagAtOnce(initialLag);
            }

            output.Trace = new DriftTrace(output.DeviceId, _logger);
            output.Mismatch = new ClockMismatchEstimator(_time);
            output.Source.StartAtDeviceFrame(long.MaxValue);
            output.Device.Start();
            run.Summary.OutputStarted(output.DeviceId, options.StartPosition);
        }

        var now = _time.GetTimestamp();
        run.Starting = true;
        run.DriftTolerance = options.DriftTolerance;
        run.Publisher = new SyncRecordPublisher(
            _events.Publish,
            (ILogger?)_loggerFactory?.CreateLogger<SyncRecordPublisher>() ?? _logger);
        _run = run;
        _loop.Polling = true; // through the pre-roll, then until every output's first audio is logged
        run.StartClockPosition = run.Clock.PositionAt(now);
        LogRunStarted(run.Outputs.Count, _time.GetElapsedTime(startTimestamp, now).TotalMilliseconds);
        return Result<RunStarted>.Success(new RunStarted(
            [.. run.Outputs.Select(o => new StartedOutput(o.TrackNumber, o.DeviceId, o.DeviceName, o.Format)
            {
                Source = tracks.Value.FirstOrDefault(t => t.Number == o.TrackNumber),
            })],
            run.StartClockPosition));
    }

    private Output? FindOutput(string deviceId) =>
        _run?.Outputs.FirstOrDefault(o => !o.Lost && string.Equals(o.DeviceId, deviceId, StringComparison.Ordinal));

    private Result SetLagTargetsCore(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode, TimeSpan moveDuration)
    {
        if (_run is not { } run)
        {
            return Result.Failure("playback.no_run", "No run is active.");
        }

        var started = WaitForClockStart(run);
        if (!started.IsSuccess)
        {
            return started;
        }

        var now = _time.GetTimestamp();
        foreach (var (deviceId, target) in targets)
        {
            if (FindOutput(deviceId) is not { Sync: { } sync } output)
            {
                continue; // lost or not in the run
            }

            if (mode == LagChangeMode.Ramp)
            {
                sync.RetargetLag(target, moveDuration);
                continue;
            }

            sync.SetLagAtOnce(target);
            BeginAlignment(output, now); // the output is re-aligned to clock − lag in one step
        }

        _loop.Polling = true;
        return Result.Success();
    }

    private Result<TimeSpan> PauseCore()
    {
        if (_run is not { } run)
        {
            return Result<TimeSpan>.Failure("playback.no_run", "No run is active.");
        }

        var started = WaitForClockStart(run);
        if (!started.IsSuccess)
        {
            return Result<TimeSpan>.Failure(started.Error);
        }

        var clock = run.Clock!;
        var now = _time.GetTimestamp();
        if (run.Paused)
        {
            return Result<TimeSpan>.Success(clock.PositionAt(now));
        }

        StopBeyondQueue(run, now);
        run.Paused = true;
        return Result<TimeSpan>.Success(clock.PositionAt(now));
    }

    private Result<TimeSpan> ResumeCore()
    {
        if (_run is not { } run)
        {
            return Result<TimeSpan>.Failure("playback.no_run", "No run is active.");
        }

        var started = WaitForClockStart(run);
        if (!started.IsSuccess)
        {
            return Result<TimeSpan>.Failure(started.Error);
        }

        var clock = run.Clock!;
        if (!run.Paused)
        {
            return Result<TimeSpan>.Success(clock.PositionAt(_time.GetTimestamp()));
        }

        var filled = WaitForStartLevel(run);
        if (!filled.IsSuccess)
        {
            return Result<TimeSpan>.Failure(filled.Error);
        }

        var now = _time.GetTimestamp();
        if (now < run.StopTimestamp)
        {
            // The devices have not reached the pause instant yet: carry on from where they really are, so the
            // audio still queued before that instant and the ring after it play on without a jump.
            clock.JumpTo(clock.PositionAt(now) - _time.GetElapsedTime(now, run.StopTimestamp));
        }

        var position = clock.PositionAt(now);
        run.StopTimestamp = long.MinValue;
        clock.Start();
        BeginRecovery(run, now);
        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            output.Source.ClearSilence();
            output.AlignAfterRecovery = false;
            output.Sync!.SetLagAtOnce(output.Sync.LagTarget); // a ramp in progress jumps to its target: re-aligned anyway
            BeginAlignment(output, now);
        }

        run.Paused = false;
        _loop.Polling = true;
        return Result<TimeSpan>.Success(position);
    }

    private Result<SeekResult> SeekCore(TimeSpan target)
    {
        if (_run is not { } run)
        {
            return Result<SeekResult>.Failure("playback.no_run", "No run is active.");
        }

        var started = WaitForClockStart(run);
        if (!started.IsSuccess)
        {
            return Result<SeekResult>.Failure(started.Error);
        }

        var outsideFile = target < TimeSpan.Zero;
        if (outsideFile)
        {
            _events.Publish(new SeekTargetOutsideFile(target, TimeSpan.Zero));
            target = TimeSpan.Zero;
            LogSeekOutsideFile("before the start");
        }

        var active = run.Outputs.Where(o => !o.Lost).ToArray();
        var heardBefore = active.Length == 0
            ? TimeSpan.Zero
            : active.Max(o => TimeSpan.FromSeconds((double)(o.Source.LastContentFrameHandedOut + 1) / o.Format.SampleRate));
        var epoch = ++run.Epoch; // a new epoch is not ended until the pump reports it so
        var now = _time.GetTimestamp();
        var preSeek = run.Clock!.PositionAt(now);
        run.Publisher?.SeekStarting(preSeek);
        run.Summary.RecordClockJump(target - preSeek);
        run.Clock.JumpTo(target);
        run.StopTimestamp = long.MinValue; // a pause instant before the seek no longer applies
        var startFrames = new Dictionary<Output, long>();
        foreach (var output in active)
        {
            var startFrame = (long)Math.Round(target.TotalSeconds * output.Format.SampleRate);
            startFrames[output] = startFrame;
            output.Source.BeginEpoch(epoch, startFrame); // before the pump can deliver anything of the new epoch
        }

        run.Pump!.Seek(target, epoch);
        BeginRecovery(run, now);
        foreach (var output in active)
        {
            output.Sync!.SetLagAtOnce(output.Sync.LagTarget); // a ramp in progress jumps to its target: re-aligned anyway
            var startFrame = startFrames[output];
            if (run.Paused)
            {
                output.Source.SetSilenceFrom(startFrame); // stays silent until resume, which aligns
                output.AlignPending = false;
                output.AlignAfterRecovery = false;
            }
            else if (run.Pump.IsStalled(output.Index))
            {
                // A hung device does not render, so it cannot be measured yet: align it once, from where it
                // really is, when it applies this epoch again.
                output.AlignPending = false;
                output.AlignAfterRecovery = true;
            }
            else
            {
                output.AlignAfterRecovery = false;
                BeginAlignment(output, now);
            }
        }

        var confirmed = WaitForSeek(run, active, epoch);
        if (!confirmed.IsSuccess)
        {
            _loop.Polling = true; // keep aligning, so the outputs that did render resume their rate correction
            return Result<SeekResult>.Failure(confirmed.Error);
        }

        if (!confirmed.Value)
        {
            // The end is the container's duration; without one, the furthest audio heard before the seek.
            LogSeekOutsideFile("past the end");
            var end = run.FileDuration is { } duration ? (duration < target ? duration : target) : heardBefore;
            _events.Publish(new SeekTargetOutsideFile(target, end));
            run.Summary.RecordClockJump(end - target); // the clock moves back from the target to the end: not played time either
            run.Clock.JumpTo(end);
            run.Clock.StopAt(_time.GetTimestamp());
            EndRun(run, RunEndReason.SeekPastEnd);
            return Result<SeekResult>.Success(new SeekResult(end, true, run.Paused));
        }

        _loop.Polling = true;
        return Result<SeekResult>.Success(new SeekResult(run.Clock.PositionAt(_time.GetTimestamp()), outsideFile, run.Paused));
    }

    /// <summary>
    /// A command given during the start's pre-roll waits on the loop until the master clock has started, then runs as
    /// it would afterwards: no film plays while paused and the start is not broken. Bounded by
    /// <see cref="FirstAudioDeadline"/> + <see cref="StartLevelTimeout"/> after the start.
    /// </summary>
    private Result WaitForClockStart(Run run)
    {
        var deadline = run.StartTimestamp + (long)((FirstAudioDeadline + StartLevelTimeout).TotalSeconds * _time.TimestampFrequency);
        while (AdvanceStart(run))
        {
            if (run.DecodeError is { } error)
            {
                return Result.Failure(error);
            }

            if (_time.GetTimestamp() >= deadline || Volatile.Read(ref _disposed) != 0)
            {
                return Result.Failure("playback.start_timeout", "The outputs did not start in time.");
            }

            Thread.Sleep(StartLevelPollInterval);
        }

        return Result.Success();
    }

    /// <summary>
    /// Moves the start on (ADR-0008): releases the film once every output's position has moved, then starts the master
    /// clock at 00:00 when the slowest output hears film frame 0, and aligns every output in one step from there.
    /// True while the clock has not started yet.
    /// </summary>
    private bool AdvanceStart(Run run)
    {
        if (!run.Starting)
        {
            return false;
        }

        var now = _time.GetTimestamp();
        if (!run.Released && !TryRelease(run, now))
        {
            return true;
        }

        if (now < run.ClockStartTimestamp)
        {
            return true;
        }

        run.Starting = false;
        run.LastLagAdvance = run.ClockStartTimestamp;
        run.Clock!.StartAt(run.ClockStartTimestamp);
        BeginRecovery(run, run.ClockStartTimestamp);
        run.SyncTimer = _time.CreateTimer(_ => _loop.Post(() => OnSyncSample(run)), null, SyncSampleInterval, SyncSampleInterval);
        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            BeginAlignment(output, now); // trims what is left: about one device period, or an output that never moved
        }

        return false;
    }

    /// <summary>
    /// Test seam: called on the control loop after each start-target change inside the release, with the number of moved
    /// outputs still awaiting their heard target. A render there is the release race (ADR-0008).
    /// </summary>
    internal Action<int>? ReleaseStepForTests { get; set; }

    /// <summary>
    /// Once every non-lost output's position has moved (or it is stalled, or <see cref="FirstAudioDeadline"/> passed),
    /// gives each output its first film frame at the device frame heard when the slowest one hears its next frame (plus
    /// <see cref="StartMargin"/>). An
    /// output that never moved gets the film at once and is aligned when the clock starts. False while still waiting.
    /// </summary>
    private bool TryRelease(Run run, long now)
    {
        var pastDeadline = _time.GetElapsedTime(run.StartTimestamp, now) >= FirstAudioDeadline;
        var moved = new List<(Output Output, long HeardAt)>();
        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            var queueRead = output.Source.TryGetLatestQueue(out var played, out var timestamp, out var written);
            var reading = ReadForRelease(queueRead, played, output.Source.LatestFramesPlayed, pastDeadline, run.Pump!.IsStalled(output.Index));
            if (reading == ReleaseReading.Wait)
            {
                return false;
            }

            if (reading == ReleaseReading.Moved)
            {
                // Constant once the position moves: a frame handed over now is heard after the frames still queued.
                var lag = (double)Math.Max(0, written - played) / output.Format.SampleRate; // an underrun queue is heard at once
                moved.Add((output, timestamp + (long)(lag * _time.TimestampFrequency)));
            }
        }

        var margin = (long)(StartMargin.TotalSeconds * _time.TimestampFrequency);
        var clockStart = moved.Count == 0 ? now : Math.Max(now, moved.Max(m => m.HeardAt - LagTicks(m.Output))) + margin;
        // A moved output keeps its pre-roll hold until its heard target is set: resetting it first would let a
        // render in between hand out film frame 0 at once, ~L_max before the clock starts.
        foreach (var output in run.Outputs.Where(o => !o.Lost && !moved.Exists(m => m.Output == o)))
        {
            output.Source.StartAtDeviceFrame(long.MinValue);
            ReleaseStepForTests?.Invoke(moved.Count);
        }

        for (var i = 0; i < moved.Count; i++)
        {
            var output = moved[i].Output;
            output.Source.StartWhenHeardAt(clockStart + LagTicks(output), (double)output.Format.SampleRate / _time.TimestampFrequency);
            ReleaseStepForTests?.Invoke(moved.Count - i - 1);
        }

        run.Released = true;
        run.ClockStartTimestamp = clockStart;
        run.FrameZeroTimestamp = clockStart + run.Outputs.Where(o => !o.Lost).Select(LagTicks).DefaultIfEmpty(0).Max();
        LogFilmReleased(moved.Count, run.Outputs.Count(o => !o.Lost), _time.GetElapsedTime(run.StartTimestamp, clockStart).TotalMilliseconds);
        return true;
    }

    /// <summary>The output's start lag in timestamp ticks: it hears film frame 0 that long after the clock starts.</summary>
    private long LagTicks(Output output) => (long)(output.Sync!.Lag.TotalSeconds * _time.TimestampFrequency);

    internal enum ReleaseReading
    {
        /// <summary>The position moved: the output gets a heard target and its lag counts for the clock start.</summary>
        Moved,

        /// <summary>The release waits for the next poll.</summary>
        Wait,

        /// <summary>The output no longer holds the release; it gets the film at once and is aligned at the clock start.</summary>
        NotMoved,
    }

    /// <summary>How the release reads one output from its last queue reading (ADR-0008).</summary>
    internal static ReleaseReading ReadForRelease(bool queueRead, long queuePlayed, long latestPlayed, bool pastDeadline, bool stalled)
    {
        if (queueRead && queuePlayed > 0)
        {
            return ReleaseReading.Moved;
        }

        // A render was updating the queue of an output that has a position: read it again on the next poll, or it would
        // get the film at once, ~its lag early, and its lag would be left out of the clock start.
        if (!queueRead && latestPlayed > 0 && !stalled)
        {
            return ReleaseReading.Wait;
        }

        return !pastDeadline && !stalled ? ReleaseReading.Wait : ReleaseReading.NotMoved;
    }

    /// <summary>
    /// Blocks the loop until every output has discarded its old-epoch audio and its device has played past it.
    /// Returns false when the decoder ended without delivering any audio at the target (seek past the end).
    /// </summary>
    private Result<bool> WaitForSeek(Run run, Output[] active, int epoch)
    {
        var deadline = _time.GetTimestamp() + (long)(StartLevelTimeout.TotalSeconds * _time.TimestampFrequency);
        while (true)
        {
            if (run.DecodeError is { } error)
            {
                return Result<bool>.Failure(error);
            }

            // A hung output never plays past the old epoch; only the outputs still rendering confirm the seek.
            var confirming = active.Where(o => !run.Pump!.IsStalled(o.Index)).ToArray();
            if (confirming.Length == 0)
            {
                confirming = active;
            }

            if (confirming.All(o => o.Source.AppliedEpoch >= epoch)
                && run.DecodeEnded
                && !active.Any(o => HasAudioOfEpoch(o, epoch)))
            {
                return Result<bool>.Success(false);
            }

            // Audio of the new epoch must have arrived too: an empty ring may still be the decoder reading on towards a
            // seek past the end, which only its end of stream decides. An output whose track has no more blocks in this
            // epoch (it ended before the target, inside the file) is not waited for, as long as another output has audio.
            if (confirming.All(o => o.Source.HasPlayedOutOldEpoch(epoch)
                    && (HasAudioOfEpoch(o, epoch) || run.Pump!.HasNoBlocksInEpoch(o.Index, epoch)))
                && active.Any(o => HasAudioOfEpoch(o, epoch)))
            {
                return Result<bool>.Success(true);
            }

            if (_time.GetTimestamp() >= deadline || Volatile.Read(ref _disposed) != 0)
            {
                return Result<bool>.Failure("playback.seek_timeout", "The outputs did not confirm the seek in time.");
            }

            Thread.Sleep(StartLevelPollInterval);
        }
    }

    /// <summary>
    /// True once audio of <paramref name="epoch"/> reached the output: written to its ring (a late older-epoch block
    /// left there does not count) or already handed to the device.
    /// </summary>
    private static bool HasAudioOfEpoch(Output output, int epoch) =>
        output.Ring.LastWrittenEpoch >= epoch || output.Source.LastContentFrameHandedOut >= output.Source.EpochStartContentFrame;

    /// <summary>
    /// Silences every active output from one master-clock instant beyond the audio queued in the fullest device and
    /// stops the clock at that instant.
    /// </summary>
    private void StopBeyondQueue(Run run, long now)
    {
        var clock = run.Clock!;
        var active = run.Outputs.Where(o => !o.Lost).ToArray();
        var queued = active.Length == 0
            ? TimeSpan.Zero
            : active.Max(o => TimeSpan.FromSeconds((double)Math.Max(0, o.Source.QueuedFrames) / o.Format.SampleRate));
        var stopInstant = clock.PositionAt(now) + queued;
        foreach (var output in active)
        {
            output.Source.SetSilenceFrom((long)Math.Ceiling(stopInstant.TotalSeconds * output.Format.SampleRate));
            output.StopDeviceFrame = output.Source.LatestFramesPlayed + (long)Math.Ceiling(queued.TotalSeconds * output.Format.SampleRate);
        }

        run.StopTimestamp = now + (long)(queued.TotalSeconds * _time.TimestampFrequency);
        clock.StopAt(run.StopTimestamp);
    }

    private void BeginAlignment(Output output, long now)
    {
        output.Sync?.Reset();
        output.Source.SetResamplerRatio(1.0);
        output.AlignPending = true;
        output.AlignRounds = 0;
        output.AlignUntil = now + (long)(AlignWindow.TotalSeconds * _time.TimestampFrequency);
        output.RealignsRequested = output.Source.RealignsApplied;
    }

    private Result OpenOutput(Run run, int index, ResolvedOutput resolved, string name, EngineOptions options)
    {
        var deferred = new FirstAudioRenderSource();
        var opened = _backend.Open(resolved.DeviceId, options.DeviceBuffer, deferred);
        if (!opened.IsSuccess)
        {
            return Result.Failure(opened.Error);
        }

        var device = opened.Value;
        var format = device.MixFormat;
        var ring = new AudioRingBuffer((int)(options.RingBuffer.TotalSeconds * format.SampleRate), format.Channels);
        var source = new OutputRenderSource(ring);
        deferred.Attach(source);
        var output = new Output(index, resolved.DeviceId, name, resolved.TrackNumber, format, ring, source, deferred, device, options.StartLevel);
        output.LostHandler = (_, _) => _loop.Post(() => OnDeviceLost(run, output));
        device.DeviceLost += output.LostHandler;
        run.Outputs.Add(output);
        return Result.Success();
    }

    /// <summary>
    /// Blocks the loop until every ring holds its start level (or the file ended before it could); fails with the
    /// decoder's error as soon as it reports one, or after <see cref="StartLevelTimeout"/> on the injected clock.
    /// </summary>
    private Result WaitForStartLevel(Run run)
    {
        var deadline = _time.GetTimestamp() + (long)(StartLevelTimeout.TotalSeconds * _time.TimestampFrequency);
        while (true)
        {
            if (run.DecodeError is { } error)
            {
                return Result.Failure(error);
            }

            if (run.DecodeEnded || run.Outputs.Where(o => !o.Lost).All(o => o.Ring.FramesAvailable >= o.StartLevelFrames))
            {
                return Result.Success();
            }

            if (_time.GetTimestamp() >= deadline || Volatile.Read(ref _disposed) != 0)
            {
                return Result.Failure("playback.start_timeout", "The decoder did not fill the output buffers in time.");
            }

            Thread.Sleep(StartLevelPollInterval);
        }
    }

    private void OnPumpEnded(Run run) =>
        _loop.Post(() =>
        {
            if (_run == run)
            {
                _loop.Polling = true;
            }
        });

    /// <summary>
    /// A decoder failure during the start is returned by <see cref="StartRun"/>; once running, it ends the run with
    /// <see cref="RunEndReason.Stop"/> (the summary has no dedicated reason; the log names the decoder error).
    /// </summary>
    private void OnDecodeFailed(Run run, Error error)
    {
        run.DecodeError = error;
        LogDecodeFailed(error.Code, error.Message);
        _loop.Post(() =>
        {
            if (_run == run)
            {
                EndRun(run, RunEndReason.Stop);
            }
        });
    }

    /// <summary>
    /// Polled while a run still has work between commands: log each output's first audio, and once the file has
    /// ended, end the run when every active output has drained its ring.
    /// </summary>
    private void Poll()
    {
        if (_run is not { } run)
        {
            _loop.Polling = false;
            return;
        }

        if (AdvanceStart(run))
        {
            _loop.Polling = true;
            return;
        }

        var awaitingFirstAudio = LogFirstAudio(run);
        if (run.DecodeEnded && run.Outputs.Where(o => !o.Lost).All(o => o.Ring.FramesAvailable == 0))
        {
            EndRun(run, RunEndReason.FileEnd);
            return;
        }

        var aligning = AlignOutputs(run);
        // AudioStarted waits for a never-rendered output until FirstAudioDeadline, then not for a stalled one; polling continues
        // until every output logs its first audio (if it recovers).
        var unlogged = run.Outputs.Any(o => !o.Lost && !o.FirstAudioLogged);
        _loop.Polling = awaitingFirstAudio || unlogged || run.DecodeEnded || aligning;
    }

    /// <summary>
    /// One-step re-alignment after the start, a seek or a resume: once an output has handed a full buffer of audio from the new
    /// position to the device, measures how far that audio is from the master clock when the device plays it and
    /// removes the offset. Each output keeps being measured for <see cref="AlignWindow"/> after the command, so a
    /// hole that appears after a good measurement (the decoder not keeping up) is removed as well. True while some
    /// output is still being aligned. Continuous drift correction is the sync controller's job.
    /// </summary>
    private bool AlignOutputs(Run run)
    {
        if (run.Paused)
        {
            return false;
        }

        var pending = false;
        var now = _time.GetTimestamp();
        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            if (output.AlignAfterRecovery)
            {
                pending = true;
                StartAlignmentIfRecovered(run, output, now);
            }

            if (!output.AlignPending)
            {
                continue;
            }

            var source = output.Source;
            if (!TryMeasureHeardContent(run, output, out var content, out var heardAt))
            {
                pending = true; // the last correction or the first audio is still on its way, or a render moved while reading
                continue;
            }

            var drift = content - (run.Clock!.PositionAt(heardAt) - output.Sync!.Lag);
            var inTolerance = Math.Abs(drift.TotalMilliseconds) <= AlignTolerance.TotalMilliseconds;
            if (output.AlignRounds >= MaxAlignRounds || (inTolerance && now >= output.AlignUntil))
            {
                output.AlignPending = false;
                continue;
            }

            pending = true;
            if (inTolerance)
            {
                continue; // aligned for now; keep measuring until the window closes
            }

            // A hole in the audio (the decoder not keeping up) shows up as drift later: remove it and measure again.
            output.Sync!.AddReading(content, heardAt, SyncSampleInterval, correct: false); // the record tells what was played before the correction
            source.RequestRealign(DriftController.ResamplerRealignFrames(drift, output.Format.SampleRate));
            output.RealignsRequested++;
            output.AlignRounds++;
            LogAligned(output.DeviceId, run.Clock.PositionAt(now), drift.TotalMilliseconds, output.AlignRounds);
        }

        return pending;
    }

    /// <summary>
    /// The content of the latest full audio buffer and the timestamp the device plays it at: a device buffer is heard
    /// when the device reaches its first frame. False while a re-alignment or the first audio is still on its way, or
    /// a render moved while reading.
    /// </summary>
    private bool TryMeasureHeardContent(Run run, Output output, out TimeSpan content, out long heardAt)
    {
        var source = output.Source;
        var rate = output.Format.SampleRate;
        if (source.AppliedEpoch != run.Epoch // a hung device still shows the pre-seek reading
            || source.RealignsApplied < output.RealignsRequested
            || !source.TryGetLatestPosition(out var played, out var timestamp)
            || !source.TryGetLatestAudioBuffer(out var bufferFrame, out var bufferContent))
        {
            content = TimeSpan.Zero;
            heardAt = 0;
            return false;
        }

        heardAt = timestamp + (long)((double)(bufferFrame - played) / rate * _time.TimestampFrequency);
        content = TimeSpan.FromSeconds(bufferContent / rate);
        return true;
    }

    /// <summary>
    /// An output that hung during a seek is aligned once it renders in the current epoch: its measurement then comes
    /// from audio of the new position.
    /// </summary>
    private void StartAlignmentIfRecovered(Run run, Output output, long now)
    {
        if (output.Source.AppliedEpoch != run.Epoch)
        {
            return;
        }

        output.AlignAfterRecovery = false;
        BeginAlignment(output, now);

        // What the device was still playing when it came back is the audio from before the seek: the record says so.
        var staleFrame = output.Source.PreviousEpochLastContentFrame;
        if (staleFrame >= 0)
        {
            output.Sync!.AddReading(TimeSpan.FromSeconds((double)staleFrame / output.Format.SampleRate), now, SyncSampleInterval, correct: false);
        }
    }

    /// <summary>Starts the recovery window (start, resume, seek): records in it are not held to the drift tolerance.</summary>
    private void BeginRecovery(Run run, long now)
    {
        run.RecoveryUntil = now + (long)(OutputSync.RecoveryTime.TotalSeconds * _time.TimestampFrequency);
        run.IntervalInRecovery = true;
    }

    /// <summary>
    /// Every <see cref="SyncSampleInterval"/> on the control loop: takes a drift reading from every playing output and
    /// updates its rate correction; every <see cref="SamplesPerRecord"/> readings publishes the sync records.
    /// </summary>
    private void OnSyncSample(Run run)
    {
        if (_run != run)
        {
            return;
        }

        var now = _time.GetTimestamp();
        var sinceLast = _time.GetElapsedTime(run.LastLagAdvance, now);
        run.LastLagAdvance = now;
        if (run.Paused)
        {
            return;
        }

        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            output.Sync!.AdvanceLag(sinceLast);
        }

        run.IntervalInRecovery |= now < run.RecoveryUntil;
        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            SampleOutput(run, output, now);
        }

        if (++run.SamplesInInterval < SamplesPerRecord)
        {
            return;
        }

        PublishSyncRecords(run, now);
        run.SamplesInInterval = 0;
        run.IntervalInRecovery = now < run.RecoveryUntil;
    }

    private void SampleOutput(Run run, Output output, long now)
    {
        var source = output.Source;
        var hasPosition = source.TryGetLatestPosition(out var played, out var timestamp);
        if (hasPosition)
        {
            output.Mismatch!.AddReading(timestamp, TimeSpan.FromSeconds((double)played / output.Format.SampleRate));
        }

        var sync = output.Sync!;
        var measured = TryMeasureHeardContent(run, output, out var content, out var heardAt);
        var drift = TimeSpan.Zero;
        if (measured)
        {
            // While a seek or resume is being aligned in steps, the rate stays at unity so the two do not fight.
            drift = sync.AddReading(content, heardAt, SyncSampleInterval, correct: !output.AlignPending);
            if (!output.AlignPending)
            {
                source.SetResamplerRatio(sync.ResamplerRatio);
                RealignStep(run, output, now);
            }
        }

        if (measured)
        {
            output.LatestErrorMs = drift.TotalMilliseconds;
            output.LatestErrorAt = now;
        }

        output.Trace!.Add(new DriftReading(
            run.Clock!.PositionAt(now),
            measured,
            hasPosition ? _time.GetElapsedTime(timestamp, now) : TimeSpan.Zero,
            played,
            measured ? TimeSpan.FromSeconds((double)(heardAt - timestamp) / _time.TimestampFrequency) : TimeSpan.Zero,
            drift,
            TimeSpan.FromSeconds((double)output.Ring.FramesAvailable / output.Format.SampleRate),
            sync.CorrectionPpm,
            source.UnderrunCount));
    }

    /// <summary>
    /// A drift step confirmed by consecutive readings is removed at once, not at the end of the record interval
    /// (spec §6: back within the tolerance 0.5 s after the step). Not inside a recovery window, where the alignment
    /// after the command is still settling.
    /// </summary>
    private void RealignStep(Run run, Output output, long now)
    {
        var source = output.Source;
        var mayRealign = now >= run.RecoveryUntil && source.RealignsApplied >= output.RealignsRequested;
        var step = output.Sync!.TakeStepRealignment(mayRealign);
        if (step == TimeSpan.Zero)
        {
            return;
        }

        source.RequestRealign(DriftController.ResamplerRealignFrames(step, output.Format.SampleRate));
        output.RealignsRequested++;
        run.Summary.RecordCorrection(output.DeviceId);
        run.StepRealignedInInterval = true;
        LogStepRealigned(output.DeviceId, run.Clock!.PositionAt(now), step.TotalMilliseconds, OutputSync.StepReadings);
    }

    /// <summary>
    /// Once per second: closes every output's record interval, re-aligns in one step an output whose drift is too
    /// large for the rate adjustment (counting a correction), and publishes and logs one sync record per device.
    /// </summary>
    private void PublishSyncRecords(Run run, long now)
    {
        var inRecovery = run.IntervalInRecovery;
        var clockPosition = run.Clock!.PositionAt(now);
        var beyondTolerance = false;
        var samples = new List<OutputSample>(run.Outputs.Count);
        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            var source = output.Source;
            var mayRealign = !inRecovery && !output.AlignPending && source.RealignsApplied >= output.RealignsRequested;
            var interval = output.Sync!.CloseInterval(mayRealign);
            if (interval.Realignment != TimeSpan.Zero)
            {
                source.RequestRealign(DriftController.ResamplerRealignFrames(interval.Realignment, output.Format.SampleRate));
                output.RealignsRequested++;
                run.Summary.RecordCorrection(output.DeviceId);
                LogRealigned(output.DeviceId, clockPosition, interval.Realignment.TotalMilliseconds, interval.AverageDrift.TotalMilliseconds);
            }

            beyondTolerance |= !inRecovery && interval.AverageDrift.Duration() > run.DriftTolerance;
            var driftMs = interval.AverageDrift.TotalMilliseconds;
            var underruns = source.UnderrunCount;
            run.Summary.RecordDrift(output.DeviceId, driftMs, inRecovery);
            run.Summary.RecordClockMismatch(output.DeviceId, output.Mismatch!.EstimatedPpm);
            run.Summary.RecordUnderruns(output.DeviceId, underruns);
            samples.Add(new OutputSample(output.DeviceId, interval.AverageDrift, output.Sync.CorrectionPpm, underruns, inRecovery));
        }

        run.Publisher!.Publish(clockPosition, samples);
        FlushDriftTraces(run, beyondTolerance || run.StepRealignedInInterval);
        run.StepRealignedInInterval = false;
    }

    /// <summary>
    /// Writes the raw 100 ms readings of every output to the log for a record interval with an output beyond the drift
    /// tolerance or re-aligned after a drift step (together with the interval before it) and for the interval that
    /// follows: what each device reported before, during and after it, side by side. A step removed after a few
    /// readings can leave its 1 s record within the tolerance, so the record alone would not show it.
    /// </summary>
    private static void FlushDriftTraces(Run run, bool disturbed)
    {
        if (!disturbed && !run.TraceNextInterval)
        {
            return;
        }

        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            output.Trace!.Flush();
        }

        run.TraceNextInterval = disturbed;
    }

    /// <summary>
    /// How long after the start an output whose position has not moved can hold the film's release (only while it keeps
    /// rendering: one that stops counts as stalled after <see cref="DecodePump.StallThreshold"/> and no longer holds it),
    /// and an output that has never rendered is still awaited by <see cref="AudioStarted"/>: above the 3 s start-to-audio
    /// budget, so a first render that misses the budget is reported, not hidden behind a faster output.
    /// </summary>
    private static readonly TimeSpan FirstAudioDeadline = TimeSpan.FromSeconds(5);

    /// <summary>Logs start-to-audio for every output that has rendered its first audio; true while some still have not.</summary>
    private bool LogFirstAudio(Run run)
    {
        var awaiting = false;
        foreach (var output in run.Outputs.Where(o => !o.Lost && !o.FirstAudioLogged))
        {
            if (output.Gate.FirstAudioTimestamp is not { } timestamp)
            {
                // A hung output is not waited for, as a seek or stop does not wait for it; it is logged if it recovers.
                // One that has never rendered is awaited until the start deadline, so a late first render is covered.
                var neverRendered = output.Ring.LastReadTimestamp == 0 && _time.GetElapsedTime(run.StartTimestamp) < FirstAudioDeadline;
                awaiting |= neverRendered || !run.Pump!.IsStalled(output.Index);
                continue;
            }

            // Film handed over before the clock started is heard when it starts.
            timestamp = Math.Max(timestamp, run.ClockStartTimestamp);
            output.FirstAudioLogged = true;
            run.LatestFirstAudio = run.LatestFirstAudio is { } previous ? Math.Max(previous, timestamp) : timestamp;
            LogFirstAudio(output.DeviceId, _time.GetElapsedTime(run.StartTimestamp, timestamp).TotalMilliseconds);
        }

        // Once per run, when no non-lost output still waits; an output lost before its first audio, one that rendered only silence
        // and then stalled, or one that never rendered and is stalled past FirstAudioDeadline is not waited for, and a run that ends
        // first (or loses every output) publishes nothing.
        if (!awaiting && !run.AudioStartedPublished && run.LatestFirstAudio is { } latest)
        {
            run.AudioStartedPublished = true;
            if (!run.ContinuesPreviousRun)
            {
                _events.Publish(new AudioStarted(run.StartClockPosition, _time.GetElapsedTime(run.StartTimestamp, latest)));
            }
        }

        return awaiting;
    }

    private void OnDeviceLost(Run run, Output output)
    {
        if (_run != run || output.Lost)
        {
            return;
        }

        output.Lost = true;
        run.Pump!.Detach(output.Index);
        output.Device.Close();
        var position = run.Clock!.PositionAt(_time.GetTimestamp());
        var lost = new DeviceLost(output.Index, output.DeviceId, position, run.Outputs.Any(o => !o.Lost));
        run.Summary.RecordDeviceLost(lost);
        run.Publisher?.OutputLost(output.DeviceId, position);
        LogDeviceLost(output.DeviceId, position.TotalSeconds, lost.OthersContinue);
        _events.Publish(lost);

        if (!lost.OthersContinue)
        {
            EndRun(run, RunEndReason.AllDevicesLost);
        }
    }

    private void EndRun(Run run, RunEndReason reason)
    {
        var clock = run.Clock!;
        var now = _time.GetTimestamp();
        run.SyncTimer?.Dispose();
        if (run.Starting)
        {
            foreach (var output in run.Outputs)
            {
                output.Source.SetSilenceFrom(0); // no film after a stop during the pre-roll; nothing to wait for
            }

            // Past the release each output may already hold film in its device queue (the slowest up to L_max, another
            // one from clockStart − L_i on). Nothing waits for it to play out: this assumes closing the device (WASAPI
            // Stop + release) discards that queue. A Bluetooth headset may still play a short tail (ADR-0008: audio
            // handed over cannot be recalled); sad.md §11 records it and the target-PC run checks it.
        }
        else if (!run.Paused)
        {
            StopBeyondQueue(run, now); // paused: already silent from the pause instant, which must not move later
        }

        WaitForDevicesToPassStop(run);
        run.Pump!.Dispose();
        run.Publisher?.Finish(clock.PositionAt(now));
        foreach (var output in run.Outputs)
        {
            run.Summary.RecordUnderruns(output.DeviceId, output.Source.UnderrunCount);
        }

        var summary = run.Summary.Build(reason, clock.PositionAt(now), run.File ?? string.Empty);
        LogRunEnded(reason.ToString(), summary.ClockPosition.TotalSeconds, summary.Outputs.Count);
        foreach (var output in summary.Outputs)
        {
            LogOutputSummary(output.DeviceId, output.Played.TotalSeconds, output.MaxDriftMs, output.ClockMismatchPpm, output.Corrections, output.Underruns, output.DeviceLostEvents.Count);
        }

        _run = null;
        _lastSummary = summary;
        _lastSummaryFile = run.File;
        _loop.Polling = false;
        _events.Publish(summary);
        Teardown(run);
    }

    /// <summary>
    /// Blocks the loop until every output's device has played past the stop instant, so no device closes (and cuts its
    /// queued audio) while another is still audible. Hung and lost outputs are not waited for; bounded by <see cref="StopTimeout"/>.
    /// </summary>
    private void WaitForDevicesToPassStop(Run run)
    {
        var deadline = _time.GetTimestamp() + (long)(StopTimeout.TotalSeconds * _time.TimestampFrequency);
        var polls = 0;
        while (run.Outputs.Any(o => !o.Lost && !run.Pump!.IsStalled(o.Index) && o.Source.LatestFramesPlayed < o.StopDeviceFrame))
        {
            if (_time.GetTimestamp() >= deadline || ++polls > run.StopWaitMaxPolls || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            Thread.Sleep(StartLevelPollInterval);
        }
    }

    private static void Teardown(Run run)
    {
        run.SyncTimer?.Dispose();
        run.Pump?.Dispose();
        foreach (var output in run.Outputs)
        {
            output.Device.DeviceLost -= output.LostHandler;
            if (!output.Lost)
            {
                output.Device.Close();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run started with {Outputs} outputs; devices started {StartToDevicesMs:F1} ms after the start command")]
    private partial void LogRunStarted(int outputs, double startToDevicesMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Film released on {Measured} of {Outputs} outputs; master clock starts {StartToClockMs:F1} ms after the start command")]
    private partial void LogFilmReleased(int measured, int outputs, double startToClockMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "First audio on {DeviceId} {StartToAudioMs:F1} ms after the start command")]
    private partial void LogFirstAudio(string deviceId, double startToAudioMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-aligned {DeviceId} in one step at master clock {ClockPosition}: removed {RealignMs:F1} ms, the interval's average drift was {AverageDriftMs:F1} ms")]
    private partial void LogRealigned(string deviceId, TimeSpan clockPosition, double realignMs, double averageDriftMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-aligned {DeviceId} in one step at master clock {ClockPosition}: removed {RealignMs:F1} ms after {Readings} readings beyond the threshold")]
    private partial void LogStepRealigned(string deviceId, TimeSpan clockPosition, double realignMs, int readings);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aligned {DeviceId} after a start, seek or resume at master clock {ClockPosition}: removed {RealignMs:F1} ms (round {Round})")]
    private partial void LogAligned(string deviceId, TimeSpan clockPosition, double realignMs, int round);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device {DeviceId} lost at master clock {ClockSeconds:F3} s; others continue: {OthersContinue}")]
    private partial void LogDeviceLost(string deviceId, double clockSeconds, bool othersContinue);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run ended ({Reason}) at master clock {ClockSeconds:F3} s with {Outputs} outputs")]
    private partial void LogRunEnded(string reason, double clockSeconds, int outputs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Output {DeviceId}: played {PlayedSeconds:F3} s, max drift {MaxDriftMs:F2} ms, clock mismatch {MismatchPpm:F1} ppm, corrections {Corrections}, underruns {Underruns}, device-lost events {LostEvents}")]
    private partial void LogOutputSummary(string deviceId, double playedSeconds, double maxDriftMs, double mismatchPpm, int corrections, long underruns, int lostEvents);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seek target was outside the file ({Where})")]
    private partial void LogSeekOutsideFile(string where);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Decoder failed ({ErrorCode}): {ErrorMessage}; ending the run")]
    private partial void LogDecodeFailed(string errorCode, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine-control loop command failed")]
    private partial void LogLoopFault(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "An engine event observer failed")]
    private partial void LogObserverFault(Exception ex);

    private sealed class Run(long startTimestamp)
    {
        private Error? _decodeError;

        /// <summary>When the start command arrived, on the injected clock.</summary>
        public long StartTimestamp { get; } = startTimestamp;

        public int StopWaitMaxPolls { get; init; }

        /// <summary>Master-clock position at the start of the run.</summary>
        public TimeSpan StartClockPosition { get; set; }

        /// <summary>Latest first-audio timestamp over the outputs that rendered one.</summary>
        public long? LatestFirstAudio { get; set; }

        public bool AudioStartedPublished { get; set; }

        public string? File { get; init; }

        public bool ContinuesPreviousRun { get; set; }

        /// <summary>The start's pre-roll: the master clock has not started yet (control-loop thread only).</summary>
        public bool Starting { get; set; }

        /// <summary>The film has been handed to the outputs at their start device frames.</summary>
        public bool Released { get; set; }

        /// <summary>When the master clock reads 00:00: the slowest output hears film frame 0 (set at the release).</summary>
        public long ClockStartTimestamp { get; set; }

        /// <summary>When the slowest output hears film frame 0 (the clock start plus the largest start lag); set at the release.</summary>
        public long? FrameZeroTimestamp { get; set; }

        /// <summary>When the lag ramps were last advanced (control-loop thread only).</summary>
        public long LastLagAdvance { get; set; }

        public List<Output> Outputs { get; } = [];

        public RunSummaryBuilder Summary { get; } = new();

        public MasterClock? Clock { get; set; }

        public DecodePump? Pump { get; set; }

        /// <summary>The decoder reached the end of the file in the current epoch (control-loop thread only).</summary>
        public bool DecodeEnded => Pump is { } pump && pump.EndedEpoch == Epoch;

        /// <summary>Playback epoch; seeks increment it (control-loop thread only).</summary>
        public int Epoch { get; set; }

        /// <summary>Length of the file when the decoder knows it.</summary>
        public TimeSpan? FileDuration { get; set; }

        /// <summary>Timestamp of the last pause or stop instant (the clock stopped there); <see cref="long.MinValue"/> when none applies.</summary>
        public long StopTimestamp { get; set; } = long.MinValue;

        public bool Paused { get; set; }

        /// <summary>Fires every sync sample interval and posts the sample to the control loop.</summary>
        public ITimer? SyncTimer { get; set; }

        public SyncRecordPublisher? Publisher { get; set; }

        /// <summary>Timestamp the current recovery window (after start, resume or seek) closes at.</summary>
        public long RecoveryUntil { get; set; }

        /// <summary>Some reading of the current record interval fell inside a recovery window.</summary>
        public bool IntervalInRecovery { get; set; }

        public int SamplesInInterval { get; set; }

        /// <summary>Spec §6 steady-state drift tolerance of this run.</summary>
        public TimeSpan DriftTolerance { get; set; }

        /// <summary>The last record interval had an output beyond the tolerance or re-aligned: the next one's readings are logged too.</summary>
        public bool TraceNextInterval { get; set; }

        /// <summary>An output was re-aligned after a drift step in the current record interval.</summary>
        public bool StepRealignedInInterval { get; set; }

        /// <summary>Set from the decode thread when the decoder reports a failure.</summary>
        public Error? DecodeError
        {
            get => Volatile.Read(ref _decodeError);
            set => Volatile.Write(ref _decodeError, value);
        }
    }

    private sealed class Output(
        int index,
        string deviceId,
        string deviceName,
        int trackNumber,
        MixFormat format,
        AudioRingBuffer ring,
        OutputRenderSource source,
        FirstAudioRenderSource gate,
        IAudioDevice device,
        TimeSpan startLevel)
    {
        public int Index { get; } = index;

        public string DeviceId { get; } = deviceId;

        public string DeviceName { get; } = deviceName;

        public int TrackNumber { get; } = trackNumber;

        public MixFormat Format { get; } = format;

        public AudioRingBuffer Ring { get; } = ring;

        public OutputRenderSource Source { get; } = source;

        public FirstAudioRenderSource Gate { get; } = gate;

        public bool FirstAudioLogged { get; set; }

        public IAudioDevice Device { get; } = device;

        public int StartLevelFrames { get; } = (int)(startLevel.TotalSeconds * format.SampleRate);

        public EventHandler LostHandler { get; set; } = static (_, _) => { };

        public bool Lost { get; set; }

        /// <summary>Device position at which the output has played everything up to the last stop or pause instant.</summary>
        public long StopDeviceFrame { get; set; }

        /// <summary>The output's audio must still be re-aligned to the master clock (after a seek or resume).</summary>
        public bool AlignPending { get; set; }

        /// <summary>The output hung during a seek: it is aligned once it applies the new epoch.</summary>
        public bool AlignAfterRecovery { get; set; }

        public int AlignRounds { get; set; }

        /// <summary>Timestamp until which the output keeps being re-measured after the command.</summary>
        public long AlignUntil { get; set; }

        /// <summary>Re-alignments requested so far, counted on the same scale as <see cref="OutputRenderSource.RealignsApplied"/>.</summary>
        public int RealignsRequested { get; set; }

        /// <summary>Drift readings, rate correction and the one-step re-alignment decision (set when the clock starts).</summary>
        public OutputSync? Sync { get; set; }

        public ClockMismatchEstimator? Mismatch { get; set; }

        /// <summary>The latest raw sync samples, logged around a drift excursion (set when the clock is created).</summary>
        public DriftTrace? Trace { get; set; }

        /// <summary>The latest measured position error and when it was taken; <see cref="long.MinValue"/> until the first reading.</summary>
        public double LatestErrorMs { get; set; }

        public long LatestErrorAt { get; set; } = long.MinValue;
    }

    /// <summary>
    /// Forwards to the real source once the device's mix format is known and the ring exists, and records the
    /// device timestamp of the first render that handed out audio (start-to-audio).
    /// </summary>
    private sealed class FirstAudioRenderSource : IRenderSource
    {
        private const long NoAudioYet = long.MinValue;

        private OutputRenderSource? _inner;
        private long _firstAudioTimestamp = NoAudioYet;

        /// <summary>Device timestamp of the first render that handed out audio; null until then.</summary>
        public long? FirstAudioTimestamp =>
            Interlocked.Read(ref _firstAudioTimestamp) is var t && t != NoAudioYet ? t : null;

        public void Attach(OutputRenderSource source) => Volatile.Write(ref _inner, source);

        public void Render(Span<float> buffer, DevicePosition position)
        {
            if (Volatile.Read(ref _inner) is not { } inner)
            {
                buffer.Clear();
                return;
            }

            inner.Render(buffer, position);
            if (Interlocked.Read(ref _firstAudioTimestamp) == NoAudioYet && inner.HasHandedOutAudio)
            {
                Interlocked.Exchange(ref _firstAudioTimestamp, position.Timestamp);
            }
        }
    }

    private sealed class EventPublisher(Action<Exception> onObserverFault) : IObservable<EngineEvent>
    {
        private readonly Lock _gate = new();
        private IObserver<EngineEvent>[] _observers = [];

        public IDisposable Subscribe(IObserver<EngineEvent> observer)
        {
            lock (_gate)
            {
                _observers = [.. _observers, observer];
            }

            return new Subscription(this, observer);
        }

        public void Publish(EngineEvent engineEvent)
        {
            IObserver<EngineEvent>[] snapshot;
            lock (_gate)
            {
                snapshot = _observers;
            }

            foreach (var observer in snapshot)
            {
                try
                {
                    observer.OnNext(engineEvent);
                }
                catch (Exception ex)
                {
                    onObserverFault(ex);
                }
            }
        }

        public void Complete()
        {
            IObserver<EngineEvent>[] snapshot;
            lock (_gate)
            {
                snapshot = _observers;
                _observers = [];
            }

            foreach (var observer in snapshot)
            {
                observer.OnCompleted();
            }
        }

        private void Remove(IObserver<EngineEvent> observer)
        {
            lock (_gate)
            {
                _observers = [.. _observers.Where(o => !ReferenceEquals(o, observer))];
            }
        }

        private sealed class Subscription(EventPublisher owner, IObserver<EngineEvent> observer) : IDisposable
        {
            public void Dispose() => owner.Remove(observer);
        }
    }
}
