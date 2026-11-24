using Microsoft.Extensions.Logging;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>Keeps every formatted log message.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];
    private readonly Lock _gate = new();

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
