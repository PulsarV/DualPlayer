using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using DualPlayer.Media.Audio;
using DualPlayer.Media.Tests.Generated;
using DualPlayer.Media.Video;
using DualPlayer.Tests;

namespace DualPlayer.Media.Tests.Video;

/// <summary>
/// AC-09 and AC-09b on real files: the real <see cref="PlaybackSession"/> (over the engine rig and a fake renderer) checks the
/// neighbours of a film with the real <see cref="FfmpegAudioDecoder"/> and <see cref="FfmpegPictureProbe"/>, on samples
/// generated into a temp folder. Output 1 plays track 1 and output 2 plays track 2.
/// </summary>
public sealed class NeighbourSkipIntegrationTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig(string current)
    {
        var rig = new SessionRig(TwoDevices, null, 600, neighbourDecoder: new FfmpegAudioDecoder(), neighbourPictureProbe: new FfmpegPictureProbe());
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = current })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    private static string MakeSample(GeneratedMedia media, string kind, string name) => kind switch
    {
        "one-track" => media.MakeFilm(name, audioTracks: 1),
        "audio-only" => media.MakeAudioOnly(name, audioTracks: 2),
        "not-media" => WriteBytes(media.PathOf(name), "This is a note, not a film.\n"u8.ToArray()),
        "empty" => WriteBytes(media.PathOf(name), []),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string WriteBytes(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData("one-track", "track-missing", 2, 2)]
    [InlineData("audio-only", "no-picture", null, null)]
    [InlineData("not-media", "unreadable", null, null)]
    [InlineData("empty", "unreadable", null, null)]
    public void A_real_sample_that_cannot_play_is_skipped_with_its_own_reason_and_the_film_goes_on_AC_09_AC_09b(
        string kind,
        string reason,
        int? output,
        int? track)
    {
        using var media = GeneratedMedia.CreateOrSkip();
        var current = media.MakeFilm("Episode 1.mkv", audioTracks: 2);
        MakeSample(media, kind, "Episode 2.mkv");
        using var rig = PlayingRig(current);

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileSkipped>().ShouldHaveSingleItem().ShouldBeEquivalentTo(new FileSkipped("Episode 2.mkv", reason, output, track));
        rig.Events.OfType<NoNeighbour>().ShouldHaveSingleItem().Next.ShouldBeTrue();
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.EngineRunFiles.ShouldBe([current], "the film was restarted although no neighbour could play");
    }

    [Fact]
    public void Real_samples_skipped_on_one_press_end_in_one_combined_note_naming_the_last_and_the_count_AC_09_AC_09b()
    {
        using var media = GeneratedMedia.CreateOrSkip();
        var current = media.MakeFilm("Episode 1.mkv", audioTracks: 2);
        MakeSample(media, "one-track", "Episode 2.mkv");
        MakeSample(media, "audio-only", "Episode 3.mkv");
        MakeSample(media, "not-media", "Episode 4.mkv");
        MakeSample(media, "empty", "Episode 5.mkv");
        using var rig = PlayingRig(current);

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileSkipped>().Select(e => (e.File, e.Reason)).ShouldBe(
        [
            ("Episode 2.mkv", "track-missing"),
            ("Episode 3.mkv", "no-picture"),
            ("Episode 4.mkv", "unreadable"),
            ("Episode 5.mkv", "unreadable"),
        ]);
        rig.Events.OfType<NoteShown>().ShouldHaveSingleItem().Text.ShouldBe("No next file (skipped 4; last: Episode 5.mkv - file cannot be read)");
        rig.EngineRunFiles.ShouldBe([current]);
    }

    // The picture of the playable sample needs the real hardware decoder (VideoToolbox here), like the probe's own success test.
    [HardwareFact(HardwareOs.MacOS)]
    public void The_next_playable_real_sample_plays_after_the_unplayable_ones_were_skipped_AC_09_AC_09b()
    {
        using var media = GeneratedMedia.CreateOrSkip();
        var current = media.MakeFilm("Episode 1.mkv", audioTracks: 2);
        MakeSample(media, "one-track", "Episode 2.mkv");
        MakeSample(media, "audio-only", "Episode 3.mkv");
        MakeSample(media, "not-media", "Episode 4.mkv");
        var playable = media.MakeFilm("Episode 5.mkv", audioTracks: 2);
        using var rig = PlayingRig(current);

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();

        rig.Events.OfType<FileSkipped>().Select(e => e.Reason).ShouldBe(["track-missing", "no-picture", "unreadable"]);
        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(playable);
        rig.EngineRunFiles.ShouldBe([current, playable]);
        rig.Session.State.ShouldBe(PlaybackState.Playing);
    }
}
