using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Settings;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>
/// A <see cref="PlaybackSession"/> over the engine rig, a fake renderer, a fake latency probe and a stubbed offsets file.
/// <c>neighbourDecoder</c> and <c>neighbourPictureProbe</c> replace the scripted fakes the session checks a next or previous
/// file with (the Media tests pass the real FFmpeg ones); <see cref="Decoder"/> and <see cref="Pictures"/> are then unused.
/// </summary>
internal sealed class SessionRig : IDisposable
{
    private readonly List<SessionEvent> _events = [];
    private readonly List<string> _runFiles = [];
    private readonly List<string> _callOrder = [];
    private readonly List<TimeSpan> _seekTargets = [];
    private readonly List<TimeSpan> _startPositions = [];
    private readonly Lock _gate = new();
    private readonly Dictionary<string, double> _forcedErrors = new(StringComparer.Ordinal);
    private readonly OffsetBook _offsets;

    public SessionRig(
        RigDevice[] devices,
        DeviceOffsets? remembered = null,
        double totalSeconds = 600,
        Result<DeviceOffsets>? stored = null,
        Func<IOutputLagControl, IOutputLagControl>? wrapLags = null,
        IAudioDecoder? neighbourDecoder = null,
        IPictureProbe? neighbourPictureProbe = null,
        ILogger<PlaybackSession>? log = null,
        int barrierEvery = 1)
    {
        Engine = new EngineRig(devices, totalSeconds, barrierEvery: barrierEvery) { EndsAllowed = true, StrictGate = true };
        Renderer = new FakeVideoRenderer(Engine.Time) { Observer = Record };
        Probe = new FakeDeviceLatencyProbe();
        Pictures = new FakePictureProbe();
        Decoder = new TrackListingDecoder();
        var store = Substitute.For<IStateStore>();
        store.Load(DeviceOffsetsFile.Definition).Returns(stored ?? Result<DeviceOffsets>.Success(remembered ?? new DeviceOffsets()));
        store.Save(DeviceOffsetsFile.Definition, Arg.Any<DeviceOffsets>()).Returns(Result.Success());
        _offsets = new OffsetBook(store, Engine.Time, NullLogger<OffsetBook>.Instance);
        Session = new PlaybackSession(
            new FollowingEngine(this),
            new ForcedErrorLags(this, wrapLags is null ? Engine.Engine : wrapLags(Engine.Engine)),
            Renderer,
            neighbourDecoder ?? Decoder,
            neighbourPictureProbe ?? Pictures,
            Probe,
            _offsets,
            Engine.Backend,
            Engine.Time,
            log ?? NullLogger<PlaybackSession>.Instance);
        Session.Events.Subscribe(new Collector(this));
        Engine.AfterStep = Session.Flush;
    }

    /// <summary>Waits until the session has handled every command, timer and event posted so far.</summary>
    public void Flush() => Session.Flush();

    public EngineRig Engine { get; }

    /// <summary>
    /// How long every engine start, resume and seek takes on the fake clock (a headset's pre-roll, a slow seek): the command
    /// returns only once the caller's rendered steps (<see cref="EngineRig.Call{T}"/>, <see cref="EngineRig.AdvanceUntil"/>)
    /// have moved the clock on that far. The devices keep being fed meanwhile, as a real device is while an engine command
    /// runs; one jump of the clock would leave them that long without audio and the outputs that far off.
    /// </summary>
    public TimeSpan EngineCommandTakes { get; set; }

    /// <summary>
    /// How long every engine stop takes on the fake clock (the engine waiting for the devices to play past the stop point, up to its
    /// 2 s limit): the stop returns only once the caller's rendered steps have moved the clock on that far, the devices fed meanwhile.
    /// </summary>
    public TimeSpan EngineStopTakes { get; set; }

    private void EngineCommandTook() => WaitFakeTime(EngineCommandTakes, "engine command");

    private void WaitFakeTime(TimeSpan takes, string what)
    {
        if (takes <= TimeSpan.Zero)
        {
            return;
        }

        var until = Engine.Time.GetUtcNow() + takes;
        Engine.Time.SignalWait(); // the command now waits on fake time: it may move
        RigLoops.WaitOrFail(() => Engine.Time.GetUtcNow() >= until, TimeSpan.FromSeconds(10), $"the caller to render {takes.TotalMilliseconds:F0} ms of the {what}");
    }

    /// <summary>
    /// Makes the engine's current position error reading of <paramref name="deviceId"/> report <paramref name="driftMs"/> from now on
    /// (null = the engine's own measurement again): an output that is slow to re-align, which the fake devices cannot show on their own.
    /// </summary>
    public void ForceOutputError(string deviceId, double? driftMs)
    {
        lock (_gate)
        {
            if (driftMs is { } ms)
            {
                _forcedErrors[deviceId] = ms;
            }
            else
            {
                _forcedErrors.Remove(deviceId);
            }
        }
    }

    /// <summary>The files the session started an engine run for, in order (what the audio of each run was opened from).</summary>
    public IReadOnlyList<string> EngineRunFiles
    {
        get
        {
            lock (_gate)
            {
                return [.. _runFiles];
            }
        }
    }

    /// <summary>The start position of every engine run the session asked for, in order.</summary>
    public IReadOnlyList<TimeSpan> EngineStartPositions
    {
        get
        {
            lock (_gate)
            {
                return [.. _startPositions];
            }
        }
    }

    /// <summary>The targets the session asked the engine to seek to, in order.</summary>
    public IReadOnlyList<TimeSpan> EngineSeekTargets
    {
        get
        {
            lock (_gate)
            {
                return [.. _seekTargets];
            }
        }
    }

    public FakeVideoRenderer Renderer { get; }

    /// <summary>The engine stops, published events and renderer calls the order tests care about, in the order they happened.</summary>
    public IReadOnlyList<string> CallOrder
    {
        get
        {
            lock (_gate)
            {
                return [.. _callOrder];
            }
        }
    }

    private void Record(string call)
    {
        lock (_gate)
        {
            _callOrder.Add(call);
        }
    }

    public FakeDeviceLatencyProbe Probe { get; }

    public FakePictureProbe Pictures { get; }

    /// <summary>What the session lists the neighbouring files' tracks with; the engine keeps its own decoder.</summary>
    public TrackListingDecoder Decoder { get; }

    public PlaybackSession Session { get; }

    public IReadOnlyList<SessionEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>Device n is "dev-n"; output n plays track ((n - 1) % 2) + 1.</summary>
    public StartRequest Request(int outputs) => new(
        "film.mkv",
        [.. Enumerable.Range(0, outputs).Select(i => new OutputAssignment((i % 2) + 1, DeviceRef.ByNumber(i + 1)))]);

    public void Dispose()
    {
        // The engine first: a session command stuck on the engine's fake time is released when the engine stops,
        // so the session's thread can end instead of the join below waiting for it forever.
        Engine.Dispose();
        Session.Dispose();
        _offsets.Dispose();
    }

    /// <summary>The engine as the session sees it; every transport command also tells the rig where the master clock stands.</summary>
    private sealed class FollowingEngine(SessionRig owner) : IAudioEngine
    {
        private EngineRig Rig => owner.Engine;

        public IObservable<EngineEvent> Events => Rig.Engine.Events;

        public Result<RunStarted> StartRun(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options)
        {
            owner.Record("engine.StartRun");
            lock (owner._gate)
            {
                owner._runFiles.Add(file);
                owner._startPositions.Add(options.StartPosition);
            }

            var result = Rig.Engine.StartRun(file, assignments, options);
            if (result.IsSuccess)
            {
                Rig.CommandDone(options.StartPosition, paused: false); // the new run's clock reads its start position from now on
            }

            owner.EngineCommandTook();

            return result;
        }

        public Result<TimeSpan> Pause()
        {
            Rig.Time.SignalWait(); // an engine command waits on the fake devices, so fake time may move
            var result = Rig.Engine.Pause();
            if (result.IsSuccess)
            {
                Rig.CommandDone(result.Value, paused: true);
            }

            return result;
        }

        public Result<TimeSpan> Resume()
        {
            Rig.Time.SignalWait(); // an engine command waits on the fake devices, so fake time may move
            var result = Rig.Engine.Resume();
            if (result.IsSuccess)
            {
                Rig.CommandDone(result.Value, paused: false);
            }

            owner.EngineCommandTook();

            return result;
        }

        public Result<SeekResult> Seek(TimeSpan target)
        {
            lock (owner._gate)
            {
                owner._seekTargets.Add(target);
            }

            Rig.CommandStarting(target < TimeSpan.Zero ? TimeSpan.Zero : target);
            Rig.Time.SignalWait(); // an engine command waits on the fake devices, so fake time may move
            var result = Rig.Engine.Seek(target);
            if (result.IsSuccess)
            {
                Rig.CommandDone(result.Value.Position, result.Value.Paused);
            }

            owner.EngineCommandTook();

            return result;
        }

        public void Stop(RunEndReason reason)
        {
            owner.Record($"engine.Stop({reason})");
            Rig.Time.SignalWait();
            owner.WaitFakeTime(owner.EngineStopTakes, "engine stop");
            Rig.Engine.Stop(reason);
        }
    }

    /// <summary>The lag control, with each output's position error replaced by the one <see cref="ForceOutputError"/> set.</summary>
    private sealed class ForcedErrorLags(SessionRig owner, IOutputLagControl inner) : IOutputLagControl
    {
        public long? FrameZeroTimestamp => inner.FrameZeroTimestamp;

        public long? ClockStartTimestamp => inner.ClockStartTimestamp;

        public bool IsRamping => inner.IsRamping;

        public Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode, TimeSpan moveDuration = default) => inner.SetLagTargets(targets, mode, moveDuration);

        public TimeSpan? CurrentLag(string deviceId) => inner.CurrentLag(deviceId);

        public IReadOnlyDictionary<string, OutputSpeed> AppliedSpeeds() => inner.AppliedSpeeds();

        public long? FirstAudioTimestamp(string deviceId) => inner.FirstAudioTimestamp(deviceId);

        public IReadOnlyDictionary<string, OutputPositionError> PositionErrors()
        {
            var readings = inner.PositionErrors();
            lock (owner._gate)
            {
                return readings.ToDictionary(
                    r => r.Key,
                    r => owner._forcedErrors.TryGetValue(r.Key, out var ms) ? r.Value with { ErrorMs = ms } : r.Value,
                    StringComparer.Ordinal);
            }
        }
    }

    private sealed class Collector(SessionRig rig) : IObserver<SessionEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(SessionEvent value)
        {
            lock (rig._gate)
            {
                rig._events.Add(value);
                if (value is PictureFailed)
                {
                    rig._callOrder.Add("event.PictureFailed");
                }
            }
        }
    }
}

/// <summary>Lists tracks per file name (unscripted files have audio tracks 1 and 2, a failed file returns its error); never decodes.</summary>
internal sealed class TrackListingDecoder : IAudioDecoder
{
    private readonly Dictionary<string, int[]> _tracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Error> _failures = new(StringComparer.Ordinal);

    public void Script(string fileName, params int[] trackNumbers) => _tracks[fileName] = trackNumbers;

    /// <summary>Makes <see cref="ListTracks"/> of <paramref name="fileName"/> fail the way the real decoder reports it.</summary>
    public void Fail(string fileName, string code, string message) => _failures[fileName] = new Error(code, message);

    /// <summary>When set, runs at the start of every <see cref="ListTracks"/> (a test can block a command there).</summary>
    public Action? BeforeList { get; set; }

    public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path)
    {
        BeforeList?.Invoke();
        if (_failures.TryGetValue(Path.GetFileName(path), out var failure))
        {
            return Result<IReadOnlyList<AudioTrackInfo>>.Failure(failure);
        }

        var numbers = _tracks.TryGetValue(Path.GetFileName(path), out var scripted) ? scripted : [1, 2];
        return Result<IReadOnlyList<AudioTrackInfo>>.Success([.. numbers.Select(n => new AudioTrackInfo(n, "und", ChannelLayout.Stereo, 48_000))]);
    }

    public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
        Result<IDecodeSession>.Failure("test.not_decoding", "The listing decoder does not decode.");
}

/// <summary>
/// What the neighbour-scan tests share about their rig. They assert on scan outcomes and fake time, not on the 100 ms sync samples,
/// so the engine's control loop (which polls in real time while a run starts) is waited for only every tenth rendered step.
/// </summary>
internal static class NeighbourRig
{
    public const int BarrierEvery = 10;
}
