using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>One fake output device: its sample rate, how fast its clock runs and how late its position starts moving.</summary>
internal readonly record struct RigDevice(int Rate = 48_000, double Ppm = 0, TimeSpan StartLatency = default);

/// <summary>
/// Drives a real <see cref="AudioEngine"/> on a <see cref="FakeTimeProvider"/> with realistic fakes: devices of different
/// rates, clock speeds and start latencies, a decoder with mismatched block sizes and early-landing seeks. Fake time moves
/// in 10 ms steps; after each step the devices are asked for audio until they hold a little more than they have played,
/// the decoder is waited for until it is a second ahead (so a slow test machine cannot cause an underrun the real engine
/// would not have) and, every <c>barrierEvery</c> steps, the engine-control loop is waited for until it has handled
/// everything queued (so the 100 ms sync samples are taken at the instant the timer fired). Device n is "dev-n" and
/// plays track ((n - 1) % 2) + 1.
/// </summary>
internal sealed class EngineRig : IDisposable
{
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    private static readonly IReadOnlyList<AudioTrackInfo> Tracks =
    [
        new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, 48_000),
        new AudioTrackInfo(2, "ukr", ChannelLayout.Stereo, 48_000),
    ];

    private readonly List<FakeAudioDevice> _devices = [];
    private readonly List<EngineEvent> _events = [];
    private readonly Lock _gate = new();
    private readonly RigDevice[] _specs;
    private readonly RealisticAudioDecoder _decoder;
    private readonly DateTimeOffset _origin;
    private readonly int _barrierEvery;
    private readonly List<(TimeSpan At, long[] Played)> _observations = [];
    private readonly bool _observe;
    private TimeSpan _positionBase;
    private TimeSpan _positionSince;
    private bool _paused;
    private int _steps;

    public EngineRig(
        RigDevice[] devices,
        double totalSeconds = 600,
        int[]? blockFrames = null,
        TimeSpan[]? seekLandsEarly = null,
        TimeSpan[]? seekLandsLate = null,
        TimeSpan[]? startLandsLate = null,
        bool keepRendered = true,
        bool observe = false,
        int barrierEvery = 1)
    {
        _specs = devices;
        _observe = observe;
        _barrierEvery = barrierEvery;
        Time = new FakeTimeProvider();
        var backend = new FakeAudioBackend(Time, new MixFormat(48_000, 2, ChannelLayout.Stereo)) { KeepRendered = keepRendered };
        for (var i = 0; i < devices.Length; i++)
        {
            var id = $"dev-{i + 1}";
            backend.AddDevice(new AudioDeviceInfo($"Device {i + 1}", null, id));
            backend.SetFormat(id, new MixFormat(devices[i].Rate, 2, ChannelLayout.Stereo));
            backend.SetPpm(id, devices[i].Ppm);
            backend.SetStartLatency(id, devices[i].StartLatency);
        }

        _decoder = new RealisticAudioDecoder(Tracks, totalSeconds, blockFrames ?? [1536, 1024], seekLandsEarly, seekLandsLate, startLandsLate);
        Engine = new AudioEngine(new CapturingBackend(backend, _devices), _decoder, Time, NullLogger<AudioEngine>.Instance);
        Engine.Events.Subscribe(new Collector(this));
        _origin = Time.GetUtcNow();
    }

    public FakeTimeProvider Time { get; }

    public AudioEngine Engine { get; }

    public IReadOnlyList<FakeAudioDevice> Devices => _devices;

    public TimeSpan Elapsed => Time.GetUtcNow() - _origin;

    /// <summary>Where the master clock should read now, as the test knows it from the commands it gave.</summary>
    public TimeSpan Position => _paused ? _positionBase : _positionBase + (Elapsed - _positionSince);

    public IReadOnlyList<EngineEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<SyncRecord> Records => [.. Events.OfType<SyncRecord>()];

    public IReadOnlyList<SyncRecord> RecordsOf(string deviceId) => [.. Records.Where(r => r.DeviceId == deviceId)];

    public RunSummary? Summary => Events.OfType<RunSummary>().FirstOrDefault();

    public void Start()
    {
        var assignments = Enumerable.Range(0, _specs.Length)
            .Select(i => new OutputAssignment((i % 2) + 1, DeviceRef.ByNumber(i + 1)))
            .ToArray();
        Engine.StartRun("film.mkv", assignments, new EngineOptions()).IsSuccess.ShouldBeTrue();
        _positionBase = TimeSpan.Zero;
        _positionSince = Elapsed;
        Fill();
    }

    /// <summary>Advances fake time by <paramref name="steps"/> x 10 ms.</summary>
    public void Render(int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            Advance(barrier: true);
        }
    }

    /// <summary>Runs <paramref name="command"/> on its own thread while fake time moves on until it has finished.</summary>
    public T Call<T>(Func<T> command)
    {
        using var started = new ManualResetEventSlim();
        var task = Task.Factory.StartNew(
            () =>
            {
                started.Set();
                return command();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        started.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the command thread never started");
        task.Wait(200);
        for (var i = 0; i < 3_000 && !task.IsCompleted; i++)
        {
            Advance(barrier: false);
            Thread.Sleep(1);
        }

        task.IsCompleted.ShouldBeTrue("the command never completed");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>Seeks and tells the rig where the master clock now is (it jumps when the command starts).</summary>
    public SeekResult Seek(TimeSpan target)
    {
        _positionBase = target;
        _positionSince = Elapsed;
        _paused = false;
        var result = Call(() => Engine.Seek(target));
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value;
    }

    public void Pause()
    {
        var result = Call(Engine.Pause);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        _positionBase = result.Value;
        _paused = true;
    }

    public void Resume()
    {
        var result = Call(Engine.Resume);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        _positionBase = result.Value;
        _positionSince = Elapsed;
        _paused = false;
    }

    public void Stop(RunEndReason reason)
    {
        Call(() =>
        {
            Engine.Stop(reason);
            return 0;
        });
    }

    /// <summary>Waits until the engine has handled everything queued so far (a start while running is refused on the loop).</summary>
    public void Barrier()
    {
        var refused = Engine.StartRun(string.Empty, [], new EngineOptions());
        refused.IsSuccess.ShouldBeFalse($"the run is no longer active; it ended with {Summary?.EndReason.ToString() ?? "no summary"} at {Summary?.ClockPosition}");
        refused.Error.Code.ShouldBe("playback.run_active");
    }

    public bool WaitFor(Func<bool> condition, int milliseconds = 10_000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(2);
        }

        return condition();
    }

    /// <summary>
    /// The largest distance, in ms, between what the device played and the master clock, over every observed step whose time
    /// lies in [<paramref name="from"/>, <paramref name="to"/>] after <paramref name="commandAt"/> (the master clock read
    /// <paramref name="positionAtCommand"/> then). Needs <c>observe</c>; silence where audio belongs counts as a huge error.
    /// </summary>
    public double MaxDriftMs(int deviceIndex, TimeSpan commandAt, TimeSpan positionAtCommand, TimeSpan from, TimeSpan to)
    {
        var device = _devices[deviceIndex];
        var rate = _specs[deviceIndex].Rate;
        var max = 0.0;
        var seen = 0;
        foreach (var (at, played) in _observations)
        {
            if (at < commandAt + from || at > commandAt + to)
            {
                continue;
            }

            seen++;
            var frame = played[deviceIndex];
            var heard = frame < device.Rendered.Count ? Math.Round(device.Rendered[(int)frame] * 1000.0) / rate : double.NaN;
            var expected = (positionAtCommand + (at - commandAt)).TotalSeconds;
            max = Math.Max(max, double.IsNaN(heard) ? double.PositiveInfinity : Math.Abs(heard - expected) * 1000);
        }

        seen.ShouldBeGreaterThan(0, "no observations in the window");
        return max;
    }

    public void Dispose() => Engine.Dispose();

    private void Advance(bool barrier)
    {
        Time.Advance(Step);
        Fill();
        if (barrier)
        {
            WaitForDecoder();
        }

        if (_observe)
        {
            _observations.Add((Elapsed, [.. _devices.Select(d => d.PlayedFramesNow)]));
        }

        if (barrier && ++_steps % _barrierEvery == 0)
        {
            Barrier();
        }
    }

    private void Fill()
    {
        foreach (var device in _devices)
        {
            if (device.IsClosed || device.IsHung || device.IsLost)
            {
                continue;
            }

            var rate = device.MixFormat.SampleRate;
            var chunk = rate / 100;
            while (device.WrittenFrames < device.PlayedFramesNow + (2 * chunk))
            {
                device.PumpRender(chunk);
            }
        }
    }

    private void WaitForDecoder()
    {
        var wanted = Math.Min(Position.TotalSeconds + 1.0, _decoder.TotalSeconds);
        for (var i = 0; i < _devices.Count; i++)
        {
            if (_devices[i].IsClosed || _devices[i].IsLost || _devices[i].IsHung)
            {
                continue;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (_decoder.DecodedSeconds(i) < wanted && DateTime.UtcNow < deadline)
            {
                Thread.Yield();
            }
        }
    }

    private sealed class CapturingBackend(IAudioBackend inner, List<FakeAudioDevice> devices) : IAudioBackend
    {
        public IReadOnlyList<AudioDeviceInfo> ListDevices() => inner.ListDevices();

        public Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source)
        {
            var result = inner.Open(deviceId, requestedBuffer, source);
            if (result.IsSuccess)
            {
                devices.Add((FakeAudioDevice)result.Value);
            }

            return result;
        }
    }

    private sealed class Collector(EngineRig rig) : IObserver<EngineEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(EngineEvent value)
        {
            lock (rig._gate)
            {
                rig._events.Add(value);
            }
        }
    }
}
