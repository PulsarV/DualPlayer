using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Settings;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>A <see cref="PlaybackSession"/> over the engine rig, a fake renderer, a fake latency probe and a stubbed offsets file.</summary>
internal sealed class SessionRig : IDisposable
{
    private readonly List<SessionEvent> _events = [];
    private readonly List<string> _runFiles = [];
    private readonly List<TimeSpan> _seekTargets = [];
    private readonly Lock _gate = new();
    private readonly OffsetBook _offsets;

    public SessionRig(
        RigDevice[] devices,
        DeviceOffsets? remembered = null,
        double totalSeconds = 600,
        Result<DeviceOffsets>? stored = null,
        Func<IOutputLagControl, IOutputLagControl>? wrapLags = null)
    {
        Engine = new EngineRig(devices, totalSeconds) { EndsAllowed = true };
        Renderer = new FakeVideoRenderer(Engine.Time);
        Probe = new FakeDeviceLatencyProbe();
        Pictures = new FakePictureProbe();
        Decoder = new TrackListingDecoder();
        var store = Substitute.For<IStateStore>();
        store.Load(DeviceOffsetsFile.Definition).Returns(stored ?? Result<DeviceOffsets>.Success(remembered ?? new DeviceOffsets()));
        store.Save(DeviceOffsetsFile.Definition, Arg.Any<DeviceOffsets>()).Returns(Result.Success());
        _offsets = new OffsetBook(store, Engine.Time, NullLogger<OffsetBook>.Instance);
        Session = new PlaybackSession(
            new FollowingEngine(this),
            wrapLags is null ? Engine.Engine : wrapLags(Engine.Engine),
            Renderer,
            Decoder,
            Pictures,
            Probe,
            _offsets,
            Engine.Backend,
            Engine.Time,
            NullLogger<PlaybackSession>.Instance);
        Session.Events.Subscribe(new Collector(this));
        Engine.AfterStep = Session.Flush;
    }

    /// <summary>Waits until the session has handled every command, timer and event posted so far.</summary>
    public void Flush() => Session.Flush();

    public EngineRig Engine { get; }

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
        Session.Dispose();
        _offsets.Dispose();
        Engine.Dispose();
    }

    /// <summary>The engine as the session sees it; every transport command also tells the rig where the master clock stands.</summary>
    private sealed class FollowingEngine(SessionRig owner) : IAudioEngine
    {
        private EngineRig Rig => owner.Engine;

        public IObservable<EngineEvent> Events => Rig.Engine.Events;

        public Result<RunStarted> StartRun(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options)
        {
            lock (owner._gate)
            {
                owner._runFiles.Add(file);
            }

            return Rig.Engine.StartRun(file, assignments, options);
        }

        public Result<TimeSpan> Pause()
        {
            var result = Rig.Engine.Pause();
            if (result.IsSuccess)
            {
                Rig.CommandDone(result.Value, paused: true);
            }

            return result;
        }

        public Result<TimeSpan> Resume()
        {
            var result = Rig.Engine.Resume();
            if (result.IsSuccess)
            {
                Rig.CommandDone(result.Value, paused: false);
            }

            return result;
        }

        public Result<SeekResult> Seek(TimeSpan target)
        {
            lock (owner._gate)
            {
                owner._seekTargets.Add(target);
            }

            Rig.CommandStarting(target < TimeSpan.Zero ? TimeSpan.Zero : target);
            var result = Rig.Engine.Seek(target);
            if (result.IsSuccess)
            {
                Rig.CommandDone(result.Value.Position, result.Value.Paused);
            }

            return result;
        }

        public void Stop(RunEndReason reason) => Rig.Engine.Stop(reason);
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
            }
        }
    }
}

/// <summary>Lists tracks per file name (unscripted files have audio tracks 1 and 2); never decodes.</summary>
internal sealed class TrackListingDecoder : IAudioDecoder
{
    private readonly Dictionary<string, int[]> _tracks = new(StringComparer.Ordinal);

    public void Script(string fileName, params int[] trackNumbers) => _tracks[fileName] = trackNumbers;

    /// <summary>When set, runs at the start of every <see cref="ListTracks"/> (a test can block a command there).</summary>
    public Action? BeforeList { get; set; }

    public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path)
    {
        BeforeList?.Invoke();
        var numbers = _tracks.TryGetValue(Path.GetFileName(path), out var scripted) ? scripted : [1, 2];
        return Result<IReadOnlyList<AudioTrackInfo>>.Success([.. numbers.Select(n => new AudioTrackInfo(n, "und", ChannelLayout.Stereo, 48_000))]);
    }

    public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
        Result<IDecodeSession>.Failure("test.not_decoding", "The listing decoder does not decode.");
}
