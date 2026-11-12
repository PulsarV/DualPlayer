using Microsoft.Extensions.Logging;

namespace DualPlayer.Platform.Windows.Audio;

/// <summary>Logging for the MMCSS "Pro Audio" registration of a render thread (pure, so it is testable off Windows).</summary>
internal static partial class MmcssLog
{
    /// <summary>Logs a warning with the Win32 error when <paramref name="handle"/> is zero (registration failed).</summary>
    public static void WarnIfNotRaised(IntPtr handle, int lastError, ILogger logger, string device)
    {
        if (handle == IntPtr.Zero)
        {
            RegistrationFailed(logger, device, lastError);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "MMCSS 'Pro Audio' registration failed for {Device} (Win32 error {Error}); the render thread runs at normal priority")]
    private static partial void RegistrationFailed(ILogger logger, string device, int error);
}
