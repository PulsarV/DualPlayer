using System.Runtime.InteropServices;
using DualPlayer.Core;
using FFmpeg.AutoGen;

namespace DualPlayer.Media.Audio;

/// <summary>Version and license of the loaded FFmpeg, for logging.</summary>
public sealed record FfmpegInfo(string Version, string License, int MajorVersion);

/// <summary>Locates and loads the FFmpeg shared libraries (ADR-0005). Dynamic linking only.</summary>
public static class FfmpegLibrary
{
    private static readonly object Gate = new();
    private static string? _loadedRoot;

    /// <summary>The FFmpeg major version matching the pinned FFmpeg.AutoGen package.</summary>
    public static int ExpectedMajorVersion { get; } = typeof(ffmpeg).Assembly.GetName().Version?.Major ?? 0;

    /// <summary>Loads FFmpeg from <paramref name="rootPath"/>, or from the default location when null.</summary>
    public static Result<FfmpegInfo> Load(string? rootPath = null)
    {
        var root = rootPath ?? DefaultRoot();
        if (!Directory.Exists(root) || !HasAvUtil(root))
        {
            return Result<FfmpegInfo>.Failure(
                "ffmpeg.not_found",
                $"FFmpeg shared libraries not found in '{root}'. Install with 'brew install ffmpeg' (macOS) or run build/fetch-ffmpeg.ps1 (Windows).");
        }

        lock (Gate)
        {
            if (_loadedRoot is not null && !SameRoot(_loadedRoot, root))
            {
                return Result<FfmpegInfo>.Failure(
                    "ffmpeg.already_loaded",
                    $"FFmpeg is already loaded from '{_loadedRoot}'; it cannot also be loaded from '{root}'.");
            }

            try
            {
                if (_loadedRoot is null)
                {
                    ffmpeg.RootPath = root;
                    DynamicallyLoadedBindings.Initialize();
                }

                var version = ffmpeg.av_version_info() ?? "unknown";
                var license = ffmpeg.avutil_license() ?? "unknown";
                var major = (int)(ffmpeg.avutil_version() >> 16);
                _loadedRoot = root;
                return Result<FfmpegInfo>.Success(new FfmpegInfo(version, license, ToFfmpegMajor(major)));
            }
            catch (Exception ex) when (ex is DllNotFoundException or NotSupportedException or EntryPointNotFoundException or BadImageFormatException)
            {
                return Result<FfmpegInfo>.Failure("ffmpeg.load_failed", $"FFmpeg in '{root}' could not be loaded: {ex.Message}");
            }
        }
    }

    private static bool SameRoot(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // libavutil major -> FFmpeg major (libavutil 56 = FFmpeg 4, 57 = 5, 58 = 6, 59 = 7, 60 = 8).
    private static int ToFfmpegMajor(int avutilMajor) => avutilMajor - 52;

    private static string DefaultRoot()
    {
        if (OperatingSystem.IsMacOS())
        {
            return Directory.Exists("/opt/homebrew/lib") ? "/opt/homebrew/lib" : "/usr/local/lib";
        }

        return AppContext.BaseDirectory;
    }

    private static bool HasAvUtil(string root)
    {
        var pattern = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "avutil-*.dll"
            : OperatingSystem.IsMacOS() ? "libavutil*.dylib" : "libavutil*.so*";
        return Directory.EnumerateFiles(root, pattern).Any();
    }
}
