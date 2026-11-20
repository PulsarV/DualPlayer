using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T29: end reasons, measured start timings and the session records cli.md §3 and sad.md flows 4, 5, 7 ask for.</summary>
public sealed class PlaybackSessionLogRecordsTests : IDisposable
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-records-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    // dev-1: Bluetooth headphones, 40 + 200 = 240 ms assumed; dev-2: wired, 0 ms. Picture delay 240 ms.
    private SessionRig PlayingRig(Action<SessionRig>? arrange = null)
    {
        File("Episode 2.mkv");
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "wasapi-stream-latency", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "wasapi-stream-latency", ConnectionKind.Usb, FormFactor.Speaker));
        arrange?.Invoke(rig);
        var request = rig.Request(2) with { File = File("Episode 1.mkv") };
        rig.Engine.Call(() => rig.Session.Start(request)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void A_file_change_ends_the_old_run_with_the_file_change_reason_AC_07()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();

        rig.Engine.Events.OfType<RunSummary>().First().EndReason.ShouldBe(RunEndReason.FileChange);
    }

    [Fact]
    public void A_picture_failure_ends_the_run_with_the_picture_failed_reason_AC_06b()
    {
        using var rig = PlayingRig();

        rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "broken"));

        rig.Engine.WaitFor(() => rig.Engine.Summary is not null).ShouldBeTrue();
        rig.Engine.Summary!.EndReason.ShouldBe(RunEndReason.PictureFailed);
    }

    [Fact]
    public void A_file_change_records_the_picture_delay_with_the_file_change_cause_AC_17()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();

        var change = rig.Events.OfType<PictureDelayChanged>().Where(e => e.Cause == PictureDelayCause.FileChange).ShouldHaveSingleItem();
        change.NewPictureDelayMs.ShouldBe(240);
        change.Outputs.Select(o => o.DeviceId).ShouldBe(["dev-1", "dev-2"]);
    }

    [Fact]
    public void A_seek_is_followed_by_a_lip_synced_again_record_within_a_second_AC_17()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(150);

        var again = rig.Events.OfType<LipSyncedAgain>().Last();
        again.Command.ShouldBe("seek");
        again.AfterCommandMs.ShouldBeInRange(0, 1000);
        Math.Abs(again.ErrorMs).ShouldBeLessThanOrEqualTo(20);
    }

    [Fact]
    public void An_offset_change_is_recorded_again_with_its_save_time_once_the_debounced_save_has_run_AC_10()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        rig.Events.OfType<OffsetChanged>().ShouldHaveSingleItem().SavedAt.ShouldBeNull();
        rig.Engine.Render(150);

        var saved = rig.Events.OfType<OffsetChanged>().Last();
        saved.SavedAt.ShouldNotBeNull("the save landed but was never recorded");
        saved.NewOffsetMs.ShouldBe(10);
        saved.DeviceId.ShouldBe("dev-1");
    }

    [Fact]
    public void Start_timings_carry_the_command_time_given_and_the_measured_first_frame_and_first_audio_AC_01()
    {
        var late = TimeSpan.FromMilliseconds(30);
        using var rig = PlayingRig(r => r.Renderer.PresentsLateBy = late);

        var timings = rig.Events.OfType<StartTimings>().ShouldHaveSingleItem();
        var frequency = rig.Engine.Time.TimestampFrequency;
        var frameZero = rig.Engine.Engine.FrameZeroTimestamp!.Value;
        ((timings.FirstFramePresented - frameZero) * 1000.0 / frequency).ShouldBe(late.TotalMilliseconds, 1, "the first frame is the plan, not the measured present");
        foreach (var (deviceId, assumedMs) in new[] { ("dev-1", 240), ("dev-2", 0) })
        {
            var given = rig.Engine.Engine.FirstAudioTimestamp(deviceId);
            given.ShouldNotBeNull($"no first audio for {deviceId}");
            ((timings.FirstAudioHeard[deviceId] - given.Value) * 1000.0 / frequency).ShouldBe(assumedMs, 1);
        }
    }

    [Fact]
    public void Start_timings_take_the_command_time_from_the_request_AC_01()
    {
        using var rig = new SessionRig(TwoDevices, null, 600);
        var commandAt = rig.Engine.Time.GetTimestamp() - 12_345;
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { CommandTimestamp = commandAt })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Events.OfType<StartTimings>().ShouldHaveSingleItem().CommandTimestamp.ShouldBe(commandAt);
    }

    [Fact]
    public void An_output_lost_in_the_pre_roll_is_left_out_of_the_one_start_timings_record_and_the_engine_is_no_longer_asked_AC_01_AC_17()
    {
        CountingLagControl? lags = null;
        using var rig = new SessionRig(TwoDevices, null, 600, wrapLags: inner => lags = new CountingLagControl(inner));
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "wasapi-stream-latency", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "wasapi-stream-latency", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.LoseOutputInPreRoll(1);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Events.OfType<OutputLost>().ShouldHaveSingleItem().DeviceId.ShouldBe("dev-1");
        rig.Engine.Engine.FirstAudioTimestamp("dev-1").ShouldBeNull("the lost output must never have handed over audio");
        var timings = rig.Events.OfType<StartTimings>().ShouldHaveSingleItem();
        timings.FirstAudioHeard.Keys.ShouldBe(["dev-2"]);

        var asked = lags!.FirstAudioQueries;
        rig.Engine.Render(100);
        rig.Events.OfType<StartTimings>().Count().ShouldBe(1);
        lags.FirstAudioQueries.ShouldBe(asked, "every sync tick still asks the engine for first audio after the timings were published");
    }

    [Fact]
    public void A_file_changes_start_timings_are_measured_from_the_key_not_from_the_process_start_command_AC_07_AC_17()
    {
        File("Episode 2.mkv");
        using var rig = new SessionRig(TwoDevices, null, 600);
        var processStart = rig.Engine.Time.GetTimestamp();
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode 1.mkv"), CommandTimestamp = processStart })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        rig.Events.OfType<StartTimings>().ShouldHaveSingleItem().CommandTimestamp.ShouldBe(processStart);

        var keyPressed = rig.Engine.Time.GetTimestamp();
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        var timings = rig.Events.OfType<StartTimings>().ToList();
        timings.Count.ShouldBe(2);
        var secondsAfterKey = (timings[1].CommandTimestamp - keyPressed) / (double)rig.Engine.Time.TimestampFrequency;
        secondsAfterKey.ShouldBeInRange(0, 0.25, "the file change's command time is the key press, not the start command three seconds earlier");
    }

    [Fact]
    public void Each_output_latency_names_its_source_and_a_failed_probe_says_why_ADR_0004()
    {
        using var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "wasapi-stream-latency", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();

        var outputs = rig.Events.OfType<LatencyEstimated>().ShouldHaveSingleItem().Estimate.Outputs;
        outputs[0].Source.ShouldBe("wasapi-stream-latency");
        outputs[1].Source.ShouldNotBeNull().ShouldContain("probe.unknown_device");
    }

    private sealed class CountingLagControl(IOutputLagControl inner) : IOutputLagControl
    {
        private int _firstAudioQueries;

        public int FirstAudioQueries => Volatile.Read(ref _firstAudioQueries);

        public long? FrameZeroTimestamp => inner.FrameZeroTimestamp;

        public long? ClockStartTimestamp => inner.ClockStartTimestamp;

        public bool IsRamping => inner.IsRamping;

        public Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode) => inner.SetLagTargets(targets, mode);

        public TimeSpan? CurrentLag(string deviceId) => inner.CurrentLag(deviceId);

        public long? FirstAudioTimestamp(string deviceId)
        {
            Interlocked.Increment(ref _firstAudioQueries);
            return inner.FirstAudioTimestamp(deviceId);
        }
    }
}
