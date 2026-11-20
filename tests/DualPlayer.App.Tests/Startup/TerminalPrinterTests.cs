using System.Text.RegularExpressions;
using DualPlayer.App.Startup;
using DualPlayer.Core;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.Tests.Startup;

public sealed partial class TerminalPrinterTests
{
    private const string DevA = "{0.0.0.00000000}.{0001}";
    private const string DevB = "{0.0.0.00000000}.{0002}";

    private static readonly OutputLatency OutA = new(DevA, DeviceType.BluetoothHeadphones, 0, 200, 40, 160, 40);
    private static readonly OutputLatency OutB = new(DevB, DeviceType.BluetoothHeadphones, 0, 200, 0, 200, 0);

    [Fact]
    public void Start_prints_one_line_per_output_the_picture_line_and_the_started_line()
    {
        var h = new Harness();
        h.Session.Publish(new LatencyEstimated(new LatencyEstimate([OutA, OutB], 200)));
        h.Session.Publish(new StartTimings(
            0,
            1900,
            new Dictionary<string, long> { [DevA] = 1900, [DevB] = 1900 }));
        h.Engine.Publish(new AudioStarted(TimeSpan.Zero, TimeSpan.FromSeconds(1.9)));

        var lines = h.Finish().Out;

        lines.ShouldContain("output 1 track 1 device Test Headset A type bluetooth-headphones reported 0 default 200 offset +40 assumed 160 lag 40");
        lines.ShouldContain("output 2 track 2 device Test Headset B type bluetooth-headphones reported 0 default 200 offset 0 assumed 200 lag 0");
        lines.ShouldContain("picture delay 200 screen 1 1920x1080");
        lines.ShouldContain("started clock 00:00:00.000 first-frame 1.9 s first-audio 1.9 s 1.9 s");
    }

    [Fact]
    public void The_started_line_carries_its_timings_when_the_audio_started_event_arrives_first()
    {
        var h = new Harness();
        h.Session.Publish(new LatencyEstimated(new LatencyEstimate([OutA, OutB], 200)));
        h.Engine.Publish(new AudioStarted(TimeSpan.Zero, TimeSpan.FromSeconds(1.9)));
        h.Session.Publish(new StartTimings(
            0,
            1900,
            new Dictionary<string, long> { [DevA] = 1900, [DevB] = 1900 }));

        var lines = h.Finish().Out;

        lines.ShouldContain("started clock 00:00:00.000 first-frame 1.9 s first-audio 1.9 s 1.9 s");
        lines.Count(l => l.StartsWith("started", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public void Start_lines_are_also_written_to_the_log_with_the_remembered_offset_and_latency()
    {
        var h = new Harness();
        h.Session.Publish(new LatencyEstimated(new LatencyEstimate([OutA, OutB], 200)));

        var result = h.Finish();

        result.Log.ShouldContain(l => Squash(l) == "output 1 track 1 device Test Headset A type bluetooth-headphones reported 0 default 200 offset +40 assumed 160 lag 40");
    }

    [Theory]
    [InlineData("state.unreadable", "remembered offsets could not be read, every output starts at offset 0")]
    [InlineData("state.invalid", "remembered offsets are invalid, every output starts at offset 0")]
    [InlineData("state.too-new", "remembered offsets were saved by a newer version, every output starts at offset 0")]
    public void A_state_warning_is_printed_as_a_warning_line(string code, string message)
    {
        var h = new Harness();
        h.Session.Publish(new OffsetsWarning(new Error(code, message)));

        h.Finish().Out.ShouldContain($"warning {code}: {message}");
    }

    [Fact]
    public void A_clamped_offset_is_printed_as_a_warning_per_value()
    {
        var h = new Harness();
        h.Session.Publish(new OffsetsWarning(new Error("offsets.clamped", "device {0001} offset 900 -> 500")));

        h.Finish().Out.ShouldContain("warning offsets.clamped: device {0001} offset 900 -> 500");
    }

    [Theory]
    [InlineData(RunEndReason.FileEnd, "end")]
    [InlineData(RunEndReason.Stop, "exit")]
    [InlineData(RunEndReason.AllDevicesLost, "last-output-lost")]
    public void A_run_summary_is_preceded_by_its_header_and_has_a_row_for_every_output(RunEndReason reason, string text)
    {
        var h = new Harness();
        h.Engine.Publish(new RunSummary(
            reason,
            TimeSpan.FromMinutes(90),
            [
                new OutputSummary(DevA, TimeSpan.FromMinutes(90), 3.5, 1.2, 4, 0, []),
                new OutputSummary(DevB, TimeSpan.FromMinutes(90), 4.5, -0.7, 2, 1, []),
            ]));

        var result = h.Finish();

        var header = result.Out.FindIndex(l => l == $"run-summary file a.mkv reason {text}");
        header.ShouldBeGreaterThanOrEqualTo(0);
        result.Out.Skip(header).ShouldContain(l => l.Contains(DevA, StringComparison.Ordinal));
        result.Out.Skip(header).ShouldContain(l => l.Contains(DevB, StringComparison.Ordinal));
        result.Log.ShouldContain(l => Squash(l) == $"run-summary file a.mkv reason {text}");
    }

    [Fact]
    public void File_change_lines_are_printed_for_a_skipped_file_and_the_next_one()
    {
        var h = new Harness();
        h.Session.Publish(new FileSkipped("b.mkv", "track-missing", 2, 2));
        h.Session.Publish(new FileChanged("c.mkv", "a.mkv"));

        var lines = h.Finish().Out;

        lines.ShouldContain("skipped b.mkv reason track-missing output 2 track 2");
        lines.ShouldContain("next c.mkv");
    }

    [Fact]
    public void Device_lost_on_the_last_output_and_picture_failure_are_printed()
    {
        var h = new Harness();
        h.Session.Publish(new OutputLost(1, DevA, new TimeSpan(0, 0, 41, 0, 0), OthersContinue: false));
        h.Session.Publish(new PictureFailed(new TimeSpan(1, 10, 5) + TimeSpan.FromMilliseconds(120), "decode"));

        var lines = h.Finish().Out;

        lines.ShouldContain($"device-lost output 1 device {DevA} clock 00:41:00.000 last output, film ended");
        lines.ShouldContain("picture-failed clock 01:10:05.120 the picture could not be decoded further, film ended");
    }

    [Fact]
    public void Errors_go_to_standard_error_not_standard_output()
    {
        var h = new Harness();
        h.Session.Publish(new OffsetsWarning(new Error("offsets.clamped", "x")));
        var result = h.Finish();

        result.Err.ShouldBeEmpty();
        result.Out.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_closed_output_stops_printing_but_the_log_continues()
    {
        var log = new ListLogger();
        var engine = new Feed<EngineEvent>();
        var session = new Feed<SessionEvent>();
        using (new TerminalPrinter(engine, session, new ThrowingWriter(), new StringWriter(), log, Harness.Context()))
        {
            session.Publish(new FileChanged("c.mkv", "a.mkv"));
        }

        log.Lines.Select(Squash).ShouldContain("next c.mkv");
    }

    private static string Squash(string s) => Spaces().Replace(s.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private sealed record Output(List<string> Out, List<string> Err, List<string> Log);

    private sealed class Harness
    {
        private readonly StringWriter _out = new();
        private readonly StringWriter _err = new();
        private readonly ListLogger _log = new();
        private readonly TerminalPrinter _printer;

        public Harness()
        {
            _printer = new TerminalPrinter(Engine, Session, _out, _err, _log, Context());
        }

        public Feed<EngineEvent> Engine { get; } = new();

        public Feed<SessionEvent> Session { get; } = new();

        public static TerminalContext Context() => new(
            [new OutputLabel(1, "Test Headset A"), new OutputLabel(2, "Test Headset B")],
            new ScreenInfo(1, 1920, 1080, true),
            "a.mkv",
            new FixedFrequencyTime());

        public Output Finish()
        {
            _printer.Dispose();
            return new Output(Lines(_out), Lines(_err), _log.Lines.ToList());
        }

        private static List<string> Lines(StringWriter w) =>
            w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Squash).ToList();
    }

    private sealed class FixedFrequencyTime : TimeProvider
    {
        public override long TimestampFrequency => 1000;
    }

    private sealed class Feed<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = [];

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _observers.Add(observer);
            return new Unsubscribe(() => _observers.Remove(observer));
        }

        public void Publish(T value)
        {
            foreach (var o in _observers.ToList())
            {
                o.OnNext(value);
            }
        }

        private sealed class Unsubscribe(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }

    private sealed class ListLogger : ILogger
    {
        private readonly Lock _gate = new();
        private readonly List<string> _lines = [];

        public List<string> Lines
        {
            get
            {
                lock (_gate)
                {
                    return [.. _lines];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _lines.Add(formatter(state, exception));
            }
        }
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value) => throw new IOException("closed pipe");

        public override void Write(string? value) => throw new IOException("closed pipe");

        public override void WriteLine(string? value) => throw new IOException("closed pipe");
    }
}
