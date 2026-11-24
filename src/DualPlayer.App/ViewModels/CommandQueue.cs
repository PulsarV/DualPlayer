using System.Threading.Channels;
using DualPlayer.Core;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.ViewModels;

/// <summary>
/// Runs posted session commands one at a time, in posting order, on its own thread, so a key handled on the UI thread
/// never waits for a folder scan, a probe, a pre-roll or an mpv load. A failed result or an exception is logged.
/// </summary>
internal sealed partial class CommandQueue : IDisposable
{
    private readonly Channel<(string Name, Func<Result> Command)> _channel =
        Channel.CreateUnbounded<(string, Func<Result>)>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ILogger _logger;
    private readonly Thread _thread;

    public CommandQueue(ILogger logger)
    {
        _logger = logger;
        _thread = new Thread(Run) { Name = "SessionCommandQueue", IsBackground = true };
        _thread.Start();
    }

    /// <summary>Queues <paramref name="command"/> and returns at once; a failed result is logged.</summary>
    public void Post(string name, Func<Result> command) => _channel.Writer.TryWrite((name, command));

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private void Run()
    {
        var reader = _channel.Reader;
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out var item))
            {
                try
                {
                    var result = item.Command();
                    if (!result.IsSuccess)
                    {
                        LogFailed(item.Name, result.Error.Code, result.Error.Message);
                    }
                }
                catch (Exception ex)
                {
                    LogFault(ex, item.Name);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Command {Command} failed: {Code} {Message}")]
    private partial void LogFailed(string command, string code, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Command {Command} threw")]
    private partial void LogFault(Exception exception, string command);
}
