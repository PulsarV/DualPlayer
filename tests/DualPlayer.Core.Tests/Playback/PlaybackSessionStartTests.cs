using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Settings;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

public sealed class PlaybackSessionStartTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static DeviceLatencyReport Bluetooth(int reportedMs) =>
        new(TimeSpan.FromMilliseconds(reportedMs), "test", ConnectionKind.Bluetooth, FormFactor.Headphones);

    private static DeviceLatencyReport Usb() =>
        new(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker);

    private static SessionRig Rig(DeviceOffsets? remembered = null, double totalSeconds = 600)
    {
        var rig = new SessionRig(TwoDevices, remembered, totalSeconds);
        rig.Probe.Set("dev-1", Bluetooth(40));
        rig.Probe.Set("dev-2", Usb());
        return rig;
    }

    [Fact]
    public void A_first_use_device_is_estimated_as_reported_plus_type_default_with_offset_zero_AC_13()
    {
        using var rig = Rig();

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();

        var estimate = rig.Events.OfType<LatencyEstimated>().ShouldHaveSingleItem().Estimate;
        var bluetooth = estimate.Outputs.Single(o => o.DeviceId == "dev-1");
        bluetooth.Type.ShouldBe(DeviceType.BluetoothHeadphones);
        bluetooth.ReportedMs.ShouldBe(40);
        bluetooth.TypeDefaultMs.ShouldBe(200);
        bluetooth.OffsetMs.ShouldBe(0);
        bluetooth.AssumedMs.ShouldBe(240);
        estimate.PictureDelayMs.ShouldBe(240);
        estimate.Outputs.Single(o => o.DeviceId == "dev-2").LagMs.ShouldBe(240);
    }

    [Fact]
    public void A_remembered_offset_is_used_and_the_latency_is_worked_out_again_AC_12()
    {
        var remembered = new DeviceOffsetsBuilder().With("dev-1", 30).Build();
        using var rig = Rig(remembered);

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();

        var estimate = rig.Events.OfType<LatencyEstimated>().ShouldHaveSingleItem().Estimate;
        var bluetooth = estimate.Outputs.Single(o => o.DeviceId == "dev-1");
        bluetooth.OffsetMs.ShouldBe(30);
        bluetooth.AssumedMs.ShouldBe(40 + 200 - 30);
        estimate.PictureDelayMs.ShouldBe(210);
    }

    [Fact]
    public void Unreadable_remembered_offsets_start_the_film_at_offset_zero_and_pass_the_state_warning_on()
    {
        var rig = new SessionRig(TwoDevices, stored: Result<DeviceOffsets>.Failure("state.unreadable", "cannot read device-offsets.json"));
        using var _ = rig;
        rig.Probe.Set("dev-1", Bluetooth(40));
        rig.Probe.Set("dev-2", Usb());

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();

        rig.Events.OfType<OffsetsWarning>().ShouldHaveSingleItem().Warning.Code.ShouldBe("state.unreadable");
        rig.Events.OfType<LatencyEstimated>().ShouldHaveSingleItem().Estimate.Outputs.ShouldAllBe(o => o.OffsetMs == 0);
    }

    [Fact]
    public void A_remembered_offset_off_the_grid_is_corrected_and_reported_as_a_warning()
    {
        var remembered = new DeviceOffsetsBuilder().With("dev-1", 600).Build();
        using var rig = Rig(remembered);

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();

        rig.Events.OfType<OffsetsWarning>().ShouldHaveSingleItem().Warning.Code.ShouldBe("offsets.clamped");
        rig.Events.OfType<LatencyEstimated>().Single().Estimate.Outputs.Single(o => o.DeviceId == "dev-1").OffsetMs.ShouldBe(500);
    }

    [Fact]
    public void Starting_plays_picture_and_every_output_together_with_each_outputs_lag_AC_01()
    {
        using var rig = Rig();
        var commandAt = rig.Engine.Elapsed;

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Engine.Devices.Count.ShouldBe(2);
        var engineStarted = rig.Engine.Events.OfType<AudioStarted>().ShouldHaveSingleItem();
        rig.Engine.Engine.CurrentLag("dev-2").ShouldBe(TimeSpan.FromMilliseconds(240));
        rig.Engine.Engine.CurrentLag("dev-1").ShouldBe(TimeSpan.Zero);

        rig.Renderer.IsPlaying.ShouldBeTrue();
        // StartToAudio ends when the slowest output hears frame 0 (clock start + max lag), so `sinceFrameZero`
        // is already clock − maxLag; the heard (and due) position is clock − PD = sinceFrameZero + maxLag − PD.
        var sinceFrameZero = rig.Engine.Elapsed - commandAt - engineStarted.StartToAudio;
        var maxLag = TimeSpan.FromMilliseconds(240);
        var pictureDelay = TimeSpan.FromMilliseconds(240);
        var due = sinceFrameZero + maxLag - pictureDelay;
        var shown = rig.Renderer.Presented!.Position;
        Math.Abs((shown - due).TotalMilliseconds).ShouldBeLessThan(20);

        var timings = rig.Events.OfType<StartTimings>().ShouldHaveSingleItem();
        timings.FirstAudioHeard.Keys.ShouldBe(["dev-1", "dev-2"], ignoreOrder: true);
    }

    [Fact]
    public void A_renderer_load_failure_returns_an_error_and_starts_no_output_AC_01()
    {
        using var rig = Rig();
        rig.Renderer.LoadError = new Error("mpv.load_failed", "no");

        var result = rig.Engine.Call(() => rig.Session.Start(rig.Request(2)));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("mpv.load_failed");
        rig.Engine.Devices.ShouldBeEmpty();
        rig.Session.State.ShouldBe(PlaybackState.Idle);
    }

    [Fact]
    public void At_the_natural_end_the_session_ends_with_a_run_summary_and_pause_does_nothing_AC_06()
    {
        using var rig = Rig(totalSeconds: 3);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();

        rig.Engine.Render(250);
        rig.Renderer.InjectEndOfFile();
        rig.Engine.WaitFor(() =>
        {
            rig.Engine.Render(10);
            return rig.Session.State == PlaybackState.Ended;
        }).ShouldBeTrue();

        rig.Engine.Summary.ShouldNotBeNull().EndReason.ShouldBe(RunEndReason.FileEnd);
        rig.Engine.Summary!.Outputs.Count.ShouldBe(2);
        rig.Renderer.IsDisposed.ShouldBeFalse();
        rig.Session.Pause();
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Fact]
    public void Exit_silences_every_output_prints_the_run_summary_and_closes_the_renderer_AC_06()
    {
        using var rig = Rig();
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);

        rig.Engine.Call(() =>
        {
            rig.Session.Exit();
            return 0;
        });

        rig.Engine.Summary.ShouldNotBeNull().EndReason.ShouldBe(RunEndReason.Stop);
        rig.Engine.Summary!.Outputs.Count.ShouldBe(2);
        rig.Renderer.IsDisposed.ShouldBeTrue();
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }
}
