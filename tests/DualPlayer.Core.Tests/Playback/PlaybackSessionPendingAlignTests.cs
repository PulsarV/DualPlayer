using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T43: a resume before the picture froze keeps the sync loop out until the picture is aligned; stale renderer events are dropped.</summary>
public sealed class PlaybackSessionPendingAlignTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    // dev-1 is heard 200 ms late (a Bluetooth-type output), dev-2 is wired: the picture delay is 200 ms.
    private static SessionRig PlayingRig()
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(200), "test", ConnectionKind.Usb, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Theory]
    [InlineData(15)] // picture ~150 ms ahead of the due position: over the seek threshold
    [InlineData(3)] // picture ~30 ms ahead: under it, but not yet aligned
    public void Resume_before_the_picture_froze_never_moves_the_picture_back_or_ahead_of_due_and_is_lip_synced_only_once_aligned_AC_04(int stepsBeforeResume)
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        var freeze = rig.Events.OfType<PictureDelayChanged>().Last(e => e.Cause == PictureDelayCause.Pause).Clock;
        var delay = TimeSpan.FromMilliseconds(rig.Events.OfType<PictureDelayChanged>().Last().NewPictureDelayMs);
        delay.ShouldBe(TimeSpan.FromMilliseconds(200), "the scenario needs a 200 ms picture delay");

        rig.Engine.Render(stepsBeforeResume);
        rig.Renderer.IsPlaying.ShouldBeTrue("the picture should still be running up to the freeze point");
        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue();
        var resumedAt = rig.Engine.Time.GetTimestamp();
        var shownAtResume = rig.Renderer.ShownNow;
        rig.Renderer.ResetLowestShown();
        (freeze - shownAtResume).ShouldBeGreaterThan(TimeSpan.Zero, "the picture was already at the freeze point");

        var previous = shownAtResume;
        for (var i = 0; i < 60; i++)
        {
            rig.Engine.Render(1);
            var since = rig.Engine.Time.GetElapsedTime(resumedAt);
            var shown = rig.Renderer.ShownNow;
            var due = since < delay ? freeze : freeze + (since - delay);
            shown.ShouldBeGreaterThanOrEqualTo(previous, $"the picture went back (repeated frames) {since.TotalMilliseconds} ms after the resume");
            shown.ShouldBeLessThanOrEqualTo(due + TimeSpan.FromMilliseconds(20), $"the picture jumped ahead of due (skipped frames) {since.TotalMilliseconds} ms after the resume");
            previous = shown;
        }

        rig.Renderer.LowestShown?.ShouldBeGreaterThanOrEqualTo(shownAtResume, "the picture was sought back");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
        foreach (var again in rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "resume"))
        {
            again.AfterCommandMs.ShouldBeGreaterThanOrEqualTo(delay.TotalMilliseconds - 10, "lip-synced was reported before the picture was aligned");
        }
    }
}
