using System.Text;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Console.Rendering;

/// <summary>
/// The one writer of console lines: callers enqueue, a dedicated thread writes each line to its stream and logs it with the same
/// text, so a slow or blocked console never stalls the thread that produced the line (contracts/cli.md Conventions).
/// </summary>
internal sealed class ConsoleLineQueue : IDisposable
{
    private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(5);

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private readonly Queue<Entry> _queue = new();
    private readonly Thread _thread;
    private int _pending;
    private bool _completed;
    private bool _writeFailureLogged; // writer thread only

    public ConsoleLineQueue(TextWriter output, TextWriter error, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        _output = output;
        _error = error;
        _log = log;
        _thread = new Thread(Drain) { IsBackground = true, Name = "console-writer" };
        _thread.Start();
    }

    /// <summary>A <see cref="TextWriter"/> whose lines go through this queue, to standard output or standard error.</summary>
    public TextWriter Writer(bool toError) => new QueueWriter(this, toError);

    /// <summary>Queues a line and returns at once; it never waits on the console writer.</summary>
    public void Enqueue(string line, bool toError = false, Action? written = null)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _pending++;
            _queue.Enqueue(new Entry(line, toError, written));
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Waits (bounded) until every queued line has been written.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            WaitIdle(DrainLimit);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            WaitIdle(DrainLimit);
            _completed = true;
            Monitor.PulseAll(_gate);
        }

        _thread.Join(DrainLimit);
    }

    private void WaitIdle(TimeSpan limit)
    {
        var deadline = Environment.TickCount64 + (long)limit.TotalMilliseconds;
        while (_pending > 0)
        {
            var left = deadline - Environment.TickCount64;
            if (left <= 0 || !Monitor.Wait(_gate, (int)left))
            {
                return;
            }
        }
    }

    private void Drain()
    {
        while (true)
        {
            Entry entry;
            lock (_gate)
            {
                while (_queue.Count == 0)
                {
                    if (_completed)
                    {
                        return;
                    }

                    Monitor.Wait(_gate);
                }

                entry = _queue.Dequeue();
            }

            // The log never depends on the console: the line is logged first, whatever the write does next.
            _log?.LogInformation("{Line}", entry.Line);
            try
            {
                var target = entry.ToError ? _error : _output;
                target.WriteLine(entry.Line);
                target.Flush();
            }
            catch (Exception ex)
            {
                // A console that cannot be written must not kill the loop or strand callers waiting for the queue to
                // drain; the failure is logged once, not for every line after it.
                if (!_writeFailureLogged)
                {
                    _writeFailureLogged = true;
                    _log?.LogError(ex, "Writing a console line failed");
                }
            }

            try
            {
                // Whoever waits on this line (the run's end) is told it is done, written or not.
                entry.Written?.Invoke();
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Signalling a written console line failed");
            }
            finally
            {
                lock (_gate)
                {
                    _pending--;
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }

    private readonly record struct Entry(string Line, bool ToError, Action? Written);

    private sealed class QueueWriter(ConsoleLineQueue owner, bool toError) : TextWriter
    {
        private readonly StringBuilder _partial = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => _partial.Append(value);

        public override void WriteLine() => WriteLine(string.Empty);

        public override void WriteLine(string? value)
        {
            var line = _partial.Append(value).ToString();
            _partial.Clear();
            owner.Enqueue(line, toError);
        }
    }
}
