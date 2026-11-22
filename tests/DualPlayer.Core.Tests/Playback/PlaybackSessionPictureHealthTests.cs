using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

public sealed class PlaybackSessionPictureHealthTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig()
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    private static void ShouldHaveEndedOnPictureFailure(SessionRig rig)
    {
        // The failure is queued on the session's loop; Flush returns once that loop has handled it (no real-time wait).
        rig.Flush();
        rig.Engine.Summary.ShouldNotBeNull("the picture failure never stopped the engine");
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        var failed = rig.Events.OfType<PictureFailed>().ShouldHaveSingleItem();
        failed.Reason.ShouldNotBeNullOrWhiteSpace();
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "The picture could not be decoded further - film ended");
        rig.Renderer.Notes.ShouldContain(n => n.Text == "The picture could not be decoded further - film ended");
        rig.Renderer.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void A_picture_that_cannot_be_decoded_ends_the_film_with_a_plain_note_and_a_run_summary_AC_06b()
    {
        using var rig = PlayingRig();

        rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "The picture cannot be decoded."));

        ShouldHaveEndedOnPictureFailure(rig);
    }

    [Fact]
    public void A_decode_failure_reloads_the_file_paused_at_the_last_presented_position_before_the_note_AC_06b()
    {
        using var rig = PlayingRig();
        rig.Engine.Render(500);
        var shownBefore = rig.Renderer.Presented.ShouldNotBeNull().Position;
        shownBefore.ShouldBeGreaterThan(TimeSpan.Zero);
        var loadsBefore = rig.Renderer.LoadCount;

        rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "The picture cannot be decoded."));

        ShouldHaveEndedOnPictureFailure(rig);
        rig.Renderer.LoadCount.ShouldBe(loadsBefore + 1);
        rig.Renderer.IsPlaying.ShouldBeFalse();
        rig.Renderer.Presented.ShouldNotBeNull().Position.ShouldBe(shownBefore, TimeSpan.FromMilliseconds(250));
        rig.Renderer.Notes.ShouldHaveSingleItem().Text.ShouldBe("The picture could not be decoded further - film ended");
    }

    [Fact]
    public void A_failed_reload_after_a_decode_failure_still_shows_the_note_and_throws_nothing_AC_06b()
    {
        using var rig = PlayingRig();
        rig.Renderer.LoadError = new Error("mpv.load_failed", "The file cannot be opened.");

        rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "The picture cannot be decoded."));

        ShouldHaveEndedOnPictureFailure(rig);
        rig.Renderer.Notes.ShouldContain(n => n.Text == "The picture could not be decoded further - film ended");
    }

    [Fact]
    public void Hardware_decoding_turning_inactive_ends_the_film_as_a_picture_failure_AC_06b()
    {
        using var rig = PlayingRig();

        rig.Renderer.InjectHardwareDecodingInactive();

        ShouldHaveEndedOnPictureFailure(rig);
    }

    [Fact]
    public void A_picture_failure_raised_while_the_film_is_starting_stops_the_sound_and_ends_the_film_AC_06b()
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        using var _ = rig;
        rig.Renderer.AfterLoad = rig.Renderer.InjectHardwareDecodingInactive;

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();

        ShouldHaveEndedOnPictureFailure(rig);
    }

    [Fact]
    public void A_damaged_stretch_that_recovers_keeps_the_sound_playing_and_is_logged_once_with_position_and_length_AC_06b()
    {
        using var rig = PlayingRig();

        rig.Renderer.InjectFrames(skippedDamaged: 5);
        rig.Engine.Render(20);
        rig.Renderer.InjectFrames(skippedDamaged: 3);
        rig.Engine.Render(300);

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Events.OfType<PictureFailed>().ShouldBeEmpty();
        var stretch = rig.Events.OfType<DamagedStretchSkipped>().ShouldHaveSingleItem();
        stretch.Frames.ShouldBe(8);
        stretch.Position.ShouldBeGreaterThan(TimeSpan.Zero);
        stretch.Length.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void The_frame_counters_are_logged_once_per_minute_with_the_skipped_damaged_frames_AC_06b()
    {
        using var rig = PlayingRig();
        rig.Events.OfType<FrameCountersReported>().ShouldBeEmpty();

        // Late is the session's own count against due time (T30); the renderer's own late count is not used.
        rig.Renderer.InjectFrames(late: 2, dropped: 1, skippedDamaged: 4);
        rig.Engine.Render(6_000);

        var reports = rig.Events.OfType<FrameCountersReported>().ToList();
        reports.Count.ShouldBeInRange(1, 2);
        reports[0].Counters.ShouldBe(new FrameCounters(Late: 0, Dropped: 1, SkippedDamaged: 4));
    }

    [Fact]
    public void The_picture_timing_is_logged_once_per_second_AC_17()
    {
        using var rig = PlayingRig();
        var before = rig.Events.OfType<PictureTimingReported>().Count();

        rig.Engine.Render(500);

        var added = rig.Events.OfType<PictureTimingReported>().Count() - before;
        added.ShouldBeInRange(4, 6);
    }

    [Fact]
    public void Pause_and_resume_each_log_the_picture_timing_AC_17()
    {
        using var rig = PlayingRig();

        var before = rig.Events.OfType<PictureTimingReported>().Count();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        var afterPause = rig.Events.OfType<PictureTimingReported>().Count();
        afterPause.ShouldBeGreaterThan(before);

        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue();

        // The picture is held until it starts with the sound one picture delay later: the first frame measured then is logged.
        rig.Engine.Render(30);
        rig.Events.OfType<PictureTimingReported>().Count().ShouldBeGreaterThan(afterPause);
    }
}
