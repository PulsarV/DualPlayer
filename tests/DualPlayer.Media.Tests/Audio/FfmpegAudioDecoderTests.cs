using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Audio;

namespace DualPlayer.Media.Tests.Audio;

public sealed class FfmpegAudioDecoderTests
{
    private static string Media(string name) => Path.Combine(AppContext.BaseDirectory, "TestMedia", name);

    private static readonly MixFormat Stereo48 = new(48000, 2, ChannelLayout.Stereo);

    private static FfmpegAudioDecoder NewDecoder() => new();

    [Fact]
    public void ListTracks_numbers_audio_tracks_from_1_with_language_layout_and_rate()
    {
        var result = NewDecoder().ListTracks(Media("two-tracks.mkv"));

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Count.ShouldBe(2);
        result.Value[0].ShouldBe(new AudioTrackInfo(1, "ukr", ChannelLayout.Surround51, 48000));
        result.Value[1].ShouldBe(new AudioTrackInfo(2, "eng", ChannelLayout.Stereo, 44100));
    }

    [Fact]
    public void ListTracks_reports_a_mono_track_without_language_as_und()
    {
        var result = NewDecoder().ListTracks(Media("mono.wav"));

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Count.ShouldBe(1);
        result.Value[0].ShouldBe(new AudioTrackInfo(1, "und", ChannelLayout.Mono, 48000));
    }

    [Theory]
    [InlineData("not-media.txt")]
    [InlineData("empty.bin")]
    [InlineData("does-not-exist.mkv")]
    public void ListTracks_of_a_file_that_is_not_media_fails_with_file_unreadable_and_a_reason(string name)
    {
        var result = NewDecoder().ListTracks(Media(name));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("playback.file_unreadable");
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Open_of_a_file_that_is_not_media_fails_with_file_unreadable()
    {
        var result = NewDecoder().Open(Media("not-media.txt"), [new DecodeRequest(1, Stereo48)]);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("playback.file_unreadable");
    }

    [Fact]
    public void Open_decodes_two_tracks_in_one_pass_into_blocks_per_output()
    {
        var opened = NewDecoder().Open(
            Media("two-tracks.mkv"),
            [new DecodeRequest(1, Stereo48), new DecodeRequest(2, Stereo48)]);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        using var session = opened.Value;

        var seen = new HashSet<int>();
        var blocks = 0;
        while (blocks < 10_000)
        {
            var read = session.Read();
            if (!read.IsSuccess)
            {
                read.Error.Code.ShouldBe("playback.end_of_stream");
                break;
            }

            read.Value.Samples.Length.ShouldBeGreaterThan(0);
            seen.Add(read.Value.OutputIndex);
            blocks++;
        }

        blocks.ShouldBeLessThan(10_000, "the session must signal end of stream");
        seen.OrderBy(i => i).ShouldBe([0, 1]);
    }

    [Fact]
    public void Seek_tags_subsequent_blocks_with_the_new_epoch()
    {
        var opened = NewDecoder().Open(Media("two-tracks.mkv"), [new DecodeRequest(2, Stereo48)]);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        using var session = opened.Value;
        session.Read().IsSuccess.ShouldBeTrue();

        session.Seek(TimeSpan.FromMilliseconds(500), 7).IsSuccess.ShouldBeTrue();
        var read = session.Read();

        read.IsSuccess.ShouldBeTrue(read.Error?.Message);
        read.Value.Epoch.ShouldBe(7);
    }

    [Fact]
    public void Seek_past_the_end_reports_end_of_stream()
    {
        var opened = NewDecoder().Open(Media("two-tracks.mkv"), [new DecodeRequest(1, Stereo48)]);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        using var session = opened.Value;

        session.Seek(TimeSpan.FromMinutes(5), 3);
        var read = session.Read();

        read.IsSuccess.ShouldBeFalse();
        read.Error.Code.ShouldBe("playback.end_of_stream");
    }

    // long-gop-ramp.mkv: one video keyframe at 0 s; mono 48 kHz tracks whose sample value is the timeline
    // (track 1 = t/12, track 2 = 0.5*t/12), so a decoded sample says which content second it came from.
    private static readonly MixFormat Mono48 = new(48000, 1, ChannelLayout.Mono);

    private static IDecodeSession OpenLongGop()
    {
        var opened = NewDecoder().Open(
            Media("long-gop-ramp.mkv"),
            [new DecodeRequest(1, Mono48), new DecodeRequest(2, Mono48)]);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        return opened.Value;
    }

    private static DecodedBlock ReadBlockOf(IDecodeSession session, int output)
    {
        for (var i = 0; i < 1000; i++)
        {
            var read = session.Read();
            read.IsSuccess.ShouldBeTrue(read.Error?.Message);
            if (read.Value.OutputIndex == output)
            {
                return read.Value;
            }
        }

        throw new ShouldAssertException($"no block for output {output}");
    }

    [Theory]
    [InlineData(0, 12.0)]
    [InlineData(1, 24.0)]
    public void Seek_into_a_long_gop_starts_each_output_audio_at_the_target_not_at_the_video_keyframe(int output, double secondsPerUnit)
    {
        using var session = OpenLongGop();

        session.Seek(TimeSpan.FromSeconds(7), 2).IsSuccess.ShouldBeTrue();
        var block = ReadBlockOf(session, output);

        var contentSeconds = block.Samples[0] * secondsPerUnit;
        contentSeconds.ShouldBe(7.0, 0.1, "audio must start at the seek target, not at the keyframe at 0 s");
    }

    [Fact]
    public void Seek_into_a_long_gop_derives_block_start_frames_from_pts_and_keeps_them_contiguous()
    {
        using var session = OpenLongGop();

        session.Seek(TimeSpan.FromSeconds(7), 2).IsSuccess.ShouldBeTrue();
        var expectedNext = 7L * 48000;
        for (var i = 0; i < 20; i++)
        {
            var block = ReadBlockOf(session, 0);
            block.StartFrame.ShouldBe(expectedNext, $"block {i} must start where the previous one ended");

            // The ramp encodes the frame index; 16-bit quantisation of a 12 s ramp is ~18 frames.
            (block.Samples[0] * 12.0 * 48000).ShouldBe(block.StartFrame, 40.0, $"block {i} StartFrame must match its content");
            expectedNext += block.Samples.Length / Mono48.Channels;
        }
    }
}
