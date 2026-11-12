using System.Globalization;
using DualPlayer.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Console.Rendering;

/// <summary>Prints every engine event live (contracts/cli.md §2) through the console line queue, which also logs each line. Output numbers are 1-based.</summary>
internal sealed class EventPrinter : IObserver<EngineEvent>, IDisposable
{
    private readonly ConsoleLineQueue _lines;
    private readonly bool _ownsLines;
    private bool _summarized;
    private readonly IDisposable _subscription;
    private readonly CancellationTokenSource _runEnded = new();

    public EventPrinter(IObservable<EngineEvent> events, TextWriter output, ILogger? log = null)
        : this(events, new ConsoleLineQueue(output, output, log), ownsLines: true)
    {
    }

    /// <summary>Prints through a shared queue so its lines stay in order with the command's own lines.</summary>
    public EventPrinter(IObservable<EngineEvent> events, ConsoleLineQueue lines)
        : this(events, lines, ownsLines: false)
    {
    }

    private EventPrinter(IObservable<EngineEvent> events, ConsoleLineQueue lines, bool ownsLines)
    {
        ArgumentNullException.ThrowIfNull(events);
        _lines = lines;
        _ownsLines = ownsLines;
        _subscription = events.Subscribe(this);
    }

    /// <summary>3 when the run ended with every device lost, otherwise 0.</summary>
    public int ExitCode { get; private set; }

    /// <summary>Cancelled once the run summary has been written: the run is over.</summary>
    public CancellationToken RunEnded => _runEnded.Token;

    public void OnNext(EngineEvent value)
    {
        switch (value)
        {
            case SyncRecord r:
                Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"record  clock {ConsoleFormat.Clock(r.ClockPosition)}  device {r.DeviceId}  drift_ms {Signed(r.DriftMs)}  correction_ppm {r.CorrectionPpm:+0;-0;0}  underruns {r.Underruns}"));
                break;
            case Excursion e:
                Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"excursion  device {e.DeviceId}  start {ConsoleFormat.Clock(e.Start)}  duration_ms {e.Duration.TotalMilliseconds:0}  peak_ms {Signed(e.PeakMs)}"));
                break;
            case DeviceLost d:
                Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"device-lost  output {d.OutputIndex + 1}  device {d.DeviceId}  clock {ConsoleFormat.Clock(d.ClockPosition)}  {(d.OthersContinue ? "others continue" : "last output, run ends")}"));
                break;
            case AudioStarted a:
                Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"started   clock {ConsoleFormat.Clock(a.ClockPosition)}  start-to-audio {ConsoleFormat.Seconds(a.StartToAudio)}"));
                break;
            case SeekTargetOutsideFile o:
                // Before the start is a negative target (playing on from 0); anything else is past the end.
                Write(o.Target >= TimeSpan.Zero
                    ? "warning: target was outside the file"
                    : $"warning: target was outside the file, playing from {ConsoleFormat.Clock(o.Position)}");
                break;
            case RunSummary s:
                lock (_runEnded)
                {
                    if (_summarized)
                    {
                        break;
                    }

                    _summarized = true;
                }

                ExitCode = s.EndReason == RunEndReason.AllDevicesLost ? 3 : 0;
                var summary = RunSummaryPrinter.Lines(s).ToList();
                for (var i = 0; i < summary.Count; i++)
                {
                    _lines.Enqueue(summary[i], written: i == summary.Count - 1 ? SignalRunEnded : null);
                }

                break;
            default:
                break;
        }
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    // The last summary line may be written after Dispose gave up waiting on a stalled console; the run has ended either way.
    private void SignalRunEnded()
    {
        try
        {
            _runEnded.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Waits (bounded) until every line published so far has been written.</summary>
    public void Flush() => _lines.Flush();

    public void Dispose()
    {
        _subscription.Dispose();
        _lines.Flush();
        if (_ownsLines)
        {
            _lines.Dispose();
        }

        _runEnded.Dispose();
    }

    private static string Signed(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    private void Write(string line) => _lines.Enqueue(line);
}
