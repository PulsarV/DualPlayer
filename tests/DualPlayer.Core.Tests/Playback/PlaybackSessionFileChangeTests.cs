using DualPlayer.Core.Outputs;
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

    private static FakeAudioBackend Backend(SessionRig rig) => (FakeAudioBackend)rig.Engine.Backend;

    [Fact]
    public void A_device_list_renumbered_between_files_keeps_each_track_on_its_device_AC_07_AC_12()
    {
        using var rig = PlayingRig("Episode 1.mkv", "Episode 2.mkv");
        rig.Events.OfType<LatencyEstimated>().Single().Estimate.Outputs.Select(o => o.DeviceId).ShouldBe(["dev-1", "dev-2"]);

        // A device plugged in whose id sorts first: the devices the command named by number 1 and 2 are now 2 and 3.
        Backend(rig).InsertDevice(0, new AudioDeviceInfo("Plugged in", null, "dev-0"));
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();

        rig.Events.OfType<LatencyEstimated>().Last().Estimate.Outputs.Select(o => o.DeviceId).ShouldBe(["dev-1", "dev-2"]);
    }

    [Fact]
    public void Next_file_after_an_output_was_lost_plays_on_the_outputs_still_connected_with_the_same_output_numbers_AC_07_AC_15()
    {
        using var rig = PlayingRig("Episode 1.mkv", "Episode 2.mkv");
        rig.Engine.Devices[0].SimulateLoss();
        Backend(rig).RemoveDevice("dev-1");
        rig.Engine.Barrier();
        rig.Events.OfType<OutputLost>().ShouldHaveSingleItem().OutputNumber.ShouldBe(1);

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        Path.GetFileName(rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File).ShouldBe("Episode 2.mkv");
        rig.Engine.Call(() => rig.Session.ChangeOffset(2, 10)).IsSuccess.ShouldBeTrue();
        rig.Events.OfType<OffsetChanged>().ShouldHaveSingleItem().DeviceId.ShouldBe("dev-2");
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        rig.Events.OfType<OffsetChanged>().Count().ShouldBe(1, "output 1 is lost: its offset key does nothing");
    }

    [Fact]
    public void A_file_change_whose_start_fails_ends_the_film_with_a_note_instead_of_going_silent_AC_07()
    {
        using var rig = PlayingRig("Episode 1.mkv", "Episode 2.mkv");
        rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse();

        rig.Session.State.ShouldBe(PlaybackState.Ended);
        rig.Events.OfType<NoteShown>().ShouldContain(n => n.Style == NoteStyle.Warning && n.Text.Contains("Episode 2.mkv"));
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue("keys after a failed file change must not answer 'no film is playing'");
    }

    [Fact]
    public void A_move_to_the_previous_file_says_so_and_a_skip_for_the_picture_names_codec_and_profile_AC_08_AC_09b()
    {
        using var rig = PlayingRig("Episode 3.mkv", "Episode 1.mkv", "Episode 2.mkv");
        rig.Pictures.Script(Path.Combine(_folder, "Episode 2.mkv"), Result<PictureInfo>.Failure(
            new Error("video.not_hardware_decodable", "The picture format hevc (profile Main 10) cannot be decoded in hardware on this PC.")
            {
                Fields = new Dictionary<string, string> { ["codec"] = "hevc", ["profile"] = "Main 10" },
            }));

        rig.Engine.Call(() => rig.Session.PreviousFile()).IsSuccess.ShouldBeTrue();

        var skipped = rig.Events.OfType<FileSkipped>().ShouldHaveSingleItem();
        skipped.Codec.ShouldBe("hevc");
        skipped.Profile.ShouldBe("Main 10");
        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().ToPrevious.ShouldBeTrue();
    }

    [Fact]
    public void A_picture_failure_of_the_old_film_raised_during_the_folder_scan_does_not_end_the_new_film_AC_06b_AC_07()
    {
        using var rig = PlayingRig("Episode 1.mkv", "Episode 2.mkv");
        var raised = 0;
        rig.Decoder.BeforeList = () =>
        {
            if (raised++ == 0)
            {
                // The old film is still playing during the scan: its renderer fails now, the session is busy with the command.
                rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "The old picture cannot be decoded."));
            }
        };

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(50);
        rig.Flush();

        raised.ShouldBeGreaterThan(0, "the failure was never raised during the scan");
        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem();
        rig.Events.OfType<PictureFailed>().ShouldBeEmpty("a failure of the old film ended the new film");
        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Renderer.IsPlaying.ShouldBeTrue();
    }
}
