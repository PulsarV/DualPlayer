using DualPlayer.Core;

namespace DualPlayer.App.Startup;

/// <summary>Exit codes and the stderr line of the start command.</summary>
internal static class StartExitCodes
{
    public const int Ok = 0;

    public static int For(Error error) => error.Code switch
    {
        "playback.file_access_denied" or "outputs.device_access_denied" => 4,
        _ => 2,
    };

    public static string FormatError(Error error) => $"error {error.Code}: {error.Message}";
}
