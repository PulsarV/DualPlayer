using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T40: a failed file change or a failed return into the film leaves a usable ended session.</summary>
public sealed class PlaybackSessionEndedRecoveryTests : IDisposable
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-endedrecovery-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    // Plays Episode 2 of three on two outputs, rendered for the given number of 10 ms steps.
    private SessionRig PlayingRig(int steps)
    {
        File("Episode 1.mkv");
        File("Episode 3.mkv");
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode 2.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(steps);
        return rig;
    }

    private static IEnumerable<string> RunFileNames(SessionRig rig) => rig.EngineRunFiles.Select(Path.GetFileName)!;

    [Fact]
    public void After_a_failed_file_change_a_backward_seek_starts_no_audio_for_the_file_that_did_not_load_AC_05_AC_07()
    {
        using var rig = PlayingRig(300);
        rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse();
        rig.Session.State.ShouldBe(PlaybackState.Ended);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));

        RunFileNames(rig).ShouldNotContain("Episode 3.mkv", "the picture of Episode 3 never loaded, so no audio may play for it");
    }

    [Fact]
    public void After_a_failed_file_change_next_and_previous_file_still_answer_AC_07()
    {
        using var rig = PlayingRig(300);
        rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse();
        rig.Renderer.LoadError = null;

        rig.Engine.Call(() => rig.Session.PreviousFile()).IsSuccess.ShouldBeTrue();

        // FileChanged is published once a target is chosen (T42, cli.md §2.2), so the failed next has one too.
        var changes = rig.Events.OfType<FileChanged>().ToList();
        changes.Count.ShouldBe(2);
        changes[0].ToPrevious.ShouldBeFalse();
        changes[1].ToPrevious.ShouldBeTrue();
        RunFileNames(rig).Last().ShouldBe(Path.GetFileName(changes[1].File), "the previous file is the one that now plays");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
    }

    [Fact]
    public void A_stale_run_summary_after_a_failed_file_change_does_not_move_the_end_position_used_by_a_backward_seek_AC_05()
    {
        using var rig = PlayingRig(2_000);
        // Output 2 leaves the list: the change fails with outputs.device_not_found before the new picture loads, so the
        // old film stays loaded and its run is still the one ← returns into.
        rig.Engine.Unlist(2);
        rig.Engine.Call(() => rig.Session.NextFile()).Error!.Code.ShouldBe("outputs.device_not_found");
        rig.Engine.Barrier(); // the old run's RunSummary(FileChange) has now reached the session
        rig.Engine.Events.OfType<RunSummary>().ShouldContain(s => s.EndReason == RunEndReason.FileChange);
        rig.Engine.Relist(2);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));

        // The failed change set the end position to 0; the old film's 20 s must not come back through its late summary.
        rig.EngineStartPositions.Count.ShouldBe(2, "the first start and the return into the old film");
        rig.EngineStartPositions.Last().ShouldBeLessThan(TimeSpan.FromSeconds(1), "a seek back from an end position of 0 lands at 0, not near 10 s");
    }

    // Both outputs are lost one after the other, which ends the film (AC-15b); the devices are still listed, so they can be tried again.
    private static void LoseEveryOutput(SessionRig rig)
    {
        rig.Engine.Devices[1].SimulateLoss();
        rig.Engine.Barrier();
        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.WaitFor(() => rig.Engine.Summary is not null).ShouldBeTrue();
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Fact]
    public void A_backward_seek_after_the_last_output_was_lost_that_cannot_return_keeps_the_session_ended_with_a_note_AC_15b()
    {
        using var rig = PlayingRig(300);
        LoseEveryOutput(rig);
        rig.Engine.Unlist(1); // no device is back, so the retry finds none (T49)
        rig.Engine.Unlist(2);
        var notesBefore = rig.Events.OfType<NoteShown>().Count();

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));

        rig.Session.State.ShouldBe(PlaybackState.Ended, "a failed return into the film leaves the film ended, not idle");
        rig.Events.OfType<NoteShown>().Count().ShouldBeGreaterThan(notesBefore, "the failed return says why");
    }

    [Fact]
    public void After_the_last_output_was_lost_and_a_failed_backward_seek_next_file_plays_AC_15b_AC_07()
    {
        using var rig = PlayingRig(300);
        LoseEveryOutput(rig);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue("Page Down still plays the next file");

        Path.GetFileName(rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File).ShouldBe("Episode 3.mkv");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
        RunFileNames(rig).Last().ShouldBe("Episode 3.mkv");
    }

    [Fact]
    public void A_backward_seek_after_every_output_was_lost_and_a_device_is_back_plays_from_the_end_position_on_it_AC_15b_AC_05()
    {
        using var rig = PlayingRig(2_000);
        LoseEveryOutput(rig);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue("the devices are listed again, so the return retries them");

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.EngineStartPositions.Count.ShouldBe(2, "the first start and the return into the film");
        rig.EngineStartPositions.Last().ShouldBeInRange(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12), "20 s end position minus 10 s");
    }

    private static void FailThePicture(SessionRig rig)
    {
        rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "The picture cannot be decoded."));
        rig.Engine.WaitFor(() => rig.Engine.Summary is not null).ShouldBeTrue();
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Fact]
    public void A_backward_seek_after_a_picture_failure_reloads_the_picture_before_any_engine_run_AC_06b_AC_05()
    {
        using var rig = PlayingRig(1_000);
        FailThePicture(rig);
        var loadsBefore = rig.Renderer.LoadCount;
        var runsAtReload = -1;
        rig.Renderer.AfterLoad = () => runsAtReload = rig.EngineRunFiles.Count;

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-5))).IsSuccess.ShouldBeTrue();

        rig.Renderer.LoadCount.ShouldBe(loadsBefore + 1, "the picture failure left mpv without a file");
        runsAtReload.ShouldBe(1, "the reload comes before the second engine run");
        rig.EngineStartPositions.Count.ShouldBe(2);
        rig.Session.State.ShouldBe(PlaybackState.Playing);
    }

    [Fact]
    public void A_backward_seek_after_a_picture_failure_whose_reload_fails_ends_with_a_note_and_starts_no_audio_AC_06b_AC_05()
    {
        using var rig = PlayingRig(1_000);
        FailThePicture(rig);
        rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");
        var notesBefore = rig.Events.OfType<NoteShown>().Count();

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-5))).IsSuccess.ShouldBeFalse();

        rig.Session.State.ShouldBe(PlaybackState.Ended);
        rig.EngineStartPositions.Count.ShouldBe(1, "no audio without a picture");
        rig.Events.OfType<NoteShown>().Count().ShouldBeGreaterThan(notesBefore);
    }
}
