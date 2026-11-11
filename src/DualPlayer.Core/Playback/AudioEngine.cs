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

            if (run.DecodeEnded || run.Outputs.All(o => o.Ring.FramesAvailable >= o.StartLevelFrames))
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

    private void OnPumpEnded(Run run)
    {
        run.DecodeEnded = true;
        _loop.Post(() =>
        {
            if (_run == run)
            {
                _loop.Polling = true;
            }
        });
    }

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

        _loop.Polling = awaitingFirstAudio || run.DecodeEnded;
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
        var active = run.Outputs.Where(o => !o.Lost).ToArray();

        // Stop beyond the audio already queued in every active device: silence from exactly that instant.
        var queued = active.Length == 0
            ? TimeSpan.Zero
            : active.Max(o => TimeSpan.FromSeconds((double)Math.Max(0, o.Source.QueuedFrames) / o.Format.SampleRate));
        var stopInstant = clock.PositionAt(now) + queued;
        foreach (var output in active)
        {
            output.Source.SetSilenceFrom((long)Math.Ceiling(stopInstant.TotalSeconds * output.Format.SampleRate));
        }

        clock.StopAt(now + (long)(queued.TotalSeconds * _time.TimestampFrequency));
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

        public volatile bool DecodeEnded;

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
