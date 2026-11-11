using DualPlayer.Core;

namespace DualPlayer.Console.Commands;

/// <summary>Exit codes of contracts/cli.md §3 and the stderr error line.</summary>
internal static class ExitCodes
{
    public const int Refused = 2;
    public const int AccessDenied = 4;

    public static int Report(Error error, TextWriter errorWriter)
    {
        errorWriter.WriteLine($"error {error.Code}: {error.Message}");
        return error.Code == "outputs.device_access_denied" ? AccessDenied : Refused;
    }
}
