using System.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Video;
using DualPlayer.Tests;

namespace DualPlayer.Media.Tests.Video;

public sealed class FfmpegPictureProbeTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static string Media(string name) => Path.Combine(AppContext.BaseDirectory, "TestMedia", name);

    private static FfmpegPictureProbe NewProbe() => new();

    [Fact]
    public void Probe_reports_no_picture_for_a_file_with_audio_only()
    {
        var result = NewProbe().Probe(Media("mono.wav"), Limit);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("video.no_picture");
    }

    [Theory]
    [InlineData("not-media.txt")]
    [InlineData("empty.bin")]
    [InlineData("does-not-exist.mkv")]
    public void Probe_reports_unreadable_for_a_file_that_is_not_media_or_is_missing(string file)
    {
        var result = NewProbe().Probe(Media(file), Limit);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("playback.file_unreadable");
    }

    [Fact]
    public void Probe_reports_access_denied_when_the_operating_system_refuses_the_read()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return;
        }

        var dir = Directory.CreateTempSubdirectory("probe-denied");
        var copy = Path.Combine(dir.FullName, "locked.mkv");
        File.Copy(Media("long-gop-ramp.mkv"), copy);
        File.SetUnixFileMode(copy, UnixFileMode.None);
        try
        {
            var result = NewProbe().Probe(copy, Limit);

            result.IsSuccess.ShouldBeFalse();
            result.Error!.Code.ShouldBe("playback.file_access_denied");
        }
        finally
        {
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            dir.Delete(true);
        }
    }

    [Fact]
    public void Probe_returns_within_the_time_limit_with_not_hardware_decodable_when_the_first_picture_is_not_in_time()
    {
        var clock = Stopwatch.StartNew();

        var result = NewProbe().Probe(Media("long-gop-ramp.mkv"), TimeSpan.FromTicks(1));

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("video.not_hardware_decodable");
    }

    [Fact]
    public void Probe_reports_not_hardware_decodable_naming_codec_and_profile_for_a_picture_only_software_can_decode()
    {
        var result = NewProbe().Probe(Media("video-ffv1.mkv"), Limit);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("video.not_hardware_decodable");
        result.Error.Message.ShouldContain("ffv1");
        result.Error.Message.ShouldContain("profile");
    }

    [Fact]
    public void A_picture_only_software_can_decode_carries_its_codec_and_profile_as_fields_for_the_skipped_line()
    {
        var result = NewProbe().Probe(Media("video-ffv1.mkv"), Limit);

        result.Error!.Fields.ShouldNotBeNull();
        result.Error.Fields["codec"].ShouldBe("ffv1");
        result.Error.Fields.ShouldContainKey("profile");
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void Probe_decodes_the_first_h264_picture_in_hardware_and_names_codec_and_size_on_macOS()
    {
        var result = NewProbe().Probe(Media("long-gop-ramp.mkv"), Limit);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Codec.ShouldBe("h264");
        result.Value.Width.ShouldBe(64);
        result.Value.Height.ShouldBe(48);
    }
}
