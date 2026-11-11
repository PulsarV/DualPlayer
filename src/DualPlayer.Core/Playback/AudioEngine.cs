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
    private const int MaxAlignRounds = 16;

    /// <summary>How long after a seek or resume each output keeps being re-measured, so a later hole is corrected too.</summary>
    private static readonly TimeSpan AlignWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StartLevelPollInterval = TimeSpan.FromMilliseconds(2);

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
            _loggerFactory?.CreateLogger<DecodePump>() ?? NullLogger<DecodePump>.Instance);
        run.Pump.Ended += () => OnPumpEnded(run);
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
            output.Device.Start();
            run.Summary.OutputStarted(output.DeviceId, TimeSpan.Zero);
        }

        var now = _time.GetTimestamp();
        _run = run;
        _loop.Polling = true; // until every output's first audio is logged
        LogRunStarted(run.Outputs.Count, _time.GetElapsedTime(startTimestamp, now).TotalMilliseconds);
        return Result<RunStarted>.Success(new RunStarted(
            [.. run.Outputs.Select(o => new StartedOutput(o.TrackNumber, o.DeviceId, o.DeviceName, o.Format))],
            run.Clock.PositionAt(now)));
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
        foreach (var output in run.Outputs.Where(o => !o.Lost))
        {
            output.Source.ClearSilence();
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
            target = TimeSpan.Zero;
            LogSeekOutsideFile("before the start");
        }

        var active = run.Outputs.Where(o => !o.Lost).ToArray();
        var heardBefore = active.Length == 0
            ? TimeSpan.Zero
            : active.Max(o => TimeSpan.FromSeconds((double)(o.Source.LastContentFrameHandedOut + 1) / o.Format.SampleRate));
        var epoch = ++run.Epoch; // a new epoch is not ended until the pump reports it so
        var now = _time.GetTimestamp();
        run.Clock!.JumpTo(target);
        run.StopTimestamp = long.MinValue; // a pause instant before the seek no longer applies
        run.Pump!.Seek(target, epoch);
        foreach (var output in active)
        {
            var startFrame = (long)Math.Round(target.TotalSeconds * output.Format.SampleRate);
            output.Source.BeginEpoch(epoch, startFrame);
            if (run.Paused)
            {
                output.Source.SetSilenceFrom(startFrame); // stays silent until resume, which aligns
                output.AlignPending = false;
            }
            else
            {
                BeginAlignment(output, now);
            }
        }

        var confirmed = WaitForSeek(run, active, epoch);
        if (!confirmed.IsSuccess)
        {
            return Result<SeekResult>.Failure(confirmed.Error);
        }

        if (!confirmed.Value)
        {
            // The end is the container's duration; without one, the furthest audio heard before the seek.
            LogSeekOutsideFile("past the end");
            var end = run.FileDuration is { } duration ? (duration < target ? duration : target) : heardBefore;
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

            if (active.All(o => o.Source.AppliedEpoch >= epoch)
                && run.DecodeEnded
                && active.All(o => o.Ring.FramesAvailable == 0 && o.Source.LastContentFrameHandedOut < o.Source.EpochStartContentFrame))
            {
                return Result<bool>.Success(false);
            }

            if (active.All(o => o.Source.HasPlayedOutOldEpoch(epoch)))
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
        }

        run.StopTimestamp = now + (long)(queued.TotalSeconds * _time.TimestampFrequency);
        clock.StopAt(run.StopTimestamp);
    }

    private void BeginAlignment(Output output, long now)
    {
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
        foreach (var output in run.Outputs.Where(o => !o.Lost && o.AlignPending))
        {
            var source = output.Source;
            var rate = output.Format.SampleRate;
            if (source.RealignsApplied < output.RealignsRequested
                || !source.TryGetLatestPosition(out var played, out var timestamp)
                || !source.TryGetLatestAudioBuffer(out var bufferFrame, out var bufferContent))
            {
                pending = true; // the last correction or the first audio is still on its way, or a render moved while reading
                continue;
            }

            // A device buffer is heard when the device reaches its first frame; the content it carries should be
            // what the master clock says at that moment.
            var heardAt = timestamp + (long)((double)(bufferFrame - played) / rate * _time.TimestampFrequency);
            var drift = TimeSpan.FromSeconds(bufferContent / rate) - run.Clock!.PositionAt(heardAt);
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
            source.RequestRealign(DriftController.ResamplerRealignFrames(drift, rate));
            output.RealignsRequested++;
            output.AlignRounds++;
        }

        return pending;
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
            LogFirstAudio(output.DeviceId, _time.GetElapsedTime(run.StartTimestamp, timestamp).TotalMilliseconds);
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
        StopBeyondQueue(run, now);
        run.Pump!.Dispose();
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

    private static void Teardown(Run run)
    {
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

        /// <summary>The output's audio must still be re-aligned to the master clock (after a seek or resume).</summary>
        public bool AlignPending { get; set; }

        public int AlignRounds { get; set; }

        /// <summary>Timestamp until which the output keeps being re-measured after the command.</summary>
        public long AlignUntil { get; set; }

        /// <summary>Re-alignments requested so far, counted on the same scale as <see cref="OutputRenderSource.RealignsApplied"/>.</summary>
        public int RealignsRequested { get; set; }
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
