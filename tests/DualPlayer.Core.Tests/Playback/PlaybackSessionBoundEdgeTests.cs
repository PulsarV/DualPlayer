using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T155 (AA2): the spec §6 bounds (output 10 ms, picture 20 ms, late 20 ms, recovery 1 s) pinned 1 ms inside and outside their edges.</summary>
public sealed class PlaybackSessionBoundEdgeTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig(bool frames = false)
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        if (frames)
        {
            rig.Renderer.FrameDuration = TimeSpan.FromMilliseconds(40);
        }

        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    private static LipSyncedAgain? SeekRecord(SessionRig rig) => rig.Events.OfType<LipSyncedAgain>().LastOrDefault(e => e.Command == "seek");

    [Theory]
    [InlineData(11, false)]
    [InlineData(-11, false)]
    [InlineData(9, true)]
    [InlineData(-9, true)]
    public void An_output_off_by_just_over_10_ms_holds_lip_synced_again_back_and_one_just_under_does_not_AC_17(double errorMs, bool reported)
    {
        using var rig = PlayingRig();
        rig.ForceOutputError("dev-2", errorMs);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(80); // 0.8 s, inside the 1 s budget

        (SeekRecord(rig) is not null).ShouldBe(reported, $"an output {errorMs} ms off was judged on the wrong side of 10 ms");
    }

    [Theory]
    [InlineData(21, false)]
    [InlineData(-21, false)]
    [InlineData(19, true)]
    [InlineData(-19, true)]
    public void A_picture_off_by_just_over_20_ms_holds_lip_synced_again_back_and_one_just_under_does_not_AC_04(double errorMs, bool reported)
    {
        using var rig = PlayingRig();
        rig.ForceOutputError("dev-2", 30); // keeps the record owed while the seek's align (240 ms picture delay) completes

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(40);
        SeekRecord(rig).ShouldBeNull();
        rig.Renderer.Jump(TimeSpan.FromMilliseconds(errorMs));
        rig.ForceOutputError("dev-2", 0);
        rig.Engine.Render(30); // steering at 0.2 % moves the error by well under 1 ms in that time

        (SeekRecord(rig) is not null).ShouldBe(reported, $"a picture {errorMs} ms off was judged on the wrong side of 20 ms");
    }

    [Fact]
    public void A_frame_21_ms_after_due_counts_as_late_AC_17()
    {
        using var rig = PlayingRig(frames: true);

        rig.Renderer.Jump(TimeSpan.FromMilliseconds(-21));
        rig.Engine.Render(30);

        rig.Session.Counters.Late.ShouldBeGreaterThan(0, "a frame 21 ms after due was not counted late");
    }

    [Fact]
    public void A_frame_19_ms_after_due_does_not_count_as_late_AC_17()
    {
        using var rig = PlayingRig(frames: true);

        rig.Renderer.Jump(TimeSpan.FromMilliseconds(-19));
        rig.Engine.Render(30);

        rig.Session.Counters.Late.ShouldBe(0);
    }

    [Fact]
    public void A_seek_whose_output_qualifies_at_1_1_s_gives_up_AC_17()
    {
        using var rig = PlayingRig();
        rig.ForceOutputError("dev-2", 30);
        var seen = rig.Events.Count;

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(110);
        rig.ForceOutputError("dev-2", 0);
        rig.Engine.Render(30);

        rig.Events.Skip(seen).OfType<NotLipSynced>().Where(e => e.Command == "seek").ShouldHaveSingleItem("recovery that took 1.1 s was not given up on");
        SeekRecord(rig).ShouldBeNull("a recovery past the 1 s budget was reported as lip-synced again");
    }

    [Fact]
    public void A_seek_whose_output_qualifies_at_0_9_s_is_lip_synced_again_AC_17()
    {
        using var rig = PlayingRig();
        rig.ForceOutputError("dev-2", 30);
        var seen = rig.Events.Count;

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(85);
        rig.ForceOutputError("dev-2", 0);
        rig.Engine.Render(30);

        var again = SeekRecord(rig).ShouldNotBeNull("a recovery that took 0.9 s was not reported");
        again.AfterCommandMs.ShouldBeLessThanOrEqualTo(1000);
        rig.Events.Skip(seen).OfType<NotLipSynced>().ShouldBeEmpty();
    }
}
