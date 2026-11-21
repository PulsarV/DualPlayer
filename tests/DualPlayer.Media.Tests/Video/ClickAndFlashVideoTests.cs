using System.Globalization;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Audio;
using DualPlayer.Media.Tests.Generated;

namespace DualPlayer.Media.Tests.Video;

/// <summary>
/// AC-16 happy path, the part a computer can check: a click-and-flash video made with the filter graph of
/// <c>build/make-click-flash-video.ps1</c> (a black picture with one white frame at the start of every 1001 ms and, on each
/// of two audio tracks, a 5 ms 2 kHz click at the same instants), cut to 4 seconds. The flashes are read from the decoded
/// picture's brightness with FFmpeg's <c>signalstats</c> filter (the decoders in the product never expose pixels); the clicks
/// are read from the audio each track decodes to with the product's own <see cref="FfmpegAudioDecoder"/>.
/// </summary>
public sealed class ClickAndFlashVideoTests
{
    private const double FrameSeconds = 1001.0 / 24000.0;
    private const int SampleRate = 48_000;

    // The graphs of the script, verbatim.
    private const string Picture = "color=c=black:s=1920x1080:r=24000/1001,drawbox=x=0:y=0:w=iw:h=ih:color=white:t=fill:enable='lt(mod(n\\,24)\\,1)'";
    private const string Click = "aevalsrc='if(lt(mod(t\\,1001/1000)\\,0.005)\\,0.8*sin(2*PI*2000*t)\\,0)':s=48000:c=stereo";

    private static readonly double[] ExpectedSeconds = [0, 1.001, 2.002, 3.003];

    [Fact]
    public void The_flash_and_the_click_of_every_audio_track_fall_on_the_same_instant_once_per_second_AC_16()
    {
        using var media = GeneratedMedia.CreateOrSkip();
        var film = media.Make(
            "click-flash.mkv",
            "-f", "lavfi", "-i", Picture,
            "-f", "lavfi", "-i", Click,
            "-f", "lavfi", "-i", Click,
            "-t", "4",
            "-map", "0:v", "-map", "1:a", "-map", "2:a",
            "-c:v", "libx264", "-profile:v", "high", "-pix_fmt", "yuv420p", "-g", "24", "-preset", "veryfast",
            "-c:a", "aac", "-b:a", "192k");

        var flashes = FlashTimes(media, film);
        var clicksOnTrack1 = ClickTimes(film, track: 1);
        var clicksOnTrack2 = ClickTimes(film, track: 2);

        // One flash and one click per 1.001 s in 4 s: four of each, at the same timestamps.
        flashes.Count.ShouldBe(4, $"flashes at {string.Join(", ", flashes)}");
        clicksOnTrack1.Count.ShouldBe(4, $"clicks on track 1 at {string.Join(", ", clicksOnTrack1)}");
        clicksOnTrack2.Count.ShouldBe(4, $"clicks on track 2 at {string.Join(", ", clicksOnTrack2)}");
        for (var i = 0; i < ExpectedSeconds.Length; i++)
        {
            flashes[i].ShouldBe(ExpectedSeconds[i], FrameSeconds, $"flash {i + 1}");
            clicksOnTrack1[i].ShouldBe(flashes[i], FrameSeconds, $"click {i + 1} of track 1 against its flash");
            clicksOnTrack2[i].ShouldBe(flashes[i], FrameSeconds, $"click {i + 1} of track 2 against its flash");
            clicksOnTrack2[i].ShouldBe(clicksOnTrack1[i], 0.001, $"click {i + 1}: the two tracks agree");
        }
    }

    // Presentation time of every picture whose mean brightness is above mid-grey (the script's white frames).
    private static List<double> FlashTimes(GeneratedMedia media, string film)
    {
        var report = media.Run("-i", film, "-an", "-vf", "signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=-", "-f", "null", "-");
        var flashes = new List<double>();
        double? pts = null;
        foreach (var line in report.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            const string ptsKey = "pts_time:";
            const string yavgKey = "lavfi.signalstats.YAVG=";
            if (line.StartsWith("frame:", StringComparison.Ordinal))
            {
                pts = double.Parse(line[(line.IndexOf(ptsKey, StringComparison.Ordinal) + ptsKey.Length)..], CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith(yavgKey, StringComparison.Ordinal) && pts is { } at
                && double.Parse(line[yavgKey.Length..], CultureInfo.InvariantCulture) > 128)
            {
                flashes.Add(at);
            }
        }

        return flashes;
    }

    // Start of every burst of samples above 0.1 that follows at least half a second of quiet.
    private static List<double> ClickTimes(string film, int track)
    {
        var target = new MixFormat(SampleRate, 2, ChannelLayout.Stereo);
        var opened = new FfmpegAudioDecoder().Open(film, [new DecodeRequest(track, target)]);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        using var session = opened.Value;

        var clicks = new List<double>();
        long lastLoud = long.MinValue / 2;
        while (true)
        {
            var read = session.Read();
            if (!read.IsSuccess)
            {
                read.Error.Code.ShouldBe("playback.end_of_stream");
                return clicks;
            }

            var block = read.Value;
            for (var i = 0; i < block.Samples.Length; i += 2)
            {
                if (Math.Abs(block.Samples[i]) <= 0.1f)
                {
                    continue;
                }

                var frame = block.StartFrame + (i / 2);
                if (frame - lastLoud > SampleRate / 2)
                {
                    clicks.Add((double)frame / SampleRate);
                }

                lastLoud = frame;
            }
        }
    }
}
