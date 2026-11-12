using System.Globalization;
using DualPlayer.Core.Diagnostics;

namespace DualPlayer.Console.Rendering;

/// <summary>Formats the run summary table of contracts/cli.md §2.4.</summary>
internal static class RunSummaryPrinter
{
    public static IEnumerable<string> Lines(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        yield return $"run summary  ended {Reason(summary.EndReason)}  clock {ConsoleFormat.Clock(summary.ClockPosition)}";
        yield return Row("output", "device", "played", "max_drift_ms", "clock_mismatch_ppm", "corrections", "underruns", "device_lost");

        var index = 1;
        foreach (var o in summary.Outputs)
        {
            var lost = o.DeviceLostEvents.Count == 0
                ? "-"
                : string.Join(',', o.DeviceLostEvents.Select(e => ConsoleFormat.Clock(e.ClockPosition)));
            yield return Row(
                (index++).ToString(CultureInfo.InvariantCulture),
                o.DeviceId,
                // A negative duration is an engine bug; keep its sign visible instead of printing a plausible time.
                (o.Played < TimeSpan.Zero ? "-" : "") + o.Played.Duration().ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture),
                o.MaxDriftMs.ToString("0.0", CultureInfo.InvariantCulture),
                o.ClockMismatchPpm.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture),
                o.Corrections.ToString(CultureInfo.InvariantCulture),
                o.Underruns.ToString(CultureInfo.InvariantCulture),
                lost);
        }
    }

    public static string Reason(RunEndReason reason) => reason switch
    {
        RunEndReason.FileEnd => "file-end",
        RunEndReason.Stop => "stop",
        RunEndReason.Interrupt => "interrupt",
        RunEndReason.SeekPastEnd => "seek-past-end",
        RunEndReason.AllDevicesLost => "all-devices-lost",
        _ => "stop",
    };

    private static string Row(string output, string device, string played, string drift, string mismatch, string corrections, string underruns, string lost) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{output,-7} {device,-26} {played,-11} {drift,-13} {mismatch,-19} {corrections,-12} {underruns,-10} {lost}");
}
