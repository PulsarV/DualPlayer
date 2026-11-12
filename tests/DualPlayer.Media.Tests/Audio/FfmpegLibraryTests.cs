using DualPlayer.Media.Audio;

namespace DualPlayer.Media.Tests.Audio;

public sealed class FfmpegLibraryTests
{
    [Fact]
    public void Load_finds_FFmpeg_and_its_major_version_matches_the_pinned_FFmpeg_AutoGen()
    {
        var result = FfmpegLibrary.Load();

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.MajorVersion.ShouldBe(FfmpegLibrary.ExpectedMajorVersion);
    }

    [Fact]
    public void Load_reports_version_and_license_text_for_logging()
    {
        var result = FfmpegLibrary.Load();

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Version.ShouldNotBeNullOrWhiteSpace();
        result.Value.License.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Load_from_a_folder_without_FFmpeg_returns_a_failure_naming_the_expected_location()
    {
        var missing = Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N"));

        var result = FfmpegLibrary.Load(missing);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.Message.ShouldContain(missing);
    }

    [Fact]
    public void Load_from_another_folder_after_FFmpeg_is_loaded_returns_a_failure_naming_that_folder()
    {
        FfmpegLibrary.Load().IsSuccess.ShouldBeTrue();
        var other = CreateFolderWithFakeAvUtil();

        var result = FfmpegLibrary.Load(other);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.Message.ShouldContain(other);
    }

    [Fact]
    public void A_failed_load_from_a_broken_folder_does_not_block_a_later_load_from_the_right_one()
    {
        var broken = CreateFolderWithFakeAvUtil();

        var failed = FfmpegLibrary.Load(broken);
        var result = FfmpegLibrary.Load();

        failed.IsSuccess.ShouldBeFalse();
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.MajorVersion.ShouldBe(FfmpegLibrary.ExpectedMajorVersion);
    }

    private static string CreateFolderWithFakeAvUtil()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var name = OperatingSystem.IsWindows() ? "avutil-60.dll"
            : OperatingSystem.IsMacOS() ? "libavutil.60.dylib" : "libavutil.so.60";
        File.WriteAllText(Path.Combine(folder, name), "not a library");
        return folder;
    }
}
