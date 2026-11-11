using System.Threading.Channels;

namespace DualPlayer.Core.Playback;

/// <summary>
/// The engine-control loop: one dedicated thread that runs commands and backend events one at a time, in the
/// order they were posted, so transport state never races. While <see cref="Polling"/> is set it also calls the
/// poll action between commands. Disposing drains every command already queued, so no caller waits forever.
/// </summary>
internal sealed class EngineControlLoop : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(2);

    private readonly Channel<Action> _channel = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Action _poll;
    private readonly Action<Exception> _onError;
    private readonly Thread _thread;
    private int _disposed;

    public EngineControlLoop(Action poll, Action<Exception> onError)
    {
        _poll = poll;
        _onError = onError;
        _thread = new Thread(Run) { Name = "EngineControlLoop", IsBackground = true };
        _thread.Start();
    }

    /// <summary>Set from the loop thread while the poll action must keep being called.</summary>
    public bool Polling { get; set; }

    public bool IsLoopThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <summary>Queues a command; false when the loop has been disposed.</summary>
    public bool Post(Action command) => _channel.Writer.TryWrite(command);

    /// <summary>Runs <paramref name="command"/> on the loop and returns its result (inline when already on the loop).</summary>
    public T Execute<T>(Func<T> command)
    {
        ObjectDisposedException.ThrowIf(!TryExecute(command, out var result), this);
        return result;
    }

    public void Execute(Action command) => ObjectDisposedException.ThrowIf(!TryExecute(command), this);

    /// <summary>Like <see cref="Execute{T}"/>, but returns false instead of throwing when the loop has been disposed.</summary>
    public bool TryExecute<T>(Func<T> command, out T result)
    {
        if (IsLoopThread)
        {
            result = command();
            return true;
        }

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = Post(() =>
        {
            try
            {
                done.SetResult(command());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        if (!queued)
        {
            result = default!;
            return false;
        }

        result = done.Task.GetAwaiter().GetResult();
        return true;
    }

    public bool TryExecute(Action command) =>
        TryExecute(
            () =>
            {
                command();
                return 0;
            },
            out _);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        if (!IsLoopThread)
        {
            _thread.Join();
        }
    }

    private void Run()
    {
        var reader = _channel.Reader;
        while (true)
        {
            try
            {
                if (reader.TryRead(out var command))
                {
                    command();
                }
                else if (Polling && Volatile.Read(ref _disposed) == 0)
                {
                    _poll();
                    Thread.Sleep(PollInterval);
                }
                else if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    return; // disposed and every queued command has run
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }
    }
}
