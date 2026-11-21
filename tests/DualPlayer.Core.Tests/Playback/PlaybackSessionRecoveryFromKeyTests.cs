using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T71 (R5): recovery after a seek, a resume and a return into the film is timed from the key, and a return is judged as a start.</summary>
public sealed class PlaybackSessionRecoveryFromKeyTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig(double totalSeconds = 600)
    {
        var rig = new SessionRig(TwoDevices, null, totalSeconds);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void A_seek_whose_engine_call_takes_300_ms_reports_the_time_since_the_key_including_those_300_ms_AC_04()
    {
        using var rig = PlayingRig();
        rig.EngineCommandTakes = TimeSpan.FromMilliseconds(300);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200);

        var again = rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "seek").ShouldHaveSingleItem("the seek's lip-synced record was never published");
        again.AfterCommandMs.ShouldBeGreaterThanOrEqualTo(300, "the engine's 300 ms were left out of the time since the key");
    }

    [Fact]
    public void A_resume_whose_engine_call_takes_300_ms_reports_the_time_since_the_key_including_those_300_ms_AC_04()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(20);
        rig.EngineCommandTakes = TimeSpan.FromMilliseconds(300);

        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200);

        var again = rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "resume").ShouldHaveSingleItem("the resume's lip-synced record was never published");
        again.AfterCommandMs.ShouldBeGreaterThanOrEqualTo(300, "the engine's 300 ms were left out of the time since the key");
    }

    [Fact]
    public void A_backward_seek_from_the_end_that_recovers_in_2_s_logs_lip_synced_again_as_a_return_and_no_give_up_AC_05_AC_17()
    {
        using var rig = PlayingRig(30);
        rig.Renderer.Duration = TimeSpan.FromSeconds(30);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(60))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(50);
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        var seen = rig.Events.Count;
        rig.EngineCommandTakes = TimeSpan.FromSeconds(2); // the engine start with its pre-roll

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        var after = rig.Events.Skip(seen).ToList();
        after.OfType<NotLipSynced>().ShouldBeEmpty("a return that recovered inside the 3 s start budget was logged as a give-up");
        var again = after.OfType<LipSyncedAgain>().ShouldHaveSingleItem("the return's lip-synced record was never published");
        again.Command.ShouldBe("return");
        again.AfterCommandMs.ShouldBeInRange(2000, 3000);
    }
}
