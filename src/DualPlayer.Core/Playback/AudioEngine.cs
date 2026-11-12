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
public sealed partial class AudioEngine : IAudioEngine, IDisposable
{
    private static readonly TimeSpan StartLevelTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AlignTolerance = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private const int MaxAlignRounds = 16;

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

        var run = new Run(startTimestamp);
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
        run.Pump.Start(0);

        var filled = WaitForStartLevel(run);
        if (!filled.IsSuccess)
        {
            Teardown(run);
            return Result<RunStarted>.Failure(filled.Error);
        }

        run.Clock = new MasterClock(_time);
        run.Clock.Start();
        foreach (var output in run.Outputs)
        {
            output.Sync = new OutputSync(run.Clock, options.DriftTolerance);
            output.Mismatch = new ClockMismatchEstimator(_time);
            output.Device.Start();
            run.Summary.OutputStarted(output.DeviceId, TimeSpan.Zero);
        }

        var now = _time.GetTimestamp();
        BeginRecovery(run, now);
        foreach (var output in run.Outputs)
        {
            BeginAlignment(output, now); // one re-align step per output, whenever its position starts moving
        }

        run.Publisher = new SyncRecordPublisher(
            _events.Publish,
            (ILogger?)_loggerFactory?.CreateLogger<SyncRecordPublisher>() ?? _logger);
        _run = run;
        run.SyncTimer = _time.CreateTimer(_ => _loop.Post(() => OnSyncSample(run)), null, SyncSampleInterval, SyncSampleInterval);
        _loop.Polling = true; // until every output's first audio is logged
        run.StartClockPosition = run.Clock.PositionAt(now);
        LogRunStarted(run.Outputs.Count, _time.GetElapsedTime(startTimestamp, now).TotalMilliseconds);
        return Result<RunStarted>.Success(new RunStarted(
            [.. run.Outputs.Select(o => new StartedOutput(o.TrackNumber, o.DeviceId, o.DeviceName, o.Format)
            {
                Source = tracks.Value.FirstOrDefault(t => t.Number == o.TrackNumber),
            })],
            run.StartClockPosition));
    }

    private Result<TimeSpan> PauseCore()
    {
        if (_run is not { } run)
        {
            return Result<TimeSpan>.Failure("playback.no_run", "No run is active.");
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
                && confirming.All(o => o.Ring.FramesAvailable == 0 && o.Source.LastContentFrameHandedOut < o.Source.EpochStartContentFrame))
            {
                return Result<bool>.Success(false);
            }

            if (confirming.All(o => o.Source.HasPlayedOutOldEpoch(epoch)))
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

        var awaitingFirstAudio = LogFirstAudio(run);
        if (run.DecodeEnded && run.Outputs.Where(o => !o.Lost).All(o => o.Ring.FramesAvailable == 0))
        {
            EndRun(run, RunEndReason.FileEnd);
            return;
        }

        var aligning = AlignOutputs(run);
        _loop.Polling = awaitingFirstAudio || run.DecodeEnded || aligning;
    }

    /// <summary>
    /// One-step re-alignment after a seek or resume: once an output has handed a full buffer of audio from the new
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

            var drift = content - run.Clock!.PositionAt(heardAt);
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
        BeginRecovery(run, now);

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
        if (_run != run || run.Paused)
        {
            return;
        }

        var now = _time.GetTimestamp();
        run.IntervalInRecovery |= now < run.RecoveryUntil;
        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            SampleOutput(run, output);
        }

        if (++run.SamplesInInterval < SamplesPerRecord)
        {
            return;
        }

        PublishSyncRecords(run, now);
        run.SamplesInInterval = 0;
        run.IntervalInRecovery = now < run.RecoveryUntil;
    }

    private void SampleOutput(Run run, Output output)
    {
        var source = output.Source;
        if (source.TryGetLatestPosition(out var played, out var timestamp))
        {
            output.Mismatch!.AddReading(timestamp, TimeSpan.FromSeconds((double)played / output.Format.SampleRate));
        }

        if (!TryMeasureHeardContent(run, output, out var content, out var heardAt))
        {
            return;
        }

        // While a seek or resume is being aligned in steps, the rate stays at unity so the two do not fight.
        var sync = output.Sync!;
        sync.AddReading(content, heardAt, SyncSampleInterval, correct: !output.AlignPending);
        if (!output.AlignPending)
        {
            source.SetResamplerRatio(sync.ResamplerRatio);
        }
    }

    /// <summary>
    /// Once per second: closes every output's record interval, re-aligns in one step an output whose drift is too
    /// large for the rate adjustment (counting a correction), and publishes and logs one sync record per device.
    /// </summary>
    private void PublishSyncRecords(Run run, long now)
    {
        var inRecovery = run.IntervalInRecovery;
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
            }

            var driftMs = interval.AverageDrift.TotalMilliseconds;
            var underruns = source.UnderrunCount;
            run.Summary.RecordDrift(output.DeviceId, driftMs, inRecovery);
            run.Summary.RecordClockMismatch(output.DeviceId, output.Mismatch!.EstimatedPpm);
            run.Summary.RecordUnderruns(output.DeviceId, underruns);
            samples.Add(new OutputSample(output.DeviceId, interval.AverageDrift, output.Sync.CorrectionPpm, underruns, inRecovery));
        }

        run.Publisher!.Publish(run.Clock!.PositionAt(now), samples);
    }

    /// <summary>Logs start-to-audio for every output that has rendered its first audio; true while some still have not.</summary>
    private bool LogFirstAudio(Run run)
    {
        var awaiting = false;
        foreach (var output in run.Outputs.Where(o => !o.Lost && !o.FirstAudioLogged))
        {
            if (output.Gate.FirstAudioTimestamp is not { } timestamp)
            {
                awaiting = true;
                continue;
            }

            output.FirstAudioLogged = true;
            run.LatestFirstAudio = run.LatestFirstAudio is { } previous ? Math.Max(previous, timestamp) : timestamp;
            LogFirstAudio(output.DeviceId, _time.GetElapsedTime(run.StartTimestamp, timestamp).TotalMilliseconds);
        }

        // Once per run, when no non-lost output still waits; an output lost before its first audio is not waited for,
        // and a run that ends first (or loses every output) publishes nothing.
        if (!awaiting && !run.AudioStartedPublished && run.LatestFirstAudio is { } latest)
        {
            run.AudioStartedPublished = true;
            _events.Publish(new AudioStarted(run.StartClockPosition, _time.GetElapsedTime(run.StartTimestamp, latest)));
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
        if (!run.Paused)
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

        var summary = run.Summary.Build(reason, clock.PositionAt(now));
        LogRunEnded(reason.ToString(), summary.ClockPosition.TotalSeconds, summary.Outputs.Count);
        foreach (var output in summary.Outputs)
        {
            LogOutputSummary(output.DeviceId, output.Played.TotalSeconds, output.MaxDriftMs, output.ClockMismatchPpm, output.Corrections, output.Underruns, output.DeviceLostEvents.Count);
        }

        _run = null;
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
        while (run.Outputs.Any(o => !o.Lost && !run.Pump!.IsStalled(o.Index) && o.Source.LatestFramesPlayed < o.StopDeviceFrame))
        {
            if (_time.GetTimestamp() >= deadline || Volatile.Read(ref _disposed) != 0)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "First audio on {DeviceId} {StartToAudioMs:F1} ms after the start command")]
    private partial void LogFirstAudio(string deviceId, double startToAudioMs);

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

        /// <summary>Master-clock position at the start of the run.</summary>
        public TimeSpan StartClockPosition { get; set; }

        /// <summary>Latest first-audio timestamp over the outputs that rendered one.</summary>
        public long? LatestFirstAudio { get; set; }

        public bool AudioStartedPublished { get; set; }

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
            if (Interlocked.Read(ref _firstAudioTimestamp) == NoAudioYet && inner.LastContentFrameHandedOut >= 0)
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
