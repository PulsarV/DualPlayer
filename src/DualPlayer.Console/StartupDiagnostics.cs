using DualPlayer.Media.Audio;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Console;

/// <summary>Start-of-process log entries (F25).</summary>
internal static class StartupDiagnostics
{
    /// <summary>Logs the FFmpeg version and license; adds a warning when the build is not LGPL (GPL or nonfree).</summary>
    public static void LogFfmpeg(FfmpegInfo info, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(log);

        log.LogInformation("FFmpeg {Version} loaded, license {License}", info.Version, info.License);
        if (!info.License.StartsWith("LGPL", StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning("FFmpeg license '{License}' is not LGPL; this build must not be redistributed (sad.md §11)", info.License);
        }
    }
}
