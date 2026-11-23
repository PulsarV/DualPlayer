using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>A fake clock that counts the timers (and so the delays) asked of it: a command that waits on fake time shows up here.</summary>
internal sealed class CountingTimeProvider : FakeTimeProvider
{
    [ThreadStatic]
    private static bool _firing;

    [ThreadStatic]
    private static bool _inReadHook;

    private int _timers;
    private int _fakeWaits;
    private int _waiters;
    private int _commandThread;
    private TimeSpan _folderWork;
    private int _scanThread;
    private int _armed;

    public int TimersCreated => Volatile.Read(ref _timers);

    /// <summary>
    /// How many times a fake (a scripted decoder or probe) has begun spending fake time, plus the timers the command waits on: those
    /// it creates itself and the periodic ticks of the engine and session it starts. Only these move fake time on during a command;
    /// a one-shot timer another thread creates (the DecodePump re-arming its 10 ms wait) and the real-time clock do not.
    /// </summary>
    public int Signals => Volatile.Read(ref _fakeWaits) + Volatile.Read(ref _commandTimers);

    private int _commandTimers;
    private int _declined;
    private readonly AutoResetEvent _pulse = new(false);

    /// <summary>
    /// How many times the rig's gate has looked at the running command and kept fake time still: it wakes on every timer, signal and
    /// waiter, and on the command finishing, so a test that waits for this to grow has seen the gate refuse, with no real-time wait.
    /// </summary>
    public int GateDeclined => Volatile.Read(ref _declined);

    /// <summary>Something the gate looks at has changed (a timer, a signal, a waiter, the command finishing): wakes the gate.</summary>
    public void Pulse() => _pulse.Set();

    /// <summary>The gate has judged the command and keeps fake time still; it sleeps until the next <see cref="Pulse"/>. False after <paramref name="failAfter"/>.</summary>
    public bool WaitForPulse(TimeSpan failAfter)
    {
        Interlocked.Increment(ref _declined);
        return _pulse.WaitOne(failAfter);
    }

    /// <summary>Waits (a failure bound, not a pass condition) until the gate has declined at least <paramref name="count"/> times.</summary>
    public bool WaitForGateDeclined(int count, TimeSpan failAfter) => SpinWait.SpinUntil(() => GateDeclined >= count, failAfter);

    /// <summary>Names the thread whose own timers count as a wait on fake time (0 for none).</summary>
    public void WatchCommandThread(int threadId) => Volatile.Write(ref _commandThread, threadId);

    /// <summary>How many fakes are spending or blocked on fake time right now: while any is, fake time moves on; when none is, it stands still.</summary>
    public int Waiters => Volatile.Read(ref _waiters);

    private int _generation;

    /// <summary>A new command starts: a waiter that entered in an earlier call and never left its scope no longer counts, so it cannot move fake time now.</summary>
    public void BeginCall()
    {
        lock (_waitersLock)
        {
            _generation++;
            _deadlines.Clear();
            Volatile.Write(ref _waiters, 0);
        }

        Pulse();
    }

    private readonly object _waitersLock = new();

    /// <summary>A fake tells the rig that its command now waits on fake time for good (an engine command), so fake time may move.</summary>
    public void SignalWait()
    {
        Interlocked.Increment(ref _fakeWaits);
        Pulse();
    }

    /// <summary>A fake is blocked in real time until the session gives up on it by fake time: fake time moves on until the scope ends.</summary>
    public IDisposable Waiting() => Waiting(null);

    private Scope Waiting(long? deadline)
    {
        int generation;
        lock (_waitersLock)
        {
            generation = _generation;
            Interlocked.Increment(ref _waiters);
            if (deadline is not null)
            {
                _deadlines.Add(deadline.Value);
            }
        }

        Pulse();
        return new Scope(this, generation, deadline);
    }

    private readonly List<long> _deadlines = [];

    /// <summary>
    /// How much fake time the rig may still move on before it reaches the earliest deadline of a fake that is spinning on it
    /// (<see cref="SpinUntil"/>): null when no fake has a deadline, zero (or less) when one has been reached and must leave its scope first.
    /// </summary>
    public TimeSpan? RoomBeforeDeadline()
    {
        long? earliest;
        lock (_waitersLock)
        {
            earliest = _deadlines.Count == 0 ? null : _deadlines.Min();
        }

        if (earliest is null)
        {
            return null;
        }

        var left = earliest.Value - base.GetTimestamp();
        return left <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)Math.Ceiling(left * (double)TimeSpan.TicksPerSecond / TimestampFrequency));
    }

    /// <summary>Called by a fake spending fake time: signals the rig that fake time may move, then spins until it has reached <paramref name="end"/> or <paramref name="cancel"/> fires (which ends the waiting scope inside the cancel).</summary>
    public void SpinUntil(long end, CancellationToken cancel)
    {
        if (GetTimestamp() >= end)
        {
            return;
        }

        // The scope also ends from the cancel itself, so fake time stops moving inside Cancel() and a grace the session starts right
        // after it cannot be spent by the rig before the fake has even seen the cancel.
        // The scope also carries the fake's deadline, so the rig never moves fake time past it while this thread waits for the CPU.
        using var scope = Waiting(end);
        using var _ = cancel.Register(scope.Dispose);
        while (GetTimestamp() < end && !cancel.IsCancellationRequested)
        {
            Thread.Yield(); // reads fake time often, so a fast-moving clock overshoots its scripted cost by a step at most
        }
    }

    private sealed class Scope(CountingTimeProvider owner, int generation, long? deadline) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                lock (owner._waitersLock)
                {
                    if (deadline is not null)
                    {
                        owner._deadlines.Remove(deadline.Value);
                    }

                    if (owner._generation == generation)
                    {
                        Interlocked.Decrement(ref owner._waiters);
                    }
                }
            }
        }
    }

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

    /// <summary>
    /// When set, runs before a read of the time (a test can hold the thread that reads it there, as an OS that starts a thread
    /// late would); it is not run again for the reads it makes itself.
    /// </summary>
    public Action? BeforeRead { get; set; }

    public override long GetTimestamp()
    {
        if (Volatile.Read(ref _armed) == 1 && !_firing)
        {
            ScanReadsTime();
        }

        var hook = BeforeRead;
        if (hook is not null && !_inReadHook)
        {
            _inReadHook = true;
            try
            {
                hook();
            }
            finally
            {
                _inReadHook = false;
            }
        }

        return base.GetTimestamp();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timers);
        if (period != Timeout.InfiniteTimeSpan || Environment.CurrentManagedThreadId == Volatile.Read(ref _commandThread))
        {
            Interlocked.Increment(ref _commandTimers);
        }

        var timer = base.CreateTimer(callback, state, dueTime, period);
        Pulse();
        return timer;
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

internal static class FakeTimeSpin
{
    /// <summary>A fake blocks in real time until the session gives up on it by fake time: on a rig clock fake time moves while the scope lasts.</summary>
    public static IDisposable Waiting(this TimeProvider? time) => (time as CountingTimeProvider)?.Waiting() ?? new NoScope();

    private sealed class NoScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    /// <summary>A scripted fake spends fake time up to <paramref name="end"/>; on a rig clock this is the signal that lets fake time move.</summary>
    public static void SpinUntil(this TimeProvider time, long end, CancellationToken cancel = default)
    {
        if (time is CountingTimeProvider counting)
        {
            counting.SpinUntil(end, cancel);
            return;
        }

        while (time.GetTimestamp() < end && !cancel.IsCancellationRequested)
        {
            Thread.Sleep(1);
        }
    }
}
