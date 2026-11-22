using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>A fake clock that counts the timers (and so the delays) asked of it: a command that waits on fake time shows up here.</summary>
internal sealed class CountingTimeProvider : FakeTimeProvider
{
    [ThreadStatic]
    private static bool _firing;

    private int _timers;
    private TimeSpan _folderWork;
    private int _scanThread;
    private int _armed;

    public int TimersCreated => Volatile.Read(ref _timers);

    /// <summary>
    /// Makes the next neighbour scan (<c>ChangeFile</c>) spend <paramref name="cost"/> of fake time on the folder work it does
    /// right after reading the time it counts its budget from: the enumeration, the sort and the file stats, which the fake
    /// folder of a test cannot make slow on its own. The clock moves at the scan's next read of the time.
    /// </summary>
    public void SlowNextFolderScan(TimeSpan cost)
    {
        _folderWork = cost;
        Volatile.Write(ref _scanThread, 0);
        Volatile.Write(ref _armed, 1);
    }

    public override long GetTimestamp()
    {
        if (Volatile.Read(ref _armed) == 1 && !_firing)
        {
            ScanReadsTime();
        }

        return base.GetTimestamp();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timers);
        return base.CreateTimer(callback, state, dueTime, period);
    }

    private void ScanReadsTime()
    {
        var me = Environment.CurrentManagedThreadId;
        var scanThread = Volatile.Read(ref _scanThread);
        if (scanThread == 0)
        {
            // The scan's first read is the time its budget counts from; it is found by the frame it reads in.
            if (new StackTrace().GetFrames().Any(f => f.GetMethod()?.Name == "ChangeFile"))
            {
                Volatile.Write(ref _scanThread, me);
            }

            return;
        }

        if (scanThread == me && Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
        {
            _firing = true;
            try
            {
                Advance(_folderWork);
            }
            finally
            {
                _firing = false;
            }
        }
    }
}
