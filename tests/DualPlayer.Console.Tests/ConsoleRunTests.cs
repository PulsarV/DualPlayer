using DualPlayer.Console;
using DualPlayer.Console.Commands;
using DualPlayer.Console.Rendering;
using DualPlayer.Core;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Console.Tests;

/// <summary>T29: end of input keeps the summary (F1), one writer off the engine thread (F19), every line logged (F11), 1-based output numbers (F8).</summary>
public sealed class ConsoleRunTests
{
    private const string Dev1 = "dev-1";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    private static PlayRequest TwoOutputs() =>
        new("film.mkv", [new OutputAssignment(1, DeviceRef.ByNumber(1)), new OutputAssignment(2, DeviceRef.ByNumber(2))], new EngineOptions());

    private static string[] SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int SummaryCount(string text) =>
        SplitLines(text).Count(l => l.StartsWith("run summary", StringComparison.Ordinal));

    private static EngineRig TwoDeviceRig() => new([new RigDevice(48_000), new RigDevice(48_000)]);

    // ---- F1: end of input keeps the summary -------------------------------------------------------------------

    [Fact]
    public void End_of_standard_input_during_a_run_stops_it_and_prints_the_run_summary_before_the_command_returns_with_exit_0()
    {
        using var rig = TwoDeviceRig();

        var exit = rig.Call(() => new PlayCommand(rig.Engine, rig.Time).Run(TwoOutputs(), _out, _err, new StringReader(string.Empty)));

        var lines = SplitLines(_out.ToString());
        lines.ShouldContain(l => l.StartsWith("run summary  ended interrupt", StringComparison.Ordinal), _out.ToString());
        lines.Count(l => l.StartsWith("1 ", StringComparison.Ordinal) || l.StartsWith("2 ", StringComparison.Ordinal)).ShouldBe(2, "one summary row per output");
        exit.ShouldBe(0);
    }

    [Fact]
    public void End_of_input_stops_the_engine_once_as_interrupt_and_prints_exactly_one_summary()
    {
        var engine = new StubEngine();

        var exit = new PlayCommand(engine).Run(TwoOutputs(), _out, _err, new StringReader(string.Empty));

        engine.StopReasons.ShouldBe([RunEndReason.Interrupt]);
        SummaryCount(_out.ToString()).ShouldBe(1);
        exit.ShouldBe(0);
    }

    [Fact]
    public void Ctrl_C_racing_with_end_of_input_yields_exactly_one_summary_and_one_stop()
    {
        var engine = new StubEngine();
        var interrupted = new ManualResetEventSlim();
        var stdin = new CallbackStdin(() =>
        {
            // Ctrl+C and end of input arrive together: the signal handler runs on its own thread.
            var signal = Task.Run(() =>
            {
                new ShutdownSignals(engine).Interrupt();
                interrupted.Set();
            });
            signal.Wait(Bound, TestContext.Current.CancellationToken);
        });

        var exit = new PlayCommand(engine).Run(TwoOutputs(), _out, _err, stdin);

        interrupted.Wait(Bound, TestContext.Current.CancellationToken).ShouldBeTrue();
        SummaryCount(_out.ToString()).ShouldBe(1, _out.ToString());
        engine.StopReasons.ShouldAllBe(r => r == RunEndReason.Interrupt);
        engine.StopReasons.Count.ShouldBeLessThanOrEqualTo(2);
        exit.ShouldBe(0);
    }

    [Fact]
    public void End_of_input_with_the_real_engine_while_a_second_interrupt_arrives_still_prints_one_summary()
    {
        using var rig = TwoDeviceRig();
        var stdin = new CallbackStdin(() => new ShutdownSignals(rig.Engine).Interrupt());

        var exit = rig.Call(() => new PlayCommand(rig.Engine, rig.Time).Run(TwoOutputs(), _out, _err, stdin));

        SummaryCount(_out.ToString()).ShouldBe(1, _out.ToString());
        exit.ShouldBe(0);
    }

    // ---- F19: a dedicated writer ------------------------------------------------------------------------------

    [Fact]
    public async Task A_blocked_console_writer_does_not_block_the_thread_that_publishes_engine_events()
    {
        using var gate = new BlockingWriter();
        var engine = new StubEngine();
        using var printer = new EventPrinter(engine.Events, gate);

        var publishing = Task.Run(
            () =>
            {
                for (var i = 0; i < 5; i++)
                {
                    printer.OnNext(new SyncRecord(Dev1, TimeSpan.FromSeconds(i), 0, 0, 0));
                }
            },
            TestContext.Current.CancellationToken);

        var finished = await Finishes(publishing);
        gate.Release();

        finished.ShouldBeTrue("publishing engine events must not wait for the console");
    }

    [Fact]
    public async Task Lines_queued_behind_a_blocked_writer_all_appear_in_order_once_it_is_released_and_disposal_flushes_the_queue()
    {
        var gate = new BlockingWriter();
        var engine = new StubEngine();
        var printer = new EventPrinter(engine.Events, gate);
        var publishing = Task.Run(
            () =>
            {
                for (var i = 0; i < 5; i++)
                {
                    printer.OnNext(new SyncRecord(Dev1, TimeSpan.FromSeconds(i), 0, 0, 0));
                }
            },
            TestContext.Current.CancellationToken);
        (await Finishes(publishing)).ShouldBeTrue("publishing engine events must not wait for the console");

        gate.Release();
        printer.Dispose();

        var clocks = SplitLines(gate.ToString()).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[2]).ToArray();
        clocks.ShouldBe(["00:00:00.000", "00:00:01.000", "00:00:02.000", "00:00:03.000", "00:00:04.000"]);
        gate.Dispose();
    }

    [Fact]
    public void Disposing_the_printer_flushes_every_queued_line_to_the_console()
    {
        var engine = new StubEngine();
        var printer = new EventPrinter(engine.Events, _out);

        for (var i = 0; i < 50; i++)
        {
            printer.OnNext(new SyncRecord(Dev1, TimeSpan.FromSeconds(i), 0, 0, 0));
        }

        printer.Dispose();

        SplitLines(_out.ToString()).Length.ShouldBe(50);
    }

    // ---- F11: every console line is also logged ----------------------------------------------------------------

    [Fact]
    public void Every_console_line_of_a_run_is_also_written_to_the_log_with_the_same_text()
    {
        var engine = new StubEngine();
        var log = new CaptureLogger<PlayCommand>();
        var stdin = new StringReader("pause\nresume\nseek +10\nseek -9999\nbogus\nstop\n");

        new PlayCommand(engine, TimeProvider.System, log).Run(TwoOutputs(), _out, _err, stdin);

        var console = SplitLines(_out.ToString()).Concat(SplitLines(_err.ToString())).ToArray();
        console.ShouldContain(l => l.StartsWith("output 1  track 1", StringComparison.Ordinal));
        console.ShouldContain(l => l.StartsWith("started   clock", StringComparison.Ordinal));
        console.ShouldContain(l => l.StartsWith("paused at", StringComparison.Ordinal));
        console.ShouldContain(l => l.StartsWith("resumed at", StringComparison.Ordinal));
        console.ShouldContain(l => l.StartsWith("seek confirmed at", StringComparison.Ordinal));
        console.ShouldContain(l => l.StartsWith("warning:", StringComparison.Ordinal));
        console.ShouldContain(l => l.StartsWith("error cli.unknown_command", StringComparison.Ordinal));
        console.ShouldContain(l => l.StartsWith("run summary", StringComparison.Ordinal));

        var logged = log.Messages.Select(m => m.Trim()).ToHashSet(StringComparer.Ordinal);
        foreach (var line in console)
        {
            logged.ShouldContain(line, $"console line missing from the log: {line}");
        }
    }

    // ---- F8: 1-based output numbers ------------------------------------------------------------------------------

    [Fact]
    public void Losing_the_second_device_of_a_real_run_prints_output_2_like_the_start_lines_and_the_summary()
    {
        using var rig = TwoDeviceRig();
        rig.Start();
        var printer = new EventPrinter(rig.Engine.Events, _out);
        rig.Render(100);

        rig.Devices[1].SimulateLoss();
        rig.Barrier();
        rig.Render(20);
        rig.Stop(RunEndReason.Stop);
        printer.Dispose();

        var lost = SplitLines(_out.ToString()).Single(l => l.StartsWith("device-lost", StringComparison.Ordinal));
        lost.ShouldStartWith("device-lost  output 2  device dev-2");
        lost.ShouldEndWith("others continue");
    }

    [Fact]
    public void Losing_the_first_device_of_a_real_run_prints_output_1()
    {
        using var rig = TwoDeviceRig();
        rig.Start();
        var printer = new EventPrinter(rig.Engine.Events, _out);
        rig.Render(100);

        rig.Devices[0].SimulateLoss();
        rig.Barrier();
        rig.Render(20);
        rig.Stop(RunEndReason.Stop);
        printer.Dispose();

        SplitLines(_out.ToString()).Single(l => l.StartsWith("device-lost", StringComparison.Ordinal))
            .ShouldStartWith("device-lost  output 1  device dev-1");
    }

    private static async Task<bool> Finishes(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ---- doubles ------------------------------------------------------------------------------------------------

    private sealed class CallbackStdin(Action onFirstRead) : TextReader
    {
        public override string? ReadLine()
        {
            onFirstRead();
            return null;
        }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        private readonly Lock _gate = new();
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_gate)
                {
                    return [.. _messages];
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
                _messages.Add(formatter(state, exception));
            }
        }
    }

    /// <summary>A console writer that blocks every write until released.</summary>
    private sealed class BlockingWriter : StringWriter
    {
        private readonly ManualResetEventSlim _released = new();

        public void Release() => _released.Set();

        public override void Write(string? value)
        {
            _released.Wait(Bound);
            base.Write(value);
        }

        public override void Write(char value)
        {
            _released.Wait(Bound);
            base.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _released.Wait(Bound);
            base.WriteLine(value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _released.Set();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Engine whose Stop publishes a summary and whose transport commands answer at once.</summary>
    private sealed class StubEngine : IAudioEngine
    {
        private readonly Lock _gate = new();
        private readonly List<IObserver<EngineEvent>> _observers = [];
        private TimeSpan _position;

        public List<RunEndReason> StopReasons { get; } = [];

        public IObservable<EngineEvent> Events => new Source(this);

        public Result<RunStarted> StartRun(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options)
        {
            Publish(new AudioStarted(TimeSpan.Zero, TimeSpan.FromMilliseconds(500)));
            return Result<RunStarted>.Success(new RunStarted(
                [
                    new StartedOutput(1, "dev-1", "Device 1", new MixFormat(48000, 2, ChannelLayout.Stereo)),
                    new StartedOutput(2, "dev-2", "Device 2", new MixFormat(48000, 2, ChannelLayout.Stereo)),
                ],
                TimeSpan.Zero));
        }

        public Result<TimeSpan> Pause() => Result<TimeSpan>.Success(_position);

        public Result<TimeSpan> Resume() => Result<TimeSpan>.Success(_position);

        public Result<SeekResult> Seek(TimeSpan target)
        {
            var outside = target < TimeSpan.Zero;
            _position = outside ? TimeSpan.Zero : target;
            if (outside)
            {
                Publish(new SeekTargetOutsideFile(target, _position));
            }

            return Result<SeekResult>.Success(new SeekResult(_position, outside, false));
        }

        public void Stop(RunEndReason reason)
        {
            lock (_gate)
            {
                StopReasons.Add(reason);
            }

            Publish(new RunSummary(
                reason,
                TimeSpan.FromSeconds(10),
                [
                    new OutputSummary("dev-1", TimeSpan.FromSeconds(10), 1, 0, 0, 0, []),
                    new OutputSummary("dev-2", TimeSpan.FromSeconds(10), 1, 0, 0, 0, []),
                ]));
        }

        private void Publish(EngineEvent e)
        {
            IObserver<EngineEvent>[] observers;
            lock (_gate)
            {
                observers = [.. _observers];
            }

            foreach (var o in observers)
            {
                o.OnNext(e);
            }
        }

        private sealed class Source(StubEngine owner) : IObservable<EngineEvent>
        {
            public IDisposable Subscribe(IObserver<EngineEvent> observer)
            {
                lock (owner._gate)
                {
                    owner._observers.Add(observer);
                }

                return new Unsubscribe(() =>
                {
                    lock (owner._gate)
                    {
                        owner._observers.Remove(observer);
                    }
                });
            }
        }

        private sealed class Unsubscribe(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
