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
    private readonly Lock _gate = new();
    private readonly OffsetBook _offsets;

    public SessionRig(RigDevice[] devices, DeviceOffsets? remembered = null, double totalSeconds = 600, Result<DeviceOffsets>? stored = null)
    {
        Engine = new EngineRig(devices, totalSeconds);
        Renderer = new FakeVideoRenderer(Engine.Time);
        Probe = new FakeDeviceLatencyProbe();
        var store = Substitute.For<IStateStore>();
        store.Load(DeviceOffsetsFile.Definition).Returns(stored ?? Result<DeviceOffsets>.Success(remembered ?? new DeviceOffsets()));
        store.Save(DeviceOffsetsFile.Definition, Arg.Any<DeviceOffsets>()).Returns(Result.Success());
        _offsets = new OffsetBook(store, Engine.Time, NullLogger<OffsetBook>.Instance);
        Session = new PlaybackSession(
            Engine.Engine,
            Engine.Engine,
            Renderer,
            Probe,
            _offsets,
            Engine.Backend,
            Engine.Time,
            NullLogger<PlaybackSession>.Instance);
        Session.Events.Subscribe(new Collector(this));
    }

    public EngineRig Engine { get; }

    public FakeVideoRenderer Renderer { get; }

    public FakeDeviceLatencyProbe Probe { get; }

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
        _offsets.Dispose();
        Engine.Dispose();
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
