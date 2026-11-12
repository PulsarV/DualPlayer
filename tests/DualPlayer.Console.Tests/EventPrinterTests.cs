using DualPlayer.Console;
using DualPlayer.Console.Commands;
using DualPlayer.Console.Rendering;
using DualPlayer.Core;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Console.Tests;

public sealed class EventPrinterTests
{
    private const string Dev1 = "{0.0.0.00000000}.{0001}";
    private const string Dev2 = "{0.0.0.00000000}.{0002}";

    private readonly StringWriter _out = new();
    private readonly FakeEngine _engine = new();

    private string[] Lines() =>
        _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] Tokens(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static RunSummary Summary(RunEndReason reason, params OutputSummary[] outputs) =>
        new(reason, TimeSpan.FromSeconds(4330), outputs);

    [Fact]
    public void A_sync_record_prints_one_record_line_with_clock_device_drift_correction_and_underruns()
    {
        using var printer = new EventPrinter(_engine.Events, _out);

        _engine.Publish(new SyncRecord(Dev1, new TimeSpan(0, 12, 34), 1.4, -35, 0));

        Lines().ShouldBe([$"record  clock 00:12:34.000  device {Dev1}  drift_ms +1.4  correction_ppm -35  underruns 0"]);
    }

    [Fact]
    public void Records_of_two_devices_print_separate_lines_keyed_by_device()
    {
        using var printer = new EventPrinter(_engine.Events, _out);

        _engine.Publish(new SyncRecord(Dev1, TimeSpan.FromSeconds(1), -0.5, 10, 2));
        _engine.Publish(new SyncRecord(Dev2, TimeSpan.FromSeconds(1), 3.0, 0, 7));

        var lines = Lines();
        lines.Length.ShouldBe(2);
        lines[0].ShouldContain($"device {Dev1}");
        lines[0].ShouldContain("drift_ms -0.5");
        lines[1].ShouldContain($"device {Dev2}");
        lines[1].ShouldContain("underruns 7");
    }

    [Fact]
    public void An_excursion_prints_start_duration_and_signed_peak()
    {
        using var printer = new EventPrinter(_engine.Events, _out);

        _engine.Publish(new Excursion(Dev2, new TimeSpan(0, 1, 2, 3), TimeSpan.FromMilliseconds(850), 14.2));

        Lines().ShouldBe([$"excursion  device {Dev2}  start 01:02:03.000  duration_ms 850  peak_ms +14.2"]);
    }

    [Fact]
    public void An_excursion_of_an_output_behind_prints_a_negative_peak()
    {
        using var printer = new EventPrinter(_engine.Events, _out);

        _engine.Publish(new Excursion(Dev2, new TimeSpan(0, 1, 2, 3), TimeSpan.FromMilliseconds(850), -14.2));

        Lines().ShouldBe([$"excursion  device {Dev2}  start 01:02:03.000  duration_ms 850  peak_ms -14.2"]);
    }

    [Fact]
    public void A_device_loss_with_other_outputs_playing_says_others_continue()
    {
        using var printer = new EventPrinter(_engine.Events, _out);

        _engine.Publish(new DeviceLost(2, Dev2, new TimeSpan(0, 0, 40, 12, 300), OthersContinue: true));

        Lines().ShouldBe([$"device-lost  output 2  device {Dev2}  clock 00:40:12.300  others continue"]);
    }

    [Fact]
    public void Losing_the_last_output_says_the_run_ends()
    {
        using var printer = new EventPrinter(_engine.Events, _out);

        _engine.Publish(new DeviceLost(1, Dev1, new TimeSpan(0, 0, 40, 12, 300), OthersContinue: false));

        Lines().ShouldBe([$"device-lost  output 1  device {Dev1}  clock 00:40:12.300  last output, run ends"]);
    }

    [Fact]
    public void The_run_summary_prints_the_reason_and_one_row_per_output_with_its_device_lost_clock()
    {
        using var printer = new EventPrinter(_engine.Events, _out);
        var lost = new DeviceLost(2, Dev2, new TimeSpan(0, 0, 40, 12, 300), true);

        _engine.Publish(Summary(
            RunEndReason.FileEnd,
            new OutputSummary(Dev1, new TimeSpan(1, 52, 10), 8.7, 41.2, 0, 0, []),
            new OutputSummary(Dev2, new TimeSpan(0, 0, 40, 12, 300), 9.1, -18.5, 0, 0, [lost])));

        var lines = Lines();
        lines.Length.ShouldBe(4);
        lines[0].ShouldBe("run summary  ended file-end  clock 01:12:10.000");
        Tokens(lines[1]).ShouldBe(["output", "device", "played", "max_drift_ms", "clock_mismatch_ppm", "corrections", "underruns", "device_lost"]);
        Tokens(lines[2]).ShouldBe(["1", Dev1, "01:52:10.0", "8.7", "+41.2", "0", "0", "-"]);
        Tokens(lines[3]).ShouldBe(["2", Dev2, "00:40:12.3", "9.1", "-18.5", "0", "0", "00:40:12.300"]);
    }

    [Theory]
    [InlineData(RunEndReason.FileEnd, "file-end")]
    [InlineData(RunEndReason.Stop, "stop")]
    [InlineData(RunEndReason.Interrupt, "interrupt")]
    [InlineData(RunEndReason.SeekPastEnd, "seek-past-end")]
    [InlineData(RunEndReason.AllDevicesLost, "all-devices-lost")]
    public void The_summary_names_every_end_reason_as_in_the_contract(RunEndReason reason, string text)
    {
        using var printer = new EventPrinter(_engine.Events, _out);

        _engine.Publish(Summary(reason));

        Lines()[0].ShouldStartWith($"run summary  ended {text}  clock ");
    }

    [Fact]
    public void The_summary_lists_every_device_lost_event_of_an_output()
    {
        using var printer = new EventPrinter(_engine.Events, _out);
        var lost = new DeviceLost(1, Dev1, new TimeSpan(0, 0, 5, 0, 100), false);

        _engine.Publish(Summary(RunEndReason.AllDevicesLost, new OutputSummary(Dev1, TimeSpan.FromMinutes(5), 1, 0, 0, 0, [lost])));

        Lines().Last().ShouldContain("00:05:00.100");
    }

    [Theory]
    [InlineData(RunEndReason.AllDevicesLost, 3)]
    [InlineData(RunEndReason.FileEnd, 0)]
    [InlineData(RunEndReason.Stop, 0)]
    [InlineData(RunEndReason.Interrupt, 0)]
    [InlineData(RunEndReason.SeekPastEnd, 0)]
    public void The_exit_code_follows_the_summary_and_is_three_only_when_all_devices_were_lost(RunEndReason reason, int exit)
    {
        using var printer = new EventPrinter(_engine.Events, _out);

        _engine.Publish(Summary(reason));

        printer.ExitCode.ShouldBe(exit);
    }

    [Fact]
    public void After_disposal_no_further_events_are_printed()
    {
        var printer = new EventPrinter(_engine.Events, _out);
        printer.Dispose();

        _engine.Publish(new SyncRecord(Dev1, TimeSpan.Zero, 0, 0, 0));

        Lines().ShouldBeEmpty();
    }

    [Fact]
    public void Ctrl_C_stops_the_run_as_interrupt_and_the_summary_is_printed_with_exit_zero()
    {
        using var printer = new EventPrinter(_engine.Events, _out);
        var signals = new ShutdownSignals(_engine);

        signals.Interrupt();

        _engine.StopReasons.ShouldBe([RunEndReason.Interrupt]);
        Lines()[0].ShouldStartWith("run summary  ended interrupt");
        printer.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void A_second_interrupt_while_stopping_is_ignored()
    {
        using var printer = new EventPrinter(_engine.Events, _out);
        var signals = new ShutdownSignals(_engine);

        signals.Interrupt();
        signals.Interrupt();

        _engine.StopReasons.Count.ShouldBe(1);
    }

    [Fact]
    public void An_interrupt_runs_the_exit_step_once_after_the_summary_is_written_with_exit_zero()
    {
        using var printer = new EventPrinter(_engine.Events, _out);
        var exits = new List<(string[] Lines, int ExitCode)>();
        var signals = new ShutdownSignals(_engine, () => exits.Add((Lines(), printer.ExitCode)));

        signals.Interrupt();
        signals.Interrupt();

        exits.Count.ShouldBe(1);
        exits[0].Lines.ShouldContain(l => l.StartsWith("run summary  ended interrupt", StringComparison.Ordinal));
        exits[0].ExitCode.ShouldBe(0);
    }

    [Fact]
    public void An_interrupt_before_the_run_starts_stops_the_run_as_soon_as_it_has_started()
    {
        using var printer = new EventPrinter(_engine.Events, _out);
        var exits = 0;
        var signals = new ShutdownSignals(_engine, () => exits++);
        signals.DeferUntilRunStarts();

        signals.Interrupt();

        _engine.StopReasons.ShouldBeEmpty();
        exits.ShouldBe(0);

        signals.RunStarted();
        signals.Interrupt();

        _engine.StopReasons.ShouldBe([RunEndReason.Interrupt]);
        exits.ShouldBe(1);
    }

    [Fact]
    public void A_deferred_signal_handler_without_an_interrupt_stops_nothing_when_the_run_starts()
    {
        var signals = new ShutdownSignals(_engine);
        signals.DeferUntilRunStarts();

        signals.RunStarted();

        _engine.StopReasons.ShouldBeEmpty();
    }

    [Fact]
    public async Task Losing_the_last_device_ends_the_play_command_with_exit_3_while_stdin_stays_open()
    {
        var engine = new FakeEngine
        {
            Started = Result<RunStarted>.Success(new RunStarted(
                [new StartedOutput(1, Dev1, "Test Headset A", new MixFormat(48000, 2, ChannelLayout.Stereo))],
                TimeSpan.Zero)),
        };
        using var stdin = new OpenStdin();
        var request = new PlayRequest("movie.mkv", [new OutputAssignment(1, DeviceRef.ByNumber(1))], new EngineOptions());
        var ct = TestContext.Current.CancellationToken;

        var run = Task.Run(() => new PlayCommand(engine).Run(request, _out, TextWriter.Null, stdin), ct);
        stdin.Reading.Wait(TimeSpan.FromSeconds(10), ct).ShouldBeTrue();
        engine.Publish(new DeviceLost(1, Dev1, new TimeSpan(0, 0, 40, 12, 300), OthersContinue: false));
        engine.Publish(Summary(RunEndReason.AllDevicesLost));

        (await run.WaitAsync(TimeSpan.FromSeconds(10), ct)).ShouldBe(3);
        Lines().ShouldContain(l => l.EndsWith("last output, run ends", StringComparison.Ordinal));
        Lines().ShouldContain(l => l.StartsWith("run summary  ended all-devices-lost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_that_ends_at_file_end_ends_the_play_command_with_exit_0_while_stdin_stays_open()
    {
        var engine = new FakeEngine
        {
            Started = Result<RunStarted>.Success(new RunStarted(
                [new StartedOutput(1, Dev1, "Test Headset A", new MixFormat(48000, 2, ChannelLayout.Stereo))],
                TimeSpan.Zero)),
        };
        using var stdin = new OpenStdin();
        var request = new PlayRequest("movie.mkv", [new OutputAssignment(1, DeviceRef.ByNumber(1))], new EngineOptions());
        var ct = TestContext.Current.CancellationToken;

        var run = Task.Run(() => new PlayCommand(engine).Run(request, _out, TextWriter.Null, stdin), ct);
        stdin.Reading.Wait(TimeSpan.FromSeconds(10), ct).ShouldBeTrue();
        engine.Publish(Summary(RunEndReason.FileEnd));

        (await run.WaitAsync(TimeSpan.FromSeconds(10), ct)).ShouldBe(0);
    }

    /// <summary>Standard input that stays open: every read blocks until the reader is disposed.</summary>
    private sealed class OpenStdin : TextReader
    {
        private readonly ManualResetEventSlim _closed = new();

        public ManualResetEventSlim Reading { get; } = new();

        public override string? ReadLine()
        {
            Reading.Set();
            _closed.Wait();
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closed.Set();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class FakeEngine : IAudioEngine
    {
        private readonly List<IObserver<EngineEvent>> _observers = [];

        public List<RunEndReason> StopReasons { get; } = [];

        public Result<RunStarted>? Started { get; init; }

        public IObservable<EngineEvent> Events => new Source(this);

        public void Publish(EngineEvent e)
        {
            foreach (var o in _observers.ToArray())
            {
                o.OnNext(e);
            }
        }

        public Result<RunStarted> StartRun(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options) =>
            Started ?? throw new NotSupportedException();

        public Result<TimeSpan> Pause() => throw new NotSupportedException();

        public Result<TimeSpan> Resume() => throw new NotSupportedException();

        public Result<SeekResult> Seek(TimeSpan target) => throw new NotSupportedException();

        public void Stop(RunEndReason reason)
        {
            StopReasons.Add(reason);
            Publish(new RunSummary(reason, TimeSpan.FromSeconds(10), []));
        }

        private sealed class Source(FakeEngine owner) : IObservable<EngineEvent>
        {
            public IDisposable Subscribe(IObserver<EngineEvent> observer)
            {
                owner._observers.Add(observer);
                return new Unsubscribe(() => owner._observers.Remove(observer));
            }
        }

        private sealed class Unsubscribe(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
