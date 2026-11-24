using System.Globalization;
using System.Text.RegularExpressions;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Audio;
using DualPlayer.Media.Tests.Generated;

namespace DualPlayer.Media.Tests.Video;

/// <summary>
/// AC-16 happy path, the part a computer can check: a click-and-flash video made with the filter graphs and the codec
/// arguments of each encoding that <c>build/make-click-flash-video.ps1</c> itself holds (read from the script, found from the
/// repository root, so a change to the script changes what is checked; H.264 and HEVC, as T23 plays both): a black picture with one white frame at the start of every 1001 ms and, on each
/// of two audio tracks, a 5 ms 2 kHz click at the same instants, cut to 4 seconds. The flashes are read from the decoded
/// picture's brightness with FFmpeg's <c>signalstats</c> filter (the decoders in the product never expose pixels); the clicks
/// are read from the audio each track decodes to with the product's own <see cref="FfmpegAudioDecoder"/>.
/// </summary>
public sealed class ClickAndFlashVideoTests
{
    private const double FrameSeconds = 1001.0 / 24000.0;
    private const double ClickToFlashSeconds = 0.002; // sample-exact clicks: far inside the 45 ms budget (AC-16)
    private const int SampleRate = 48_000;

    private static readonly double[] ExpectedSeconds = [0, 1.001, 2.002, 3.003];

    /// <summary>The output file names of the script's <c>$encodings</c>, one theory case each.</summary>
    public static TheoryData<string> EncodingNames()
    {
        var names = new TheoryData<string>();
        foreach (var encoding in GeneratorScript.Read().Encodings)
        {
            names.Add(encoding.Name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void The_flash_and_the_click_of_every_audio_track_fall_on_the_same_instant_once_per_second_AC_16(string encodingName)
    {
        var script = GeneratorScript.Read();
        var encoding = script.Encodings.Single(e => e.Name == encodingName);
        using var media = GeneratedMedia.CreateOrSkip();
        var codec = encoding.Codec[1]; // after "-c:v"
        Assert.SkipUnless(
            media.Run("-encoders").Split('\n').Any(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() == codec),
            $"The local FFmpeg cannot write {codec}, so {encodingName} was not checked.");

        var film = media.Make(
            encodingName,
            [
                "-f", "lavfi", "-i", script.Picture,
                "-f", "lavfi", "-i", script.Click,
                "-f", "lavfi", "-i", script.Click,
                "-t", "4",
                "-map", "0:v", "-map", "1:a", "-map", "2:a",
                .. encoding.Codec,
                "-pix_fmt", "yuv420p", "-g", "24", "-preset", "veryfast",
                "-c:a", "aac", "-b:a", "192k",
            ]);

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
            clicksOnTrack1[i].ShouldBe(flashes[i], ClickToFlashSeconds, $"click {i + 1} of track 1 against its flash");
            clicksOnTrack2[i].ShouldBe(flashes[i], ClickToFlashSeconds, $"click {i + 1} of track 2 against its flash");
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

    /// <summary>What the test takes from <c>build/make-click-flash-video.ps1</c>: the two filter graphs and each encoding's codec arguments.</summary>
    private sealed record Encoding(string Name, string[] Codec);

    private sealed record GeneratorScript(string Picture, string Click, Encoding[] Encodings)
    {
        public static GeneratorScript Read()
        {
            var path = Path.Combine(RepositoryRoot(), "build", "make-click-flash-video.ps1");
            File.Exists(path).ShouldBeTrue($"the generator script is not at {path}");
            var text = File.ReadAllText(path);

            var encodings = Regex.Matches(text, @"@\{\s*Name\s*=\s*'(?<name>[^']+)'\s*;\s*Codec\s*=\s*@\((?<args>[^)]*)\)\s*\}")
                .Select(m => new Encoding(
                    m.Groups["name"].Value,
                    [.. Regex.Matches(m.Groups["args"].Value, "'([^']*)'").Select(a => a.Groups[1].Value)]))
                .ToArray();
            encodings.Length.ShouldBeGreaterThanOrEqualTo(2, "the script's $encodings (H.264 and HEVC) were not found");
            encodings.ShouldAllBe(e => e.Codec.Length >= 2 && e.Codec[0] == "-c:v");
            return new GeneratorScript(Assignment(text, "video"), Assignment(text, "click"), encodings);
        }

        // `$name = "..."` on one line; PowerShell's double-quoted string: a backtick escapes the next character, "" is one quote.
        private static string Assignment(string text, string name)
        {
            var match = Regex.Match(text, "^\\$" + name + "\\s*=\\s*\"(?<value>.*)\"\\s*$", RegexOptions.Multiline);
            match.Success.ShouldBeTrue($"the script has no ${name} assignment");
            var value = match.Groups["value"].Value;
            value.ShouldNotContain("$", customMessage: $"${name} uses a PowerShell variable; the test reads only literal graphs");
            return Regex.Replace(value.Replace("\"\"", "\""), "`(.)", "$1");
        }

        private static string RepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DualPlayer.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException("DualPlayer.slnx was not found above " + AppContext.BaseDirectory);
        }
    }
}
