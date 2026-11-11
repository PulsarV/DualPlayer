namespace DualPlayer.Core.Sync;

/// <summary>
/// Estimates the device clock's rate relative to the master clock, in ppm, from device-position readings since the
/// first one. The base is monotonic elapsed time on the <see cref="TimeProvider"/> timestamp base (the rate the
/// running master clock advances at), not the master position, so pause and seek do not disturb it; device positions
/// stay continuous across commands (ADR-0006).
/// </summary>
public sealed class ClockMismatchEstimator(TimeProvider time)
{
    private long _firstTimestamp;
    private TimeSpan _firstDevice;
    private long _lastTimestamp;
    private TimeSpan _lastDevice;
    private bool _hasFirst;

    public double EstimatedPpm
    {
        get
        {
            if (!_hasFirst)
            {
                return 0;
            }

            var elapsed = time.GetElapsedTime(_firstTimestamp, _lastTimestamp).TotalSeconds;
            return elapsed <= 0 ? 0 : (((_lastDevice - _firstDevice).TotalSeconds / elapsed) - 1) * 1e6;
        }
    }

    /// <summary>Adds a reading: the device's played duration and the monotonic timestamp it was taken at.</summary>
    public void AddReading(long timestamp, TimeSpan deviceElapsed)
    {
        if (!_hasFirst)
        {
            _firstTimestamp = timestamp;
            _firstDevice = deviceElapsed;
            _hasFirst = true;
        }

        _lastTimestamp = timestamp;
        _lastDevice = deviceElapsed;
    }

    public void Reset() => _hasFirst = false;
}
