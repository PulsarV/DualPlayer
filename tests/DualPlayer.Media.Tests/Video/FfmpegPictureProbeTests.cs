using System.Diagnostics;
using System.Runtime.Versioning;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Video;
using DualPlayer.Tests;

namespace DualPlayer.Media.Tests.Video;

public sealed class FfmpegPictureProbeTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static string Media(string name) => Path.Combine(AppContext.BaseDirectory, "TestMedia", name);

    private static FfmpegPictureProbe NewProbe() => new();

    [Theory]
    [InlineData(13)] // EACCES
    [InlineData(1)] // EPERM
    public void An_open_failure_of_eacces_or_eperm_is_reported_as_access_denied(int posixError)
    {
        var result = FfmpegPictureProbe.OpenFailure("/x/film.mkv", -posixError);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("playback.file_access_denied");
    }

    [Fact]
    public void Probe_reports_no_picture_for_a_file_with_audio_only()
    {
        var result = NewProbe().Probe(Media("mono.wav"), Limit, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("video.no_picture");
    }

    [Theory]
    [InlineData("not-media.txt")]
    [InlineData("empty.bin")]
    [InlineData("does-not-exist.mkv")]
    public void Probe_reports_unreadable_for_a_file_that_is_not_media_or_is_missing(string file)
    {
        var result = NewProbe().Probe(Media(file), Limit, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("playback.file_unreadable");
    }

    [Fact]
    public void Probe_reports_access_denied_when_the_operating_system_refuses_the_read()
    {
        // A real skip, not a silent pass: Windows has no POSIX file mode and root reads files whatever their mode.
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("File modes are POSIX; Windows would need an ACL to refuse the read.");
        }
        else if (Environment.UserName == "root")
        {
            Assert.Skip("root reads a file whatever its mode.");
        }
        else
        {
            ProbeAFileTheOsRefuses();
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void ProbeAFileTheOsRefuses()
    {
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
    public void Probe_returns_within_the_time_limit_with_probe_timeout_when_the_first_picture_is_not_in_time()
    {
        var clock = Stopwatch.StartNew();

        var result = NewProbe().Probe(Media("long-gop-ramp.mkv"), TimeSpan.FromTicks(1), TestContext.Current.CancellationToken);

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("video.probe_timeout");
    }

    [Fact]
    public void A_timeout_after_the_codec_is_read_names_the_codec_in_the_message_and_fields()
    {
        // The hook runs on the probe worker right after codec and profile are published, and holds it past the limit.
        var probe = new FfmpegPictureProbe { AfterFormatRead = () => Thread.Sleep(TimeSpan.FromSeconds(1)) };

        var result = probe.Probe(Media("long-gop-ramp.mkv"), TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("video.probe_timeout");
        result.Error.Message.ShouldContain("h264");
        result.Error.Fields.ShouldNotBeNull();
        result.Error.Fields["codec"].ShouldBe("h264");
        result.Error.Fields.ShouldContainKey("profile");
    }

    [Fact]
    public void A_file_whose_probe_worker_is_still_blocked_is_not_given_a_second_worker_however_often_it_is_probed_AC_09b()
    {
        using var release = new ManualResetEventSlim();
        var workers = 0;
        var probe = new FfmpegPictureProbe
        {
            AfterFormatRead = () =>
            {
                Interlocked.Increment(ref workers);
                release.Wait(TimeSpan.FromSeconds(30)); // a read stuck on a bad sector: ignores the token
            },
        };
        var path = Media("long-gop-ramp.mkv");
        var limit = TimeSpan.FromMilliseconds(300);

        var results = Enumerable.Range(0, 3).Select(_ => probe.Probe(path, limit, TestContext.Current.CancellationToken)).ToList();
        release.Set();

        results[0].Error!.Code.ShouldBe("video.probe_timeout");
        results.Skip(1).ShouldAllBe(r => !r.IsSuccess && r.Error!.Code == "video.probe_busy", "a later probe of a file whose worker is blocked must say so at once");
        workers.ShouldBe(1, "a press started another probe worker on a file whose first one was still blocked");
    }

    [Fact]
    public void A_file_is_busy_exactly_while_its_probe_worker_is_still_running_AC_09b()
    {
        using var release = new ManualResetEventSlim();
        var probe = new FfmpegPictureProbe { AfterFormatRead = () => release.Wait(TimeSpan.FromSeconds(30)) };
        var path = Media("long-gop-ramp.mkv");

        probe.IsBusy(path).ShouldBeFalse("a file never probed was reported busy");
        probe.Probe(path, TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken).Error!.Code.ShouldBe("video.probe_timeout");

        probe.IsBusy(path).ShouldBeTrue("a file whose worker is blocked was not reported busy");
        release.Set();
        SpinWait.SpinUntil(() => !probe.IsBusy(path), TimeSpan.FromSeconds(10)).ShouldBeTrue("the busy flag never cleared after the worker ended");
    }

    [Fact]
    public void Probe_reports_not_hardware_decodable_naming_codec_and_profile_for_a_picture_only_software_can_decode()
    {
        var result = NewProbe().Probe(Media("video-ffv1.mkv"), Limit, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("video.not_hardware_decodable");
        result.Error.Message.ShouldContain("ffv1");
        result.Error.Message.ShouldContain("profile");
    }

    [Fact]
    public void A_picture_only_software_can_decode_carries_its_codec_and_profile_as_fields_for_the_skipped_line()
    {
        var result = NewProbe().Probe(Media("video-ffv1.mkv"), Limit, TestContext.Current.CancellationToken);

        result.Error!.Fields.ShouldNotBeNull();
        result.Error.Fields["codec"].ShouldBe("ffv1");
        result.Error.Fields.ShouldContainKey("profile");
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void Probe_decodes_the_first_h264_picture_in_hardware_and_names_codec_and_size_on_macOS()
    {
        var result = NewProbe().Probe(Media("long-gop-ramp.mkv"), Limit, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Codec.ShouldBe("h264");
        result.Value.Width.ShouldBe(64);
        result.Value.Height.ShouldBe(48);
    }
}
