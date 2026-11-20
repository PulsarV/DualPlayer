using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

public sealed class PlaybackSessionFileChangeTests : IDisposable
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-filechange-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    // Output 1 plays track 1 on dev-1, output 2 plays track 2 on dev-2.
    private SessionRig PlayingRig(string current, params string[] others)
    {
        foreach (var other in others)
        {
            File(other);
        }

        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File(current) })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void Next_file_plays_the_following_file_in_natural_order_on_the_same_outputs_AC_07()
    {
        using var rig = PlayingRig("Episode 2.mkv", "Episode 1.mkv", "Episode 10.mkv", "notes.txt");

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();

        var changed = rig.Events.OfType<FileChanged>().ShouldHaveSingleItem();
        Path.GetFileName(changed.File).ShouldBe("Episode 10.mkv");
        Path.GetFileName(changed.PreviousFile!).ShouldBe("Episode 2.mkv");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Engine.Summary.ShouldNotBeNull();
        rig.Events.OfType<LatencyEstimated>().Count().ShouldBe(2);
        rig.Pictures.Probed.Select(Path.GetFileName).ShouldBe(["Episode 10.mkv"]);
    }

    [Fact]
    public void Previous_file_plays_the_preceding_file_AC_07()
    {
        using var rig = PlayingRig("Episode 2.mkv", "Episode 1.mkv", "Episode 10.mkv");

        rig.Engine.Call(() => rig.Session.PreviousFile()).IsSuccess.ShouldBeTrue();

        Path.GetFileName(rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File).ShouldBe("Episode 1.mkv");
    }

    [Fact]
    public void Next_file_while_paused_plays_the_new_file_AC_07()
    {
        using var rig = PlayingRig("Episode 1.mkv", "Episode 2.mkv");
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();

        Path.GetFileName(rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File).ShouldBe("Episode 2.mkv");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
    }

    [Fact]
    public void Next_file_with_no_following_file_leaves_the_film_and_shows_a_note_AC_08()
    {
        using var rig = PlayingRig("Episode 2.mkv", "Episode 1.mkv");

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<NoNeighbour>().ShouldHaveSingleItem().Next.ShouldBeTrue();
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "No next file");
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Engine.Summary.ShouldBeNull();
    }

    [Fact]
    public void Previous_file_with_no_preceding_file_keeps_a_paused_film_paused_AC_08()
    {
        using var rig = PlayingRig("Episode 1.mkv", "Episode 2.mkv");
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();

        rig.Engine.Call(() => rig.Session.PreviousFile());

        rig.Events.OfType<NoNeighbour>().ShouldHaveSingleItem().Next.ShouldBeFalse();
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "No previous file");
        rig.Session.State.ShouldBe(PlaybackState.Paused);
    }

    [Fact]
    public void A_file_lacking_an_outputs_track_is_skipped_and_the_next_playable_one_plays_AC_09()
    {
        using var rig = PlayingRig("Episode-01.mkv", "Episode-02.mkv", "Episode-03.mkv");
        rig.Decoder.Script("Episode-02.mkv", 1);

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileSkipped>().ShouldHaveSingleItem().ShouldBe(new FileSkipped("Episode-02.mkv", "track-missing", 2, 2));
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "Skipped Episode-02.mkv: no track 2 for output 2");
        Path.GetFileName(rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File).ShouldBe("Episode-03.mkv");
    }

    [Fact]
    public void A_file_whose_picture_cannot_be_decoded_in_hardware_is_skipped_with_its_reason_AC_09b()
    {
        using var rig = PlayingRig("Episode-01.mkv", "Episode-02.mkv", "Episode-03.mkv");
        rig.Pictures.Fail(Path.Combine(_folder, "Episode-02.mkv"), "video.not_hardware_decodable", "no");

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileSkipped>().ShouldHaveSingleItem().ShouldBeEquivalentTo(new FileSkipped("Episode-02.mkv", "not-hardware-decodable", null, null));
        Path.GetFileName(rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File).ShouldBe("Episode-03.mkv");
    }

    [Fact]
    public void Several_skipped_files_end_in_one_note_naming_the_last_and_the_count_AC_09b()
    {
        using var rig = PlayingRig("Episode-01.mkv", "Episode-02.mkv", "Episode-03.mkv", "Episode-04.mkv", "Episode-05.mkv");
        rig.Pictures.Fail(Path.Combine(_folder, "Episode-02.mkv"), "playback.file_unreadable", "no");
        rig.Decoder.Script("Episode-03.mkv", 1);
        rig.Pictures.Fail(Path.Combine(_folder, "Episode-04.mkv"), "video.not_hardware_decodable", "no");

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileSkipped>().Select(e => e.Reason).ShouldBe(["unreadable", "track-missing", "not-hardware-decodable"]);
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "Skipped 3 files; last: Episode-04.mkv - picture cannot be played on this PC");
        Path.GetFileName(rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File).ShouldBe("Episode-05.mkv");
    }

    [Fact]
    public void When_every_following_file_is_skipped_the_film_stays_and_no_next_file_is_reported_AC_09()
    {
        using var rig = PlayingRig("Episode-01.mkv", "Episode-02.mkv");
        rig.Decoder.Script("Episode-02.mkv", 1);

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileSkipped>().ShouldHaveSingleItem();
        rig.Events.OfType<NoNeighbour>().ShouldHaveSingleItem();
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Engine.Summary.ShouldBeNull();
    }
}
