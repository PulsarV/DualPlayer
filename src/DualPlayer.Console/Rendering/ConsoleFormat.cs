using System.Globalization;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Console.Rendering;

/// <summary>Text formats shared by the console commands (contracts/cli.md).</summary>
internal static class ConsoleFormat
{
    public static string Clock(TimeSpan position) =>
        position.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    public static string Seconds(TimeSpan span) =>
        span.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    /// <summary>The track-to-device description inside the parentheses of an output line (contracts/cli.md section 1.3).</summary>
    public static string TrackConversion(StartedOutput output)
    {
        var device = output.Format;
        if (output.Source is not { } source)
        {
            return $"-> {Layout(device.ChannelLayout)} {device.SampleRate}";
        }

        var text = $"{source.Language} {Layout(source.ChannelLayout)}";
        if (source.ChannelLayout != device.ChannelLayout)
        {
            text += $" -> {Layout(device.ChannelLayout)}";
            if (source.ChannelLayout is ChannelLayout.Surround51 or ChannelLayout.Surround71 && device.Channels < 6)
            {
                text += ", LFE dropped";
            }
        }

        if (source.SampleRate != device.SampleRate)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" {source.SampleRate} -> {device.SampleRate}");
        }

        return text;
    }

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
