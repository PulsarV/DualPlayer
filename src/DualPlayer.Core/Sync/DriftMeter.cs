namespace DualPlayer.Core.Sync;

/// <summary>
/// Measures an output's drift: content position of the frame the device reports played minus the master position at the reading.
/// Not thread-safe by design: <see cref="Record"/> and <see cref="TakeAverage"/> must be called by the single owner — the
/// output's render thread — which may not lock (sad.md §8 real-time rule); the owner hands the 1 s average to the
/// engine-control loop through its own atomic publication.
/// </summary>
public sealed class DriftMeter(MasterClock clock)
{
    private double _sumTicks;
    private long _count;

    public TimeSpan Record(TimeSpan contentPosition, long readingTimestamp)
    {
        var drift = contentPosition - clock.PositionAt(readingTimestamp);
        _sumTicks += drift.Ticks;
        _count++;
        return drift;
    }

    /// <summary>Average of the readings since the previous call (the 1 s record average); zero when there were none.</summary>
    public TimeSpan TakeAverage()
    {
        var average = _count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)(_sumTicks / _count));
        _sumTicks = 0;
        _count = 0;
        return average;
    }
}
