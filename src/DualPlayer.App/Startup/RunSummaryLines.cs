using System.Globalization;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;

namespace DualPlayer.App.Startup;

/// <summary>The run-summary lines of contracts/cli.md section 2.3: a <c>run-summary</c> header and one row per output (copied from the step-3 console formatting).</summary>
internal static class RunSummaryLines
{
    public static IEnumerable<string> Lines(string file, RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        yield return $"run-summary  file {file}  reason {Reason(summary.EndReason)}";
        yield return Row("output", "device", "played", "max_drift_ms", "clock_mismatch_ppm", "corrections", "underruns", "device_lost");

        var index = 1;
        foreach (var o in summary.Outputs)
        {
            var lost = o.DeviceLostEvents.Count == 0
                ? "-"
                : string.Join(',', o.DeviceLostEvents.Select(e => Clock(e.ClockPosition)));
            yield return Row(
                (index++).ToString(CultureInfo.InvariantCulture),
                o.DeviceId,
                (o.Played < TimeSpan.Zero ? "-" : "") + o.Played.Duration().ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture),
                o.MaxDriftMs.ToString("0.0", CultureInfo.InvariantCulture),
                o.ClockMismatchPpm.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture),
                o.Corrections.ToString(CultureInfo.InvariantCulture),
                o.Underruns.ToString(CultureInfo.InvariantCulture),
                lost);
        }
    }

    public static string Clock(TimeSpan position) =>
        position.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    public static string Reason(RunEndReason reason) => reason switch
    {
        RunEndReason.FileEnd or RunEndReason.SeekPastEnd => "end",
        RunEndReason.AllDevicesLost => "last-output-lost",
        RunEndReason.FileChange => "file-change",
        RunEndReason.PictureFailed => "picture-failed",
        _ => "exit",
    };

    public static string TypeName(DeviceType type) => type switch
    {
        DeviceType.BluetoothHeadphones => "bluetooth-headphones",
        DeviceType.BluetoothSpeaker => "bluetooth-speaker",
        DeviceType.Wired => "wired",
        DeviceType.Hdmi => "hdmi",
        _ => "other",
    };

    public static string CauseName(PictureDelayCause cause) => cause switch
    {
        PictureDelayCause.Offset => "offset",
        PictureDelayCause.DeviceLost => "device-lost",
        PictureDelayCause.Seek => "seek",
        PictureDelayCause.Pause => "pause",
        PictureDelayCause.Resume => "resume",
        PictureDelayCause.FileChange => "file-change",
        _ => "other",
    };

    private static string Row(string output, string device, string played, string drift, string mismatch, string corrections, string underruns, string lost) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{output,-7} {device,-26} {played,-11} {drift,-13} {mismatch,-19} {corrections,-12} {underruns,-10} {lost}");
}
