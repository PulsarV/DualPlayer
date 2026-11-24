using DualPlayer.App.ViewModels;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualPlayer.App.Tests.ViewModels;

/// <summary>T85 (P9): the window's controls stamp a key when it arrives, so a key that waits in the queue is timed from its press.</summary>
public sealed class PlaybackControlsKeyArrivalTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    [Fact]
    public void A_seek_queued_behind_a_2_s_command_reports_the_time_since_its_key_press_not_since_it_started_AC_04()
    {
        using var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(20);
        using var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
        rig.EngineCommandTakes = TimeSpan.FromSeconds(2); // the resume's engine call, and then the seek's

        controls.TogglePause(); // resumes: 2 s in the engine
        controls.Seek(TimeSpan.FromSeconds(10)); // pressed at once, runs only when the resume is done
        rig.Engine.AdvanceUntil(() => SeekRecordsMs(rig).Count > 0, maxSteps: 3_000).ShouldBeTrue("the seek's lip-synced record was never published");

        // 2 s waiting for the resume + 2 s in the seek's own engine call; counted from the seek's own start it would be about 2 s.
        SeekRecordsMs(rig).ShouldAllBe(ms => ms >= 3_900, "the seek's time since the key press left out its wait behind the resume");
    }

    private static List<double> SeekRecordsMs(SessionRig rig) =>
    [
        .. rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "seek").Select(e => e.AfterCommandMs),
        .. rig.Events.OfType<NotLipSynced>().Where(e => e.Command == "seek").Select(e => e.AfterCommandMs),
    ];
}
