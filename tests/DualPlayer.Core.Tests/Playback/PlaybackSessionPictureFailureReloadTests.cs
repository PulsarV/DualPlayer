using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T80: on a picture failure the film is stopped and the failure published before the last good frame is reloaded (the
/// reload is bounded and never holds the session), and a reload that worked is not repeated when the film is returned to.
/// </summary>
public sealed class PlaybackSessionPictureFailureReloadTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private static readonly Error DecodeError = new("video.decode_failed", "The picture cannot be decoded.");

    private static SessionRig PlayingRig()
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(800); // eight seconds of the film
        return rig;
    }

    private static void FailThePicture(SessionRig rig)
    {
        rig.Renderer.InjectDecodeFailure(DecodeError);

        // The failure is queued on the session's loop when it is injected; Flush returns once that loop has handled it (the
        // engine stop, the events and the reload), so nothing here races a real-time wait against the session thread.
        rig.Flush();
        rig.Engine.Summary.ShouldNotBeNull("the picture failure never stopped the engine");
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        rig.Events.OfType<PictureFailed>().ShouldHaveSingleItem();
        rig.Renderer.Notes.ShouldContain(n => n.Text == "The picture could not be decoded further - film ended");
    }

    [Fact]
    public void A_picture_failure_stops_the_engine_and_publishes_the_failure_before_the_last_frame_is_reloaded_AC_06b()
    {
        using var rig = PlayingRig();

        FailThePicture(rig);

        var order = rig.CallOrder;
        var stop = order.ToList().IndexOf("engine.Stop(PictureFailed)");
        var published = order.ToList().IndexOf("event.PictureFailed");
        var load = order.ToList().LastIndexOf("renderer.Load(paused)");
        var seek = order.ToList().LastIndexOf("renderer.Seek");
        var note = order.ToList().LastIndexOf("renderer.ShowNote");
        stop.ShouldBeGreaterThanOrEqualTo(0, string.Join(" > ", order));
        published.ShouldBeGreaterThanOrEqualTo(0, string.Join(" > ", order));
        load.ShouldBeGreaterThan(stop, "the film is stopped before mpv reopens the file: " + string.Join(" > ", order));
        load.ShouldBeGreaterThan(published, "the failure is published before mpv reopens the file: " + string.Join(" > ", order));
        seek.ShouldBeGreaterThan(load, "the reloaded frame is moved to the last presented position: " + string.Join(" > ", order));
        note.ShouldBeGreaterThan(seek, "the note goes on the reloaded frame: " + string.Join(" > ", order));
    }

    [Fact]
    public void The_run_summary_ends_at_the_failure_position_even_when_the_reload_takes_time_AC_06b_AC_05()
    {
        using var rig = PlayingRig();
        rig.Renderer.LoadTakes = TimeSpan.FromSeconds(2);

        FailThePicture(rig);

        var failedAt = rig.Events.OfType<PictureFailed>().Single().Clock;
        rig.Engine.Summary.ShouldNotBeNull().ClockPosition.ShouldBe(failedAt, TimeSpan.FromMilliseconds(100), "the sound stopped when the picture failed, not when the reload finished");
    }

    [Fact]
    public void The_reload_wait_is_bounded_to_three_seconds_and_a_reload_that_does_not_finish_leaves_the_note_and_a_session_that_answers_AC_06b()
    {
        using var rig = PlayingRig();
        rig.Renderer.LoadTakes = TimeSpan.FromSeconds(15);
        var before = rig.Engine.Time.GetUtcNow();

        FailThePicture(rig);

        var reload = rig.Renderer.LoadTimeouts.Last().ShouldNotBeNull("the reload was given no bound");
        reload.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(3));
        (rig.Engine.Time.GetUtcNow() - before).ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(3.5), "the session was held longer than the bound by the reload");
        rig.Renderer.Notes.ShouldContain(n => n.Text == "The picture could not be decoded further - film ended");

        rig.Renderer.LoadTakes = TimeSpan.Zero;
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue("the next key was not answered");
    }

    [Fact]
    public void A_return_into_the_film_after_a_successful_reload_loads_the_file_once_more_paused_AC_05()
    {
        using var rig = PlayingRig();
        FailThePicture(rig);
        var loads = rig.Renderer.LoadCount;

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-3))).IsSuccess.ShouldBeTrue();

        rig.Renderer.LoadCount.ShouldBe(loads + 1, "a failure of the reloaded frame may still be queued behind this command, so the return always loads");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
    }

    [Fact]
    public void A_return_into_the_film_after_a_failed_reload_loads_the_file_AC_05()
    {
        using var rig = PlayingRig();
        rig.Renderer.LoadError = new Error("mpv.load_failed", "The file cannot be opened.");
        FailThePicture(rig);
        rig.Renderer.LoadError = null;
        var loads = rig.Renderer.LoadCount;

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-3))).IsSuccess.ShouldBeTrue();

        rig.Renderer.LoadCount.ShouldBe(loads + 1, "mpv holds no file after a failed reload, so the return loads it");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
    }

    [Fact]
    public void A_decode_failure_after_the_reload_returned_makes_the_next_return_load_the_file_again_AC_06b_AC_05()
    {
        using var rig = PlayingRig();
        FailThePicture(rig);
        var loads = rig.Renderer.LoadCount;
        var runs = rig.EngineRunFiles.Count;

        // mpv reports the reload as loaded on FILE_LOADED; the picture then fails to decode again.
        rig.Renderer.InjectDecodeFailure(DecodeError);
        rig.Flush();
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-3))).IsSuccess.ShouldBeTrue();

        rig.Renderer.LoadCount.ShouldBe(loads + 1, "the reloaded frame failed again, so mpv holds no file and the return loads it");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.EngineRunFiles.Count.ShouldBe(runs + 1);
    }

    [Fact]
    public void A_return_after_a_second_decode_failure_whose_load_fails_is_refused_and_starts_no_engine_run_AC_06b_AC_05()
    {
        using var rig = PlayingRig();
        FailThePicture(rig);
        var runs = rig.EngineRunFiles.Count;
        rig.Renderer.InjectDecodeFailure(DecodeError);
        rig.Flush();
        rig.Renderer.LoadError = new Error("mpv.load_failed", "The file cannot be opened.");

        var result = rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-3)));

        result.IsSuccess.ShouldBeFalse("the file could not be loaded, so the return must not start sound against an idle mpv");
        rig.EngineRunFiles.Count.ShouldBe(runs, "no engine run may start before the load succeeds");
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Fact]
    public void The_published_failure_carries_the_renderer_error_code_and_reason_AC_06b()
    {
        using var rig = PlayingRig();

        FailThePicture(rig);

        var failed = rig.Events.OfType<PictureFailed>().ShouldHaveSingleItem();
        failed.Code.ShouldBe("video.decode_failed");
        failed.Reason.ShouldBe(DecodeError.Message);
    }

    [Fact]
    public void A_reload_that_worked_is_published_after_the_failure_as_reloaded_at_the_last_presented_position_AC_06b()
    {
        using var rig = PlayingRig();

        FailThePicture(rig);

        var events = rig.Events.ToList();
        var reload = events.OfType<PictureReloadFinished>().ShouldHaveSingleItem();
        reload.Outcome.ShouldBe(PictureReloadOutcome.Reloaded);
        var failed = events.OfType<PictureFailed>().Single();
        reload.Position.ShouldBe(failed.Clock, TimeSpan.FromSeconds(1), "the reloaded frame is at the last presented position");
        events.IndexOf(reload).ShouldBeGreaterThan(events.IndexOf(failed));
    }

    [Fact]
    public void A_reload_the_renderer_refused_is_published_as_failed_AC_06b()
    {
        using var rig = PlayingRig();
        rig.Renderer.LoadError = new Error("mpv.load_failed", "The file cannot be opened.");

        FailThePicture(rig);

        rig.Events.OfType<PictureReloadFinished>().ShouldHaveSingleItem().Outcome.ShouldBe(PictureReloadOutcome.Failed);
    }

    [Fact]
    public void A_reload_that_used_its_whole_bound_is_published_as_timed_out_AC_06b()
    {
        using var rig = PlayingRig();
        rig.Renderer.LoadTakes = TimeSpan.FromSeconds(15);

        FailThePicture(rig);

        rig.Events.OfType<PictureReloadFinished>().ShouldHaveSingleItem().Outcome.ShouldBe(PictureReloadOutcome.TimedOut);
    }

    [Fact]
    public void A_reload_that_failed_with_a_load_error_after_the_whole_bound_is_published_as_failed_not_timed_out_AC_06b()
    {
        using var rig = PlayingRig();
        rig.Renderer.LoadTakes = TimeSpan.FromSeconds(3);
        rig.Renderer.LoadError = new Error("mpv.load_failed", "The file cannot be opened.");

        FailThePicture(rig);

        rig.Events.OfType<PictureReloadFinished>().ShouldHaveSingleItem().Outcome.ShouldBe(PictureReloadOutcome.Failed);
    }

    [Fact]
    public void A_reload_the_renderer_reports_as_load_timeout_is_published_as_timed_out_even_when_no_time_passed_AC_06b()
    {
        using var rig = PlayingRig();
        rig.Renderer.LoadError = new Error("mpv.load_timeout", "mpv did not open the file in time.");

        FailThePicture(rig);

        rig.Events.OfType<PictureReloadFinished>().ShouldHaveSingleItem().Outcome.ShouldBe(PictureReloadOutcome.TimedOut);
    }

    [Fact]
    public void A_decode_failure_of_the_reloaded_frame_is_published_as_a_reload_that_failed_again_AC_06b()
    {
        using var rig = PlayingRig();
        FailThePicture(rig);

        rig.Renderer.InjectDecodeFailure(DecodeError);
        rig.Flush();

        var outcomes = rig.Events.OfType<PictureReloadFinished>().Select(r => r.Outcome).ToList();
        outcomes.ShouldBe([PictureReloadOutcome.Reloaded, PictureReloadOutcome.FailedAgain]);
    }
}
