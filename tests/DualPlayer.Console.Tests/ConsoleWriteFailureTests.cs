using DualPlayer.Console.Rendering;
using DualPlayer.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Console.Tests;

/// <summary>A console that cannot be written, or writes late, must not lose the log of a line or strand the run's end.</summary>
public sealed class ConsoleWriteFailureTests
{
    private static RunSummary Summary() =>
        new(RunEndReason.FileEnd, TimeSpan.FromSeconds(10), [new OutputSummary("dev-1", TimeSpan.FromSeconds(10), 1.0, 0, 0, 0, [])]);

    [Fact]
    public void When_every_console_write_fails_each_line_is_still_logged_and_the_run_still_ends()
    {
        var engine = new Events();
        var log = new CapturingLog();
        using var printer = new EventPrinter(engine, new FailingWriter(), log);

        engine.Publish(new SyncRecord("dev-1", TimeSpan.FromSeconds(1), 0, 0, 0));
        engine.Publish(Summary());

        printer.RunEnded.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)).ShouldBeTrue("the run end was never signalled");
        printer.Flush();
        log.Information.ShouldContain(l => l.StartsWith("record", StringComparison.Ordinal));
        log.Information.ShouldContain(l => l.StartsWith("run summary", StringComparison.Ordinal));
        log.Information.ShouldContain(l => l.StartsWith("1 ", StringComparison.Ordinal) && l.Contains("dev-1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_summary_written_after_the_printer_gave_up_waiting_is_not_reported_as_a_write_failure()
    {
        var engine = new Events();
        var log = new CapturingLog();
        var writer = new GatedWriter();
        using var lines = new ConsoleLineQueue(writer, writer, log, TimeSpan.FromMilliseconds(100));
        var printer = new EventPrinter(engine, lines);

        engine.Publish(Summary());
        printer.Dispose(); // the console is stalled: the bounded flush (100 ms here) gives up and the printer goes away
        writer.Release();
        writer.AllWritten.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();
        Thread.Sleep(100);

        log.Errors.ShouldBeEmpty();
    }

    private sealed class Events : IObservable<EngineEvent>
    {
        private IObserver<EngineEvent>? _observer;

        public void Publish(EngineEvent e) => _observer?.OnNext(e);

        public IDisposable Subscribe(IObserver<EngineEvent> observer)
        {
            _observer = observer;
            return new Unsubscriber(this);
        }

        private sealed class Unsubscriber(Events owner) : IDisposable
        {
            public void Dispose() => owner._observer = null;
        }
    }

    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("disk full");

        public override void Flush() => throw new IOException("disk full");
    }

    private sealed class GatedWriter : StringWriter
    {
        private readonly ManualResetEventSlim _released = new();
        private int _lines;

        public ManualResetEventSlim AllWritten { get; } = new();

        public void Release() => _released.Set();

        public override void WriteLine(string? value)
        {
            _released.Wait();
            base.WriteLine(value);
            if (Interlocked.Increment(ref _lines) >= 3)
            {
                AllWritten.Set();
            }
        }
    }

    private sealed class CapturingLog : ILogger
    {
        private readonly Lock _gate = new();
        private readonly List<string> _information = [];
        private readonly List<string> _errors = [];

        public IReadOnlyList<string> Information
        {
            get
            {
                lock (_gate)
                {
                    return [.. _information];
                }
            }
        }

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_gate)
                {
                    return [.. _errors];
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
                (logLevel >= LogLevel.Warning ? _errors : _information).Add(formatter(state, exception));
            }
        }
    }
}
