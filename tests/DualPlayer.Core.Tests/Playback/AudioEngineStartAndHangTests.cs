using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// Start alignment with a device that reports its position late, and seeks while one device hangs (review F5, F7,
/// F20). The fake decoder's content frame n carries the value n / 1000; a device frame i is heard at
/// <c>StartLatency + i / 48000</c> seconds after the start, where a correct engine has the master clock at the same
/// content position.
/// </summary>
public sealed class AudioEngineStartAndHangTests
{
    private const int Rate = 48_000;
    private const double ToleranceSeconds = 0.010;
    private const double Epsilon = 1e-9;
    private static readonly MixFormat Stereo = new(Rate, 2, ChannelLayout.Stereo);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    private static readonly IReadOnlyList<AudioTrackInfo> Tracks =
    [
        new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, Rate),
        new AudioTrackInfo(2, "ukr", ChannelLayout.Stereo, Rate),
    ];

    private sealed class Rig : IDisposable
    {
        private readonly List<FakeAudioDevice> _devices = [];
        private readonly List<EngineEvent> _events = [];
        private readonly Lock _gate = new();
        private readonly DateTimeOffset _start;
        private readonly int _depth;
        private readonly int _chunk;

        public Rig(TimeSpan[] latencies, double ppm = 0, long totalFrames = (long)Rate * 600, int depth = 120, int chunk = 120)
        {
            _depth = depth;
            _chunk = chunk;
            Time = new FakeTimeProvider();
            var backend = new FakeAudioBackend(Time, Stereo) { PpmOffset = ppm };
            for (var i = 1; i <= latencies.Length; i++)
            {
                backend.AddDevice(new AudioDeviceInfo($"Device {i}", null, $"dev-{i}"));
                backend.SetStartLatency($"dev-{i}", latencies[i - 1]);
            }

            Engine = new AudioEngine(new CapturingBackend(backend, _devices), new FakeAudioDecoder(Tracks, totalFrames), Time, NullLogger<AudioEngine>.Instance);
            Engine.Events.Subscribe(new Collector(this));
            var assignments = latencies.Select((_, i) => new OutputAssignment((i % 2) + 1, DeviceRef.ByNumber(i + 1))).ToArray();
            Engine.StartRun("film.mkv", assignments, new EngineOptions()).IsSuccess.ShouldBeTrue();
            _start = Time.GetUtcNow();
            Fill();
        }

        public FakeTimeProvider Time { get; }

        public AudioEngine Engine { get; }

        public IReadOnlyList<FakeAudioDevice> Devices => _devices;

        public TimeSpan Elapsed => Time.GetUtcNow() - _start;

        public IReadOnlyList<SyncRecord> RecordsOf(string deviceId)
        {
            lock (_gate)
            {
                return [.. _events.OfType<SyncRecord>().Where(r => r.DeviceId == deviceId)];
            }
        }

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

        public void Render(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                Time.Advance(Step);
                Fill();
                Thread.Sleep(1);
            }
        }

        /// <summary>Runs a command on its own thread; fake time only advances while it has not finished by itself.</summary>
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
                Render(1);
            }

            task.IsCompleted.ShouldBeTrue("the command never completed");
            return task.GetAwaiter().GetResult();
        }

        public void Dispose() => Engine.Dispose();

        private void Fill()
        {
            foreach (var device in _devices)
            {
                if (device.IsClosed || device.IsHung)
                {
                    continue;
                }

                while (device.WrittenFrames < device.PlayedFramesNow + _depth)
                {
                    device.PumpRender(_chunk);
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

        private sealed class Collector(Rig rig) : IObserver<EngineEvent>
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

    private static double ContentSeconds(float sample) => Math.Round(sample * 1000.0) / Rate;

    /// <summary>
    /// The device is heard within 10 ms of the master clock for heard times in [from, to] seconds after
    /// <paramref name="commandAt"/>, where the clock read <paramref name="positionAtCommand"/> at that moment.
    /// </summary>
    private static void AssertInSync(FakeAudioDevice device, TimeSpan commandAt, TimeSpan positionAtCommand, double from, double to)
    {
        for (var t = commandAt.TotalSeconds + from; t <= commandAt.TotalSeconds + to; t += 0.05)
        {
            var index = (int)Math.Round((t - device.StartLatency.TotalSeconds) * Rate);
            var heard = ContentSeconds(device.Rendered[index]);
            var expected = positionAtCommand.TotalSeconds + (t - commandAt.TotalSeconds);
            Math.Abs(heard - expected).ShouldBeLessThanOrEqualTo(ToleranceSeconds + Epsilon, $"at {t:F2} s heard {heard:F3} s, master clock {expected:F3} s");
        }
    }

    [Fact]
    public void An_output_whose_position_starts_moving_fifty_ms_late_is_within_ten_ms_of_the_clock_one_second_after_start()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.FromMilliseconds(50)]);

        rig.Render(160);

        foreach (var device in rig.Devices)
        {
            AssertInSync(device, TimeSpan.Zero, TimeSpan.Zero, 1.0, 1.5);
        }
    }

    [Fact]
    public void A_seek_with_one_hung_output_is_confirmed_on_the_rendering_output_long_before_the_ten_second_timeout()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.Zero]);
        rig.Render(100);
        rig.Devices[1].Hang();
        rig.Render(300); // well past the one second after which an output counts as stalled
        var commandAt = rig.Elapsed; // the master clock jumps to the target when the command starts, not when it returns

        var seek = rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(120)));
        var took = rig.Elapsed - commandAt;

        seek.IsSuccess.ShouldBeTrue(seek.Error?.Message);
        took.ShouldBeLessThan(TimeSpan.FromSeconds(5), "the control loop waited for the hung output");
        rig.Events.OfType<RunSummary>().ShouldBeEmpty();
        rig.Events.OfType<DeviceLost>().ShouldBeEmpty();
        rig.Render(170);
        AssertInSync(rig.Devices[0], commandAt, TimeSpan.FromSeconds(120), 1.0, 1.5);
    }

    [Fact]
    public void After_a_seek_with_one_hung_output_the_surviving_output_keeps_its_rate_correction()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.Zero], ppm: 300);
        rig.Render(100);
        rig.Devices[1].Hang();
        rig.Render(300);
        rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(120))).IsSuccess.ShouldBeTrue("the seek must not time out on the hung output");

        rig.Render(1_500);

        var last = rig.RecordsOf("dev-1")[^1];
        last.CorrectionPpm.ShouldBeGreaterThan(100, "the rate correction follows the +300 ppm clock");
        Math.Abs(last.DriftMs).ShouldBeLessThanOrEqualTo(10);
    }

    [Fact]
    public void A_seek_after_the_decoder_reached_the_end_plays_the_target_frame_first_with_no_frames_swallowed()
    {
        // Guard for F20 (BeginEpoch before Pump.Seek): the race window is microseconds wide, so this cannot be forced
        // deterministically at engine level; it pins the observable contract the fix must keep.
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.Zero], totalFrames: (long)Rate * 60);
        rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(58.5))).IsSuccess.ShouldBeTrue();
        rig.Render(50);
        var marks = rig.Devices.Select(d => d.Rendered.Count).ToArray();

        rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(30))).IsSuccess.ShouldBeTrue();
        rig.Render(20);

        for (var d = 0; d < rig.Devices.Count; d++)
        {
            var heard = rig.Devices[d].Rendered.Skip(marks[d]).First(s => s != 0f && s < 2_000f);
            Math.Round(heard * 1000.0).ShouldBe(30.0 * Rate, $"device {d + 1} first new-epoch content frame");
        }
    }
}
