using System.Globalization;
using DualPlayer.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Console.Rendering;

/// <summary>Prints every engine event live (contracts/cli.md §2) and mirrors each line to the log.</summary>
internal sealed class EventPrinter : IObserver<EngineEvent>, IDisposable
{
    private readonly TextWriter _output;
    private readonly ILogger? _log;
    private readonly Lock _gate = new();
    private readonly IDisposable _subscription;
    private readonly CancellationTokenSource _runEnded = new();

    public EventPrinter(IObservable<EngineEvent> events, TextWriter output, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _log = log;
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
                    $"device-lost  output {d.OutputIndex}  device {d.DeviceId}  clock {ConsoleFormat.Clock(d.ClockPosition)}  {(d.OthersContinue ? "others continue" : "last output, run ends")}"));
                break;
            case RunSummary s:
                ExitCode = s.EndReason == RunEndReason.AllDevicesLost ? 3 : 0;
                foreach (var line in RunSummaryPrinter.Lines(s))
                {
                    Write(line);
                }

                _runEnded.Cancel();
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

    public void Dispose()
    {
        _subscription.Dispose();
        _runEnded.Dispose();
    }

    private static string Signed(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    private void Write(string line)
    {
        lock (_gate)
        {
            _output.WriteLine(line);
            _output.Flush();
        }

        _log?.LogInformation("{Line}", line);
    }
}
