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
    [InlineData("mono.wav", ChannelLayout.Mono)]
    [InlineData("tones-2.1.wav", ChannelLayout.Other)]
    [InlineData("tones-6.0.wav", ChannelLayout.Other)]
    [InlineData("tones-5.1.wav", ChannelLayout.Surround51)]
    [InlineData("tones-7.1.wav", ChannelLayout.Surround71)]
    public void ListTracks_labels_a_source_5_1_or_7_1_only_when_it_has_an_LFE(string file, ChannelLayout layout)
    {
        var result = NewDecoder().ListTracks(Media(file));

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value[0].ChannelLayout.ShouldBe(layout);
    }

    [Theory]
    [InlineData("mono.wav", 1, false)]
    [InlineData("tones-2.1.wav", 3, true)]
    [InlineData("tones-6.0.wav", 6, false)]
    [InlineData("tones-5.1.wav", 6, true)]
    [InlineData("tones-7.1.wav", 8, true)]
    public void ListTracks_reports_the_channel_count_and_whether_the_source_has_an_LFE(string file, int channels, bool hasLfe)
    {
        var result = NewDecoder().ListTracks(Media(file));

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value[0].Channels.ShouldBe(channels);
        result.Value[0].HasLfe.ShouldBe(hasLfe);
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

    // ramp-offset.ts / .mkv: 6 s mono 48 kHz ramp (value = t/6) in containers whose timestamps start above 0
    // (TS start_time 1.4 s, MKV 600 s). Content time is relative to the stream start.
    private static IDecodeSession OpenRamp(string name)
    {
        var opened = NewDecoder().Open(Media(name), [new DecodeRequest(1, Mono48)]);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        return opened.Value;
    }

    [Theory]
    [InlineData("ramp-offset.ts")]
    [InlineData("ramp-offset.mkv")]
    public void Open_of_a_file_whose_timestamps_start_above_zero_starts_the_first_block_at_frame_zero(string name)
    {
        using var session = OpenRamp(name);

        var block = ReadBlockOf(session, 0);

        block.StartFrame.ShouldBeInRange(0L, 2048L, "the first block must start at content time 0, not at the container offset");
        (block.Samples[0] * 6.0).ShouldBe(0.0, 0.1, "the first block must carry content from time 0");
    }

    [Theory]
    [InlineData("ramp-offset.ts")]
    [InlineData("ramp-offset.mkv")]
    public void Duration_of_a_file_whose_timestamps_start_above_zero_excludes_the_start_offset(string name)
    {
        using var session = OpenRamp(name);

        session.Duration.ShouldNotBeNull();
        session.Duration.Value.TotalSeconds.ShouldBe(6.0, 0.2);
    }

    [Theory]
    [InlineData("ramp-offset.ts")]
    [InlineData("ramp-offset.mkv")]
    public void Seek_in_a_file_whose_timestamps_start_above_zero_lands_on_the_target_content_time(string name)
    {
        using var session = OpenRamp(name);

        session.Seek(TimeSpan.FromSeconds(3), 2).IsSuccess.ShouldBeTrue();
        var block = ReadBlockOf(session, 0);

        block.StartFrame.ShouldBeInRange(3L * 48000 - 2048, 3L * 48000 + 2048);
        (block.Samples[0] * 6.0).ShouldBe(3.0, 0.1, "audio must start at the seek target, not offset early");
    }

    [Fact]
    public void Seek_just_before_the_end_of_a_file_whose_timestamps_start_above_zero_still_yields_audio()
    {
        using var session = OpenRamp("ramp-offset.ts");

        session.Seek(TimeSpan.FromSeconds(5.5), 2).IsSuccess.ShouldBeTrue();
        var read = session.Read();

        read.IsSuccess.ShouldBeTrue("a seek 0.5 s before the end is not past the end: " + read.Error?.Message);
        read.Value.StartFrame.ShouldBeInRange(5L * 48000 + 24000 - 2048, 5L * 48000 + 24000 + 2048);
        (read.Value.Samples[0] * 6.0).ShouldBe(5.5, 0.1, "audio must start at the seek target, not offset early");
    }

    // ramp-offset-span.mkv: 10 s mono 48 kHz ramp (value = t/10) at +3 s whose Duration element holds the span (10 s),
    // as mkvmerge writes it, instead of the end timestamp (13 s) FFmpeg's muxer writes.
    [Fact]
    public void Duration_of_a_matroska_file_whose_duration_element_is_the_span_is_the_span()
    {
        using var session = OpenRamp("ramp-offset-span.mkv");

        session.Duration.ShouldNotBeNull();
        session.Duration.Value.TotalSeconds.ShouldBe(10.0, 0.2);
    }

    [Theory]
    [InlineData(7.0)]
    [InlineData(9.0)]
    [InlineData(9.5)]
    public void Seek_near_the_end_of_a_matroska_file_whose_duration_element_is_the_span_yields_audio_at_the_target(double seconds)
    {
        using var session = OpenRamp("ramp-offset-span.mkv");

        session.Seek(TimeSpan.FromSeconds(seconds), 2).IsSuccess.ShouldBeTrue();
        var read = session.Read();

        read.IsSuccess.ShouldBeTrue($"a seek to {seconds} s of a 10 s file is not past the end: " + read.Error?.Message);
        read.Value.StartFrame.ShouldBeInRange((long)(seconds * 48000) - 2048, (long)(seconds * 48000) + 2048);
        (read.Value.Samples[0] * 10.0).ShouldBe(seconds, 0.1, "audio must start at the seek target");
    }

    [Theory]
    [InlineData("ramp-offset.mkv", 6.5)]
    [InlineData("ramp-offset-span.mkv", 10.5)]
    public void Seek_past_the_end_of_a_file_yields_end_of_stream(string name, double seconds)
    {
        using var session = OpenRamp(name);

        session.Seek(TimeSpan.FromSeconds(seconds), 2).IsSuccess.ShouldBeTrue();

        session.Read().Error?.Code.ShouldBe("playback.end_of_stream");
    }

    [Fact]
    public void Open_when_the_restore_seek_after_the_duration_probe_fails_plays_from_the_start()
    {
        var decoder = new FfmpegAudioDecoder { RewindSeekHook = () => -1 };
        var opened = decoder.Open(Media("ramp-offset-span.mkv"), [new DecodeRequest(1, Mono48)]);

        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);

        using var session = opened.Value;
        var read = session.Read();

        read.IsSuccess.ShouldBeTrue("a failed restore seek must not end the stream before playing: " + read.Error?.Message);
        read.Value.StartFrame.ShouldBeInRange(0L, 2048L);
        (read.Value.Samples[0] * 10.0).ShouldBe(0.0, 0.1, "the first block must carry content from time 0");
    }

    // RewindSeekHook = -1 fails every seek back to the start (none is made). ramp-offset.mkv's Duration is its end
    // timestamp, so the duration probe reads to end of file and the demuxer sits at EOF. The seam models a failed
    // seek (nothing is rewound); the reopen must put the demuxer back at the start so the first Read is not EOF.
    [Fact]
    public void Open_when_every_rewind_seek_fails_with_the_demuxer_at_end_of_file_plays_from_content_zero()
    {
        var decoder = new FfmpegAudioDecoder { RewindSeekHook = () => -1 };
        var opened = decoder.Open(Media("ramp-offset.mkv"), [new DecodeRequest(1, Mono48)]);

        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);

        using var session = opened.Value;
        var read = session.Read();

        read.IsSuccess.ShouldBeTrue("a failed rewind must not leave the demuxer at end of stream: " + read.Error?.Message);
        read.Value.StartFrame.ShouldBeInRange(0L, 2048L);
        (read.Value.Samples[0] * 6.0).ShouldBe(0.0, 0.1, "the first block must carry content from time 0");
        session.Duration.ShouldNotBeNull().TotalSeconds.ShouldBe(6.0, 0.1);
    }

    // The reopen itself fails: the hook fails the rewind seek and empties the file (truncation, not deletion, so it also
    // works on Windows where an open file cannot be deleted), so the reopen finds no streams.
    [Fact]
    public void Open_when_the_restore_seek_fails_and_the_file_cannot_be_reopened_fails_as_file_unreadable()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"dualplayer-{Guid.NewGuid():N}.mkv");
        File.Copy(Media("ramp-offset-span.mkv"), tmp);
        try
        {
            var decoder = new FfmpegAudioDecoder
            {
                RewindSeekHook = () =>
                {
                    using var fs = new FileStream(tmp, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    return -1;
                },
            };

            var opened = decoder.Open(tmp, [new DecodeRequest(1, Mono48)]);

            opened.IsSuccess.ShouldBeFalse();
            opened.Error?.Code.ShouldBe("playback.file_unreadable");
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
