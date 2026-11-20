using DualPlayer.Media.Video;

namespace DualPlayer.Media.Tests.Video;

public sealed class MpvLibraryTests
{
    private static uint Api(uint major, uint minor) => (major << 16) | minor;

    [Fact]
    public void Load_from_a_path_that_does_not_exist_returns_mpv_not_found_naming_the_path()
    {
        var missing = Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N"), "libmpv-2.dll");

        var result = MpvLibrary.Load(missing);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe("mpv.not_found");
        result.Error.Message.ShouldContain(missing);
    }

    [Fact]
    public void Load_from_a_file_that_is_not_a_native_library_returns_mpv_load_failed_not_an_exception()
    {
        var dir = Directory.CreateTempSubdirectory("DualPlayer.Tests.");
        try
        {
            var bogus = Path.Combine(dir.FullName, "libmpv-2.dll");
            File.WriteAllText(bogus, "this is not a native library");

            var result = MpvLibrary.Load(bogus);

            result.IsSuccess.ShouldBeFalse();
            result.Error.ShouldNotBeNull();
            result.Error.Code.ShouldBe("mpv.load_failed");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void CheckVersion_with_a_client_API_older_than_required_returns_mpv_load_failed_naming_the_found_version()
    {
        var result = MpvLibrary.CheckVersion("/x/libmpv.dylib", Api(1, 107), Api(2, 0));

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe("mpv.load_failed");
        result.Error.Message.ShouldContain("1.107");
    }

    [Fact]
    public void CheckVersion_with_a_client_API_at_or_above_required_succeeds_with_the_path_and_version()
    {
        var result = MpvLibrary.CheckVersion("/x/libmpv.dylib", Api(2, 5), Api(2, 0));

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Path.ShouldBe("/x/libmpv.dylib");
        result.Value.ClientApiVersion.ShouldBe(Api(2, 5));
    }

    [Fact]
    public void Load_finds_the_installed_libmpv_and_reports_a_client_API_at_or_above_required()
    {
        Assert.SkipUnless(IsLibmpvInstalled(), "libmpv is not installed (brew install mpv / build/fetch-libmpv.ps1).");

        var result = MpvLibrary.Load();

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.ClientApiVersion.ShouldBeGreaterThanOrEqualTo(MpvLibrary.RequiredClientApiVersion);
        File.Exists(result.Value.Path).ShouldBeTrue();
    }

    [Fact]
    public void Load_of_the_installed_libmpv_with_an_unreachable_required_version_returns_mpv_load_failed()
    {
        Assert.SkipUnless(IsLibmpvInstalled(), "libmpv is not installed (brew install mpv / build/fetch-libmpv.ps1).");

        var result = MpvLibrary.Load(requiredClientApiVersion: uint.MaxValue);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe("mpv.load_failed");
    }

    private static bool IsLibmpvInstalled() =>
        OperatingSystem.IsMacOS()
            ? File.Exists("/opt/homebrew/lib/libmpv.dylib") || File.Exists("/usr/local/lib/libmpv.dylib")
            : File.Exists(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"));
}
