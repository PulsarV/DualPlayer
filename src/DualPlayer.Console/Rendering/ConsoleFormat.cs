using System.Globalization;
using DualPlayer.Core.Outputs;

namespace DualPlayer.Console.Rendering;

/// <summary>Text formats shared by the console commands (contracts/cli.md).</summary>
internal static class ConsoleFormat
{
    public static string Clock(TimeSpan position) =>
        position.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    public static string SignedMs(double milliseconds) =>
        milliseconds.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ms";

    public static string Ppm(double ppm) =>
        ppm.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ppm";

    public static string Layout(ChannelLayout layout) => layout switch
    {
        ChannelLayout.Mono => "mono",
        ChannelLayout.Stereo => "stereo",
        ChannelLayout.Surround51 => "5.1",
        ChannelLayout.Surround71 => "7.1",
        _ => "other",
    };
}
