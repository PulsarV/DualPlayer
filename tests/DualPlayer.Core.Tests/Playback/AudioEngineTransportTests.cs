using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// Pause, resume and seek across two fake devices. The fake decoder's content frame n carries the value n / 1000,
/// so the content heard at any device frame can be read back from what the device was handed. A device frame i is
/// heard when the device's reported position reaches i, i.e. i / 48000 seconds after the start; a correct engine
/// therefore has content heard at time t equal to the master-clock position at t.
/// </summary>
public sealed class AudioEngineTransportTests
{
    private const int Rate = 48_000;
    private const double ToleranceSeconds = 0.010;

    /// <summary>Floating-point slack on the 10 ms boundary only (content and clock are computed in doubles).</summary>
    private const double Epsilon = 1e-9;
    private static readonly MixFormat Stereo = new(Rate, 2, ChannelLayout.Stereo);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    private static readonly IReadOnlyList<AudioTrackInfo> Tracks =
    [
        new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, Rate),
        new AudioTrackInfo(2, "ukr", ChannelLayout.Stereo, Rate),
    ];

    /// <summary>Two devices; each keeps <c>depth</c> frames queued ahead of its reported position, written in <c>chunk</c> frames.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly List<FakeAudioDevice> _devices = [];
        private readonly List<EngineEvent> _events = [];
        private readonly Lock _gate = new();
        private readonly (int Depth, int Chunk)[] _queues;
        private readonly DateTimeOffset _start;

        public Rig((int Depth, int Chunk)[] queues, long totalFrames = (long)Rate * 600)
        {
            _queues = queues;
            Time = new FakeTimeProvider();
            var backend = new FakeAudioBackend(Time, Stereo);
            for (var i = 1; i <= queues.Length; i++)
            {
                backend.AddDevice(new AudioDeviceInfo($"Device {i}", null, $"dev-{i}"));
            }

            Engine = new AudioEngine(new CapturingBackend(backend, _devices), new FakeAudioDecoder(Tracks, totalFrames), Time, NullLogger<AudioEngine>.Instance);
            Engine.Events.Subscribe(new Collector(this));
            var assignments = queues.Select((_, i) => new OutputAssignment(i + 1, DeviceRef.ByNumber(i + 1))).ToArray();
            Engine.StartRun("film.mkv", assignments, new EngineOptions()).IsSuccess.ShouldBeTrue();
            _start = Time.GetUtcNow();
            Fill();
        }

        public FakeTimeProvider Time { get; }

        public AudioEngine Engine { get; }

        public IReadOnlyList<FakeAudioDevice> Devices => _devices;

        public TimeSpan Elapsed => Time.GetUtcNow() - _start;

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

        /// <summary>Advances 10 ms per step and keeps every device's queue topped up.</summary>
        public void Render(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                Time.Advance(Step);
                Fill();
                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// Runs a command on its own thread; time only advances if it does not finish by itself. A dedicated thread
        /// (not the thread pool, which can be starved by parallel tests) and the start handshake keep fake time
        /// still until the engine has had the command.
        /// </summary>
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
            for (var d = 0; d < _devices.Count; d++)
            {
                var (depth, chunk) = _queues[d];
                if (_devices[d].IsClosed)
                {
                    continue; // a closed device no longer renders; topping it up would never finish
                }

                RigLoops.FillQueue(_devices[d], depth, chunk);
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

    private static Rig SmallQueues() => new([(120, 120), (120, 120)]);

    private static double ContentSeconds(float sample) => Math.Round(sample * 1000.0) / Rate;

    /// <summary>Index one past the last audible frame (non-zero sample) the device was handed.</summary>
    private static long SilentFrom(FakeAudioDevice device)
    {
        for (var i = device.Rendered.Count - 1; i >= 0; i--)
        {
            if (device.Rendered[i] != 0f)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Every output must be heard within 10 ms of the master clock for heard times in
    /// [<paramref name="fromSeconds"/>, <paramref name="toSeconds"/>] after the command issued at <paramref name="commandAt"/>,
    /// where the clock read <paramref name="positionAtCommand"/> at that moment.
    /// </summary>
    private static void AssertInSync(Rig rig, TimeSpan commandAt, TimeSpan positionAtCommand, double fromSeconds, double toSeconds)
    {
        foreach (var device in rig.Devices)
        {
            for (var t = commandAt.TotalSeconds + fromSeconds; t <= commandAt.TotalSeconds + toSeconds; t += 0.05)
            {
                var index = (int)Math.Round(t * Rate);
                var heard = ContentSeconds(device.Rendered[index]);
                var expected = positionAtCommand.TotalSeconds + (t - commandAt.TotalSeconds);
                Math.Abs(heard - expected).ShouldBeLessThanOrEqualTo(ToleranceSeconds + Epsilon, $"at {t:F2} s heard {heard:F3} s, master clock {expected:F3} s");
            }
        }
    }

    [Fact]
    public void Pause_silences_devices_with_different_queue_depths_within_ten_ms_of_each_other_and_stops_the_clock()
    {
        using var rig = new Rig([(480, 480), (1_920, 480)]);
        rig.Render(100);

        var paused = rig.Call(rig.Engine.Pause);
        rig.Render(100);

        paused.IsSuccess.ShouldBeTrue(paused.Error?.Message);
        var first = SilentFrom(rig.Devices[0]);
        var second = SilentFrom(rig.Devices[1]);
        (Math.Abs(first - second) / (double)Rate).ShouldBeLessThanOrEqualTo(ToleranceSeconds, $"silent from device frame {first} vs {second}");
        foreach (var device in rig.Devices)
        {
            var lastAudible = ContentSeconds(device.Rendered[(int)SilentFrom(device) - 1]);
            lastAudible.ShouldBeInRange(paused.Value.TotalSeconds - ToleranceSeconds, paused.Value.TotalSeconds + 0.001);
        }

        // The clock stood still while everything was silent for a second.
        var resumed = rig.Call(rig.Engine.Resume);
        resumed.IsSuccess.ShouldBeTrue(resumed.Error?.Message);
        resumed.Value.TotalSeconds.ShouldBe(paused.Value.TotalSeconds, 0.001);
    }

    [Fact]
    public void Stop_silences_devices_with_different_queue_depths_within_ten_ms_of_each_other_and_closes_them_only_after_every_device_passed_the_stop_instant()
    {
        using var rig = new Rig([(480, 480), (1_920, 480)]);
        rig.Render(100);

        rig.Call(() =>
        {
            rig.Engine.Stop(RunEndReason.Stop);
            return 0;
        });

        var first = SilentFrom(rig.Devices[0]);
        var second = SilentFrom(rig.Devices[1]);
        (Math.Abs(first - second) / (double)Rate).ShouldBeLessThanOrEqualTo(ToleranceSeconds, $"silent from device frame {first} vs {second}");
        for (var d = 0; d < rig.Devices.Count; d++)
        {
            var device = rig.Devices[d];
            device.IsClosed.ShouldBeTrue();
            device.PlayedFramesNow.ShouldBeGreaterThanOrEqualTo(SilentFrom(device), $"device {d + 1} was closed before it played up to the stop instant");
        }

        rig.Events.OfType<RunSummary>().Single().EndReason.ShouldBe(RunEndReason.Stop);
    }

    [Fact]
    public void Stop_while_paused_plays_no_audio_after_the_pause_instant()
    {
        using var rig = new Rig([(480, 480), (1_920, 480)]);
        rig.Render(100);
        rig.Call(rig.Engine.Pause).IsSuccess.ShouldBeTrue();
        rig.Render(20);
        var audibleUntil = rig.Devices.Select(SilentFrom).ToArray();

        rig.Call(() =>
        {
            rig.Engine.Stop(RunEndReason.Stop);
            return 0;
        });
        rig.Render(20);

        for (var d = 0; d < rig.Devices.Count; d++)
        {
            SilentFrom(rig.Devices[d]).ShouldBe(audibleUntil[d], $"device {d + 1} played audio after the pause instant");
        }
    }

    [Fact]
    public void Seeking_past_the_end_publishes_the_out_of_file_notice_before_the_run_summary()
    {
        using var rig = new Rig([(120, 120), (120, 120)], totalFrames: (long)Rate * 60);
        rig.Render(100);
        var seekReturned = false;
        SeekTargetOutsideFile? notice = null;
        var noticeBeforeSummary = false;
        var summarySeenAfterSeekReturned = false;
        using var probe = rig.Engine.Events.Subscribe(new Probe(e =>
        {
            if (e is RunSummary)
            {
                summarySeenAfterSeekReturned = Volatile.Read(ref seekReturned);
                noticeBeforeSummary = rig.Events.OfType<SeekTargetOutsideFile>().Any();
            }
        }));
        using var noticeProbe = rig.Engine.Events.Subscribe(new Probe(e => notice ??= e as SeekTargetOutsideFile));

        rig.Call(() =>
        {
            var result = rig.Engine.Seek(TimeSpan.FromMinutes(10));
            Volatile.Write(ref seekReturned, true);
            return result;
        }).IsSuccess.ShouldBeTrue();
        rig.Render(20);

        rig.Events.OfType<RunSummary>().Count().ShouldBe(1);
        rig.Events.OfType<SeekTargetOutsideFile>().Count().ShouldBe(1);
        noticeBeforeSummary.ShouldBeTrue("the run summary was published with no SeekTargetOutsideFile before it (cli.md 1.4: warning first, then summary)");
        summarySeenAfterSeekReturned.ShouldBeFalse("the summary is published inside Seek, after the notice");
        notice!.Target.ShouldBe(TimeSpan.FromMinutes(10));
        notice.Position.ShouldBeLessThan(notice.Target);
    }

    private sealed class Probe(Action<EngineEvent> onNext) : IObserver<EngineEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(EngineEvent value) => onNext(value);
    }

    [Fact]
    public void Pausing_while_already_paused_replies_with_the_current_position()
    {
        using var rig = SmallQueues();
        rig.Render(100);
        var first = rig.Call(rig.Engine.Pause);
        rig.Render(50);

        var second = rig.Call(rig.Engine.Pause);

        second.IsSuccess.ShouldBeTrue(second.Error?.Message);
        second.Value.TotalSeconds.ShouldBe(first.Value.TotalSeconds, 0.001);
    }

    [Fact]
    public void Resume_continues_every_output_from_the_paused_position_and_is_in_sync_within_one_second()
    {
        using var rig = SmallQueues();
        rig.Render(100);
        var paused = rig.Call(rig.Engine.Pause).Value;
        rig.Render(100);

        var resumed = rig.Call(rig.Engine.Resume);
        var commandAt = rig.Elapsed;
        rig.Render(170);

        resumed.IsSuccess.ShouldBeTrue(resumed.Error?.Message);
        resumed.Value.TotalSeconds.ShouldBe(paused.TotalSeconds, 0.001);
        AssertInSync(rig, commandAt, resumed.Value, 1.0, 1.5);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(-10)]
    [InlineData(-60)]
    public void Seeking_while_playing_moves_every_output_to_the_target_and_recovers_within_one_second(int deltaSeconds)
    {
        using var rig = SmallQueues();
        rig.Render(50);
        rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(120))).IsSuccess.ShouldBeTrue();
        rig.Render(100);
        var target = TimeSpan.FromSeconds(120 + deltaSeconds);
        var commandAt = rig.Elapsed;

        var seek = rig.Call(() => rig.Engine.Seek(target));
        var played = rig.Devices.Select(d => d.PlayedFramesNow).ToArray();
        rig.Render(170);

        seek.IsSuccess.ShouldBeTrue(seek.Error?.Message);
        seek.Value.OutsideFile.ShouldBeFalse();
        seek.Value.Paused.ShouldBeFalse();
        seek.Value.Position.TotalSeconds.ShouldBeInRange(target.TotalSeconds, target.TotalSeconds + 1.0);

        // Nothing heard from the confirmation on comes from the old position (about 121 s, plus the 2 s ring).
        for (var d = 0; d < rig.Devices.Count; d++)
        {
            for (var i = (int)played[d]; i < rig.Devices[d].Rendered.Count; i++)
            {
                var sample = rig.Devices[d].Rendered[i];
                if (sample != 0f)
                {
                    ContentSeconds(sample).ShouldBeInRange(target.TotalSeconds - 0.001, target.TotalSeconds + 4, $"device {d + 1} frame {i}");
                }
            }
        }

        AssertInSync(rig, commandAt, target, 1.0, 1.5);
    }

#pragma warning disable xUnit1031 // the fake clock is driven from this thread, so the seek has to be awaited synchronously
    [Fact]
    public void A_seek_is_confirmed_only_after_every_device_has_played_out_what_it_had_queued()
    {
        using var rig = new Rig([(480, 480), (1_920, 480)]);
        rig.Render(100);
        var written = rig.Devices.Select(d => d.WrittenFrames).ToArray();

        var seek = Task.Run(() => rig.Engine.Seek(TimeSpan.FromSeconds(60)), TestContext.Current.CancellationToken);

        seek.Wait(150, TestContext.Current.CancellationToken).ShouldBeFalse("confirmed before any device moved past its old queue");
        for (var i = 0; i < 3_000 && !seek.IsCompleted; i++)
        {
            rig.Render(1);
        }

        seek.IsCompleted.ShouldBeTrue();
        seek.Result.IsSuccess.ShouldBeTrue(seek.Result.Error?.Message);
        for (var d = 0; d < rig.Devices.Count; d++)
        {
            rig.Devices[d].PlayedFramesNow.ShouldBeGreaterThanOrEqualTo(written[d]);
        }
    }

#pragma warning restore xUnit1031

    [Fact]
    public void Seeking_while_paused_moves_the_position_keeps_every_output_silent_and_resume_continues_from_it()
    {
        using var rig = SmallQueues();
        rig.Render(100);
        rig.Call(rig.Engine.Pause).IsSuccess.ShouldBeTrue();
        rig.Render(20);
        var target = TimeSpan.FromSeconds(75);

        var seek = rig.Call(() => rig.Engine.Seek(target));
        var silentFrom = rig.Devices.Select(d => d.Rendered.Count).ToArray();
        rig.Render(50);

        seek.IsSuccess.ShouldBeTrue(seek.Error?.Message);
        seek.Value.Paused.ShouldBeTrue();
        seek.Value.OutsideFile.ShouldBeFalse();
        seek.Value.Position.TotalSeconds.ShouldBe(target.TotalSeconds, 0.001);
        for (var d = 0; d < rig.Devices.Count; d++)
        {
            rig.Devices[d].Rendered.Skip(silentFrom[d] - 1).ShouldAllBe(s => s == 0f);
        }

        var resumed = rig.Call(rig.Engine.Resume);
        var commandAt = rig.Elapsed;
        rig.Render(170);

        resumed.IsSuccess.ShouldBeTrue(resumed.Error?.Message);
        resumed.Value.TotalSeconds.ShouldBe(target.TotalSeconds, 0.001);
        AssertInSync(rig, commandAt, resumed.Value, 1.0, 1.5);
    }

    [Fact]
    public void Played_duration_after_a_forward_seek_is_the_real_running_time()
    {
        using var rig = new Rig([(120, 120), (120, 120)]);
        rig.Render(300);
        rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(63))).IsSuccess.ShouldBeTrue();
        rig.Render(200);

        rig.Call(() =>
        {
            rig.Engine.Stop(RunEndReason.Stop);
            return 0;
        });

        var summary = rig.Events.OfType<RunSummary>().Single();
        summary.Outputs.ShouldAllBe(o => Math.Abs(o.Played.TotalSeconds - rig.Elapsed.TotalSeconds) < 1.5);
    }

    [Fact]
    public void Played_duration_after_a_forward_and_a_backward_seek_is_the_real_running_time()
    {
        using var rig = new Rig([(120, 120), (120, 120)]);
        rig.Render(300);
        rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(100))).IsSuccess.ShouldBeTrue();
        rig.Render(200);
        rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(20))).IsSuccess.ShouldBeTrue();
        rig.Render(200);

        rig.Call(() =>
        {
            rig.Engine.Stop(RunEndReason.Stop);
            return 0;
        });

        var summary = rig.Events.OfType<RunSummary>().Single();
        summary.Outputs.ShouldAllBe(o => Math.Abs(o.Played.TotalSeconds - rig.Elapsed.TotalSeconds) < 1.5);
    }

    [Fact]
    public void Played_duration_after_a_seek_past_the_end_is_the_real_running_time()
    {
        using var rig = new Rig([(120, 120), (120, 120)], totalFrames: (long)Rate * 60);
        rig.Render(300);

        rig.Call(() => rig.Engine.Seek(TimeSpan.FromMinutes(100))).IsSuccess.ShouldBeTrue();
        rig.Render(20);

        var summary = rig.Events.OfType<RunSummary>().Single();
        summary.EndReason.ShouldBe(RunEndReason.SeekPastEnd);
        summary.Outputs.ShouldAllBe(o => Math.Abs(o.Played.TotalSeconds - rig.Elapsed.TotalSeconds) < 1.5);
    }

    [Fact]
    public void Seeking_before_the_start_plays_from_the_beginning_and_reports_the_target_was_outside_the_file()
    {
        using var rig = SmallQueues();
        rig.Render(100);
        var commandAt = rig.Elapsed;

        var seek = rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(-5)));
        rig.Render(170);

        seek.IsSuccess.ShouldBeTrue(seek.Error?.Message);
        seek.Value.OutsideFile.ShouldBeTrue();
        seek.Value.Position.TotalSeconds.ShouldBeInRange(0, 1.0);
        rig.Events.OfType<RunSummary>().ShouldBeEmpty();
        AssertInSync(rig, commandAt, TimeSpan.Zero, 1.0, 1.5);
    }

    [Fact]
    public void Seeking_past_the_end_ends_the_run_with_a_summary_after_every_output_falls_silent()
    {
        using var rig = new Rig([(120, 120), (120, 120)], totalFrames: (long)Rate * 60);
        rig.Render(100);

        var seek = rig.Call(() => rig.Engine.Seek(TimeSpan.FromMinutes(10)));
        rig.Render(20);

        seek.IsSuccess.ShouldBeTrue(seek.Error?.Message);
        seek.Value.OutsideFile.ShouldBeTrue();
        seek.Value.Position.TotalSeconds.ShouldBe(60, 0.001, "the position moves to the end of the file");
        var summary = rig.Events.OfType<RunSummary>().Single();
        summary.EndReason.ShouldBe(RunEndReason.SeekPastEnd);
        summary.ClockPosition.TotalSeconds.ShouldBe(60, 0.001, "the run summary reports the end of the file");
        summary.Outputs.Count.ShouldBe(2);
        foreach (var device in rig.Devices)
        {
            device.IsClosed.ShouldBeTrue();
        }
    }

    [Fact]
    public void A_seek_after_the_decoder_reached_the_end_of_the_file_plays_from_the_target()
    {
        using var rig = new Rig([(120, 120), (120, 120)], totalFrames: (long)Rate * 60);
        rig.Call(() => rig.Engine.Seek(TimeSpan.FromSeconds(58.5))).IsSuccess.ShouldBeTrue();
        rig.Render(50); // the last 1.5 s fit in the ring: the decoder has reached the end of the file by now
        var target = TimeSpan.FromSeconds(30);
        var commandAt = rig.Elapsed;

        var seek = rig.Call(() => rig.Engine.Seek(target));
        rig.Render(170);

        seek.IsSuccess.ShouldBeTrue(seek.Error?.Message);
        seek.Value.OutsideFile.ShouldBeFalse();
        rig.Events.OfType<RunSummary>().ShouldBeEmpty();
        AssertInSync(rig, commandAt, target, 1.0, 1.5);
    }

    [Fact]
    public void Resuming_before_the_pause_instant_continues_from_the_real_position_without_a_jump()
    {
        // 40 ms queued in every device: the pause instant lies 40 ms ahead of the command.
        using var rig = new Rig([(1_920, 480), (1_920, 480)]);
        rig.Render(100);
        var paused = rig.Call(rig.Engine.Pause).Value;
        var commandAt = rig.Elapsed;

        var resumed = rig.Call(rig.Engine.Resume);
        rig.Render(170);

        resumed.IsSuccess.ShouldBeTrue(resumed.Error?.Message);
        resumed.Value.ShouldBeLessThan(paused, "the devices have not reached the pause instant yet");
        AssertInSync(rig, commandAt, resumed.Value, 0.0, 1.5);
    }
}
