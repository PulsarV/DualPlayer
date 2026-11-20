using DualPlayer.App.ViewModels;
using DualPlayer.Core;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.Tests.ViewModels;

public sealed class CommandQueueTests
{
    private readonly ListLogger _log = new();

    [Fact]
    public void Posting_returns_at_once_while_an_earlier_command_is_still_running()
    {
        using var queue = new CommandQueue(_log);
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        queue.Post("slow", () =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return Result.Success();
        });
        started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        queue.Post("next", Result.Success);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(100));
        release.Set();
    }

    [Fact]
    public void Commands_run_one_at_a_time_in_posting_order_on_a_thread_other_than_the_caller()
    {
        using var queue = new CommandQueue(_log);
        var order = new List<int>();
        var inside = 0;
        var overlapped = false;
        var caller = Environment.CurrentManagedThreadId;
        var threads = new HashSet<int>();
        using var done = new CountdownEvent(20);
        for (var i = 0; i < 20; i++)
        {
            var n = i;
            queue.Post($"c{n}", () =>
            {
                overlapped |= Interlocked.Increment(ref inside) > 1;
                lock (order)
                {
                    order.Add(n);
                    threads.Add(Environment.CurrentManagedThreadId);
                }

                Thread.Sleep(1);
                Interlocked.Decrement(ref inside);
                done.Signal();
                return Result.Success();
            });
        }

        done.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();
        order.ShouldBe(Enumerable.Range(0, 20));
        overlapped.ShouldBeFalse();
        threads.ShouldNotContain(caller);
    }

    [Fact]
    public void A_failed_command_is_logged_with_its_error_and_later_commands_still_run()
    {
        using var queue = new CommandQueue(_log);
        using var ran = new ManualResetEventSlim();
        queue.Post("next-file", () => Result.Failure("outputs.device_not_found", "dev-9 is not a connected playback device"));
        queue.Post("pause", () =>
        {
            ran.Set();
            return Result.Success();
        });

        ran.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();
        _log.Records.ShouldContain(r => r.Level == LogLevel.Warning && r.Message.Contains("next-file") && r.Message.Contains("outputs.device_not_found"));
    }

    [Fact]
    public void A_command_that_throws_is_logged_and_later_commands_still_run()
    {
        using var queue = new CommandQueue(_log);
        using var ran = new ManualResetEventSlim();
        queue.Post("seek", () => throw new InvalidOperationException("boom"));
        queue.Post("pause", () =>
        {
            ran.Set();
            return Result.Success();
        });

        ran.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();
        _log.Records.ShouldContain(r => r.Level == LogLevel.Error && r.Exception is InvalidOperationException);
    }

    private sealed class ListLogger : ILogger
    {
        private readonly Lock _gate = new();
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _records = [];

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Records
        {
            get
            {
                lock (_gate)
                {
                    return [.. _records];
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
                _records.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
