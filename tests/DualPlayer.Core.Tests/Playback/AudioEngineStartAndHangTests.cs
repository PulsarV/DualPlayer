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
        private readonly bool _pullsDuringStartLatency;

        // 1 ms per hook: the release reads the frequency several times, and a real render thread queues far less than
        // StartMargin in that time — a whole chunk per read would push the queue past the margin on its own.
        private const int HookFrames = Rate / 1_000;

        private readonly Lock _deviceGate = new();
        private bool _inHook;

        /// <param name="rendersWhenTheEngineReadsTheTimestampFrequency">
        /// Every output renders 1 ms each time the engine's control loop reads the timestamp frequency before the clock has
        /// started: the release reads it once per output between resetting the start targets and handing out the film, so a
        /// render thread that runs there (a deterministic stand-in for that race) is part of the scenario.
        /// </param>
        /// <param name="pullsDuringStartLatency">
        /// The device takes audio at its rate from the start while its position still reads 0, as a Bluetooth headset
        /// does (measured on the target PC: position 0 for ~0.6–1 s, then <c>elapsed − position</c> stays constant).
        /// </param>
        public Rig(TimeSpan[] latencies, double ppm = 0, long totalFrames = (long)Rate * 600, int depth = 120, int chunk = 120, bool pullsDuringStartLatency = false, bool rendersWhenTheEngineReadsTheTimestampFrequency = false)
        {
            _depth = depth;
            _chunk = chunk;
            _pullsDuringStartLatency = pullsDuringStartLatency;
            Time = rendersWhenTheEngineReadsTheTimestampFrequency ? new RenderingTime(this) : new FakeTimeProvider();
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

        /// <summary>When the master clock started at 00:00, after the start command (read from the engine's AudioStarted).</summary>
        public TimeSpan ClockStart => Events.OfType<AudioStarted>().Single().StartToAudio;

        /// <summary>Renders until the master clock has started; fails when it does not within <paramref name="steps"/> steps.</summary>
        public void RenderUntilClockStarts(int steps = 700)
        {
            for (var i = 0; i < steps && !Events.OfType<AudioStarted>().Any(); i++)
            {
                Render(1);
            }

            Events.OfType<AudioStarted>().ShouldNotBeEmpty("the master clock never started");
        }

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

        private void RenderOnEngineThread()
        {
            // Only the control loop runs the release; other threads (the decode pump, the fake clock's own timers) read the
            // frequency too, and blocking them here could deadlock against Fill. Never wait for the gate: skip when busy.
            if (_inHook || Thread.CurrentThread.Name != "EngineControlLoop" || Events.OfType<AudioStarted>().Any() || !_deviceGate.TryEnter())
            {
                return;
            }

            _inHook = true;
            try
            {
                foreach (var device in _devices.ToArray())
                {
                    device.PumpRender(HookFrames);
                }
            }
            finally
            {
                _inHook = false;
                _deviceGate.Exit();
            }
        }

        private void Fill()
        {
            lock (_deviceGate)
            {
                FillLocked();
            }
        }

        private void FillLocked()
        {
            foreach (var device in _devices)
            {
                if (device.IsClosed || device.IsHung || device.IsLost)
                {
                    continue;
                }

                var target = _pullsDuringStartLatency
                    ? device.PlayedFramesNow + (long)(device.StartLatency.TotalSeconds * Rate) + _depth
                    : device.PlayedFramesNow + _depth;
                if (_pullsDuringStartLatency && device.PlayedFramesNow == 0)
                {
                    target = (long)(Elapsed.TotalSeconds * Rate) + _depth; // still silent: the device takes audio at its rate
                }

                while (device.WrittenFrames < target)
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

        private sealed class RenderingTime(Rig rig) : FakeTimeProvider
        {
            public override long TimestampFrequency
            {
                get
                {
                    rig.RenderOnEngineThread();
                    return base.TimestampFrequency;
                }
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

        rig.RenderUntilClockStarts();
        rig.Render(160);

        foreach (var device in rig.Devices)
        {
            AssertInSync(device, rig.ClockStart, TimeSpan.Zero, 1.0, 1.5);
        }
    }

    /// <summary>No film is heard on <paramref name="device"/> before <paramref name="clockStart"/>: its first content frame is heard then.</summary>
    private static void AssertFilmFirstHeardAt(FakeAudioDevice device, TimeSpan clockStart)
    {
        var first = -1;
        for (var i = 0; i < device.Rendered.Count && first < 0; i++)
        {
            first = device.Rendered[i] != 0f ? i : -1;
        }

        first.ShouldBeGreaterThan(0, "the device never heard film");
        var heardAt = device.StartLatency.TotalSeconds + ((first - 1.0) / Rate); // content frame 0 carries 0, so frame 1 marks the film
        Math.Abs(heardAt - clockStart.TotalSeconds).ShouldBeLessThanOrEqualTo(ToleranceSeconds, $"film frame 0 heard at {heardAt:F3} s, clock started at {clockStart.TotalSeconds:F3} s");
    }

    private static RunSummary StopAndSummarize(Rig rig)
    {
        rig.Call(() =>
        {
            rig.Engine.Stop(RunEndReason.Stop);
            return 0;
        });
        return rig.Events.OfType<RunSummary>().Single();
    }

    private static Rig StartingRig() =>
        new([TimeSpan.Zero, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(980)], depth: 1_920, chunk: 480, pullsDuringStartLatency: true);

    private static int FilmSamples(FakeAudioDevice device) => device.Rendered.Count(s => s != 0f);

    private static int[] FilmSamples(Rig rig) => [.. rig.Devices.Select(FilmSamples)];

    /// <summary>Renders until the film has been handed to some device (the release happened) while the master clock has not started.</summary>
    private static void RenderUntilReleasedButClockNotStarted(Rig rig)
    {
        for (var i = 0; i < 700 && rig.Devices.All(d => FilmSamples(d) == 0); i++)
        {
            rig.Events.OfType<AudioStarted>().ShouldBeEmpty("the clock started before the film was released");
            rig.Render(1);
        }

        rig.Devices.Any(d => FilmSamples(d) > 0).ShouldBeTrue("the film was never released");
        rig.Events.OfType<AudioStarted>().ShouldBeEmpty("the clock already started: the test missed the window between release and clock start");
    }

    private static void LoseAll(Rig rig)
    {
        rig.Call(() =>
        {
            foreach (var device in rig.Devices)
            {
                device.SimulateLoss();
            }

            return 0;
        });
        rig.Render(30);
    }

    [Fact]
    public void A_stop_before_the_film_is_released_produces_a_summary_and_no_output_ever_hears_film()
    {
        using var rig = StartingRig();
        rig.Render(30);
        rig.Events.OfType<AudioStarted>().ShouldBeEmpty();
        FilmSamples(rig).ShouldAllBe(n => n == 0, "the film must not be released yet");

        var summary = StopAndSummarize(rig);
        rig.Render(50);

        summary.EndReason.ShouldBe(RunEndReason.Stop);
        FilmSamples(rig).ShouldAllBe(n => n == 0, "film was heard after a stop before the release");
        rig.Events.OfType<AudioStarted>().ShouldBeEmpty();
    }

    [Fact]
    public void A_stop_after_the_release_but_before_the_clock_starts_produces_a_summary_and_no_output_renders_film_after_it()
    {
        using var rig = StartingRig();
        RenderUntilReleasedButClockNotStarted(rig);
        var atStop = FilmSamples(rig);

        var summary = StopAndSummarize(rig);
        rig.Render(50);

        summary.EndReason.ShouldBe(RunEndReason.Stop);
        FilmSamples(rig).ShouldBe(atStop, "film was rendered after the stop");
        rig.Events.OfType<AudioStarted>().ShouldBeEmpty("the clock must not start after a stop");
    }

    [Fact]
    public void Losing_the_last_devices_before_the_film_is_released_ends_the_run_with_all_devices_lost_and_no_film_plays()
    {
        using var rig = StartingRig();
        rig.Render(30);
        FilmSamples(rig).ShouldAllBe(n => n == 0, "the film must not be released yet");

        LoseAll(rig);

        var summary = rig.Events.OfType<RunSummary>().Single();
        summary.EndReason.ShouldBe(RunEndReason.AllDevicesLost);
        FilmSamples(rig).ShouldAllBe(n => n == 0, "film was heard after the loss");
    }

    [Fact]
    public void Losing_the_last_devices_after_the_release_but_before_the_clock_starts_ends_the_run_with_all_devices_lost_and_no_film_plays_after_it()
    {
        using var rig = StartingRig();
        RenderUntilReleasedButClockNotStarted(rig);
        var atLoss = FilmSamples(rig);

        LoseAll(rig);

        var summary = rig.Events.OfType<RunSummary>().Single();
        summary.EndReason.ShouldBe(RunEndReason.AllDevicesLost);
        FilmSamples(rig).ShouldBe(atLoss, "film was rendered after the loss");
        rig.Events.OfType<AudioStarted>().ShouldBeEmpty();
    }

    [Fact]
    public void A_bluetooth_output_that_takes_audio_while_its_position_reads_zero_for_a_second_is_aligned_once_it_plays()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.FromMilliseconds(980)], depth: 1_920, chunk: 480, pullsDuringStartLatency: true);

        rig.RenderUntilClockStarts();
        rig.Render(320);

        var clockStart = rig.ClockStart;
        foreach (var device in rig.Devices)
        {
            AssertFilmFirstHeardAt(device, clockStart);
            AssertInSync(device, clockStart, TimeSpan.Zero, 1.0, 2.9);
        }

        StopAndSummarize(rig).Outputs.ShouldAllBe(o => o.Corrections == 0, "no one-step correction at start");
    }

    [Fact]
    public void Three_outputs_with_start_lags_of_zero_six_hundred_and_nine_hundred_eighty_ms_hear_film_frame_zero_together()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(980)], depth: 1_920, chunk: 480, pullsDuringStartLatency: true);

        rig.RenderUntilClockStarts();
        rig.Render(320);

        var clockStart = rig.ClockStart;
        clockStart.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(3), "start command to audio on every output");
        foreach (var device in rig.Devices)
        {
            AssertFilmFirstHeardAt(device, clockStart);
            AssertInSync(device, clockStart, TimeSpan.Zero, 1.0, 2.9);
        }

        StopAndSummarize(rig).Outputs.ShouldAllBe(o => o.Corrections == 0, "no one-step correction at start");
    }

    [Fact]
    public void An_output_that_renders_while_the_film_is_being_released_still_hears_film_frame_zero_at_the_clock_start()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(980)], depth: 1_920, chunk: 480, pullsDuringStartLatency: true, rendersWhenTheEngineReadsTheTimestampFrequency: true);

        rig.RenderUntilClockStarts();
        rig.Render(320);

        var clockStart = rig.ClockStart;
        foreach (var device in rig.Devices)
        {
            AssertFilmFirstHeardAt(device, clockStart);
        }
    }

    [Fact]
    public void An_output_whose_position_never_moves_does_not_hold_the_clock_past_the_first_audio_deadline()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.FromHours(1)], depth: 1_920, chunk: 480, pullsDuringStartLatency: true);

        rig.RenderUntilClockStarts();
        rig.Render(200);

        rig.ClockStart.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(5.1), "the never-moving output held the clock");
        rig.ClockStart.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4.9), "it is awaited until the first-audio deadline");
        AssertInSync(rig.Devices[0], rig.ClockStart, TimeSpan.Zero, 1.0, 1.9);
    }

    [Fact]
    public void A_pause_during_the_pre_roll_waits_for_the_start_and_then_plays_no_film_past_its_instant()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.FromMilliseconds(980)], depth: 1_920, chunk: 480, pullsDuringStartLatency: true);

        var paused = rig.Call(rig.Engine.Pause);
        rig.Render(300);

        paused.IsSuccess.ShouldBeTrue(paused.Error?.Message);
        rig.Events.OfType<AudioStarted>().ShouldNotBeEmpty("the pause waited for the start");
        foreach (var device in rig.Devices)
        {
            var furthest = device.Rendered.Max(ContentSeconds);
            furthest.ShouldBeLessThanOrEqualTo(paused.Value.TotalSeconds + ToleranceSeconds, "film was played past the pause instant");
        }
    }

    [Fact]
    public void A_seek_during_the_pre_roll_waits_for_the_start_and_then_plays_the_target_on_every_output_together()
    {
        using var rig = new Rig([TimeSpan.Zero, TimeSpan.FromMilliseconds(980)], depth: 1_920, chunk: 480, pullsDuringStartLatency: true);

        var seek = rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(60)));
        rig.Render(300);

        seek.IsSuccess.ShouldBeTrue(seek.Error?.Message);
        rig.Events.OfType<AudioStarted>().ShouldNotBeEmpty("the seek waited for the start");
        for (var t = rig.Elapsed.TotalSeconds - 1.0; t <= rig.Elapsed.TotalSeconds - 0.1; t += 0.05)
        {
            var heard = rig.Devices.Select(d => ContentSeconds(d.Rendered[(int)Math.Round((t - d.StartLatency.TotalSeconds) * Rate)])).ToArray();
            heard.Min().ShouldBeGreaterThanOrEqualTo(60.0, $"at {t:F2} s an output still plays from before the seek");
            (heard.Max() - heard.Min()).ShouldBeLessThanOrEqualTo(ToleranceSeconds + Epsilon, $"at {t:F2} s the outputs hear {string.Join(", ", heard.Select(h => h.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
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
