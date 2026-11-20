using System.Collections.Concurrent;
using System.Globalization;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.Startup;

/// <summary>What the printer cannot learn from the events: each output's track and device name, the screen, the opened file.</summary>
internal sealed record TerminalContext(IReadOnlyList<OutputLabel> Outputs, ScreenInfo Screen, string File, TimeProvider Time);

/// <summary>An output's track number (from 1) and the device's display name.</summary>
internal sealed record OutputLabel(int Track, string DeviceName);

/// <summary>
/// Prints the start, warning, running and run-summary lines of contracts/cli.md to the terminal and the log. Lines are queued and
/// written by one thread, so a slow terminal never stalls the control loop; a terminal that cannot be written stops printing, the log goes on.
/// </summary>
internal sealed class TerminalPrinter : IDisposable
{
    private readonly TextWriter _output;
    private readonly ILogger? _log;
    private readonly TerminalContext _context;
    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread _thread;
    private readonly IDisposable _engineSubscription;
    private readonly IDisposable _sessionSubscription;
    private readonly Lock _gate = new();
    private LatencyEstimate? _estimate;
    private StartTimings? _timings;
    private AudioStarted? _audioStarted;
    private string _file;
    private bool _terminalBroken;

    public TerminalPrinter(
        IObservable<EngineEvent> engine,
        IObservable<SessionEvent> session,
        TextWriter output,
        TextWriter error,
        ILogger? log,
        TerminalContext context)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(context);
        _output = output;
        _log = log;
        _context = context;
        _file = Path.GetFileName(context.File);
        _thread = new Thread(Drain) { IsBackground = true, Name = "terminal-printer" };
        _thread.Start();
        _engineSubscription = engine.Subscribe(new Observer<EngineEvent>(OnEngine));
        _sessionSubscription = session.Subscribe(new Observer<SessionEvent>(OnSession));
    }

    public void Dispose()
    {
        _engineSubscription.Dispose();
        _sessionSubscription.Dispose();
        FlushPendingStarted();
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void OnEngine(EngineEvent value)
    {
        switch (value)
        {
            case AudioStarted a:
                Started(a);
                break;
            case RunSummary s:
                FlushPendingStarted();
                string file;
                lock (_gate)
                {
                    file = _file;
                }

                foreach (var line in RunSummaryLines.Lines(file, s))
                {
                    Print(line);
                }

                break;
            default:
                break;
        }
    }

    private void OnSession(SessionEvent value)
    {
        switch (value)
        {
            case LatencyEstimated l:
                LatencyLines(l.Estimate);
                break;
            case StartTimings t:
                StartTimingsArrived(t);
                break;
            case OffsetsWarning w:
                // A state-file problem prints the contract's sentence; the store's own wording goes to the log only.
                var text = w.Warning.Code.StartsWith("state.", StringComparison.Ordinal)
                    ? "remembered offsets could not be read, every output starts at offset 0"
                    : w.Warning.Message;
                Print($"warning {w.Warning.Code}: {text}");
                break;
            case FileSkipped f:
                var where = f.OutputNumber is { } o && f.Track is { } t2 ? $"  output {o}  track {t2}" : string.Empty;
                var format = f.Codec is { } codec ? $"  codec {codec}  profile {ProfileName(f.Profile)}" : string.Empty;
                Print($"skipped  {f.File}  reason {f.Reason}{where}{format}");
                break;
            case FileChanged c:
                var name = Path.GetFileName(c.File);
                lock (_gate)
                {
                    _file = name;
                }

                Print($"{(c.ToPrevious ? "previous" : "next"),-8} {name}");
                break;
            case NoNeighbour n:
                Print(n.Next ? "no-next-file" : "no-previous-file");
                break;
            case OutputLost l:
                var move = l.OthersContinue && l.OldPictureDelayMs is { } from && l.NewPictureDelayMs is { } to ? $"  picture-delay {from} -> {to}" : string.Empty;
                Print($"device-lost  output {l.OutputNumber}  device {l.DeviceId}  clock {RunSummaryLines.Clock(l.Clock)}  {(l.OthersContinue ? "others continue" : "last output, film ended")}{move}");
                break;
            case PictureFailed p:
                Print($"picture-failed  clock {RunSummaryLines.Clock(p.Clock)}  the picture could not be decoded further, film ended");
                break;
            default:
                break;
        }
    }

    // FFmpeg's profile names ("Main 10") in the contract's form ("main-10").
    private static string ProfileName(string? profile) =>
        string.IsNullOrWhiteSpace(profile) ? "unknown" : string.Join('-', profile.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private void LatencyLines(LatencyEstimate estimate)
    {
        lock (_gate)
        {
            _estimate = estimate;
        }

        for (var i = 0; i < estimate.Outputs.Count; i++)
        {
            var o = estimate.Outputs[i];
            var label = i < _context.Outputs.Count ? _context.Outputs[i] : new OutputLabel(i + 1, o.DeviceId);
            Print(string.Create(
                CultureInfo.InvariantCulture,
                $"output {i + 1}  track {label.Track}  device {label.DeviceName}  type {TypeName(o.Type)}  reported {o.ReportedMs}  default {o.TypeDefaultMs}  offset {Offset(o.OffsetMs)}  assumed {o.AssumedMs}  lag {o.LagMs}"));
        }

        var screen = _context.Screen;
        Print(string.Create(CultureInfo.InvariantCulture, $"picture   delay {estimate.PictureDelayMs}  screen {screen.Number}  {screen.Width}x{screen.Height}"));
    }

    private void Started(AudioStarted a)
    {
        // The two events arrive in no fixed order and on different threads: the line is printed when the second one arrives.
        StartTimings? timings;
        lock (_gate)
        {
            if (_timings is null)
            {
                _audioStarted = a;
                return;
            }

            timings = _timings;
            _timings = null;
        }

        PrintStarted(a, timings);
    }

    private void StartTimingsArrived(StartTimings t)
    {
        AudioStarted? started;
        lock (_gate)
        {
            if (_audioStarted is null)
            {
                _timings = t;
                return;
            }

            started = _audioStarted;
            _audioStarted = null;
        }

        PrintStarted(started, t);
    }

    /// <summary>Prints a started line still waiting for its timings, without them, so a start is never silent.</summary>
    private void FlushPendingStarted()
    {
        AudioStarted? started;
        lock (_gate)
        {
            started = _audioStarted;
            _audioStarted = null;
        }

        if (started is not null)
        {
            PrintStarted(started, null);
        }
    }

    private void PrintStarted(AudioStarted a, StartTimings? timings)
    {
        LatencyEstimate? estimate;
        lock (_gate)
        {
            estimate = _estimate;
        }

        var line = $"started   clock {RunSummaryLines.Clock(a.ClockPosition)}";
        if (timings is not null)
        {
            var frequency = _context.Time.TimestampFrequency;
            string Seconds(long stamp) =>
                ((double)(stamp - timings.CommandTimestamp) / frequency).ToString("0.0", CultureInfo.InvariantCulture) + " s";

            var audio = estimate is null
                ? timings.FirstAudioHeard.Values
                : estimate.Outputs.Where(o => timings.FirstAudioHeard.ContainsKey(o.DeviceId)).Select(o => timings.FirstAudioHeard[o.DeviceId]);
            line += $"  first-frame {Seconds(timings.FirstFramePresented)}  first-audio {string.Join(' ', audio.Select(Seconds))}";
        }

        Print(line);
    }

    private static string TypeName(DeviceType type) => type switch
    {
        DeviceType.BluetoothHeadphones => "bluetooth-headphones",
        DeviceType.BluetoothSpeaker => "bluetooth-speaker",
        DeviceType.Wired => "wired",
        DeviceType.Hdmi => "hdmi",
        _ => "other",
    };

    private static string Offset(int ms) => ms.ToString(ms == 0 ? "0" : "+0;-0", CultureInfo.InvariantCulture);

    private void Print(string line)
    {
        try
        {
            _queue.Add(line);
        }
        catch (InvalidOperationException)
        {
            // Disposed: the run is over.
        }
    }

    private void Drain()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            // The log never depends on the terminal: the line is logged first, whatever the write does next.
            _log?.LogInformation("{Line}", line);
            if (_terminalBroken)
            {
                continue;
            }

            try
            {
                _output.WriteLine(line);
                _output.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _terminalBroken = true;
                _log?.LogError(ex, "Writing a terminal line failed; printing stops");
            }
        }
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
