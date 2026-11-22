using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>A fake clock that counts the timers (and so the delays) asked of it: a command that waits on fake time shows up here.</summary>
internal sealed class CountingTimeProvider : FakeTimeProvider
{
    private int _timers;

    public int TimersCreated => Volatile.Read(ref _timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timers);
        return base.CreateTimer(callback, state, dueTime, period);
    }
}
