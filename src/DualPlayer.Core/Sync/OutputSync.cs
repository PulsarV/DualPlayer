namespace DualPlayer.Core.Sync;

/// <summary>One 1 s record interval of an output: its average drift and the drift to remove in one step (zero for none).</summary>
public readonly record struct SyncInterval(TimeSpan AverageDrift, TimeSpan Realignment);

/// <summary>
/// One output's sync state, driven by its single owner (the engine-control loop): drift readings feed the
/// continuous rate correction, and once per second <see cref="CloseInterval"/> yields the record's average drift and
/// decides a one-step re-alignment when the error is too large for the clamped rate (sad.md Flow 7). Not thread-safe.
/// </summary>
public sealed class OutputSync(MasterClock clock, TimeSpan driftTolerance)
{
    /// <summary>Spec §6: every output is back within the drift tolerance at most this long after a disturbance.</summary>
    public static readonly TimeSpan RecoveryTime = TimeSpan.FromSeconds(1);

    /// <summary>The most drift the clamped continuous correction can remove within <see cref="RecoveryTime"/>.</summary>
    public static readonly TimeSpan RateCorrectableDrift =
        TimeSpan.FromSeconds(DriftController.MaxCorrectionPpm * 1e-6 * RecoveryTime.TotalSeconds);

    private readonly DriftMeter _meter = new(clock);
    private readonly DriftController _controller = new();
    private TimeSpan _latestDrift;

    /// <summary>
    /// Gets the drift beyond which the rate adjustment cannot bring the output back within the tolerance in
    /// <see cref="RecoveryTime"/>, so it is removed in one step instead.
    /// </summary>
    public TimeSpan RealignThreshold { get; } = driftTolerance + RateCorrectableDrift;

    public double CorrectionPpm => _controller.CorrectionPpm;

    /// <summary>Gets the ratio to hand to the output's adaptive resampler.</summary>
    public double ResamplerRatio => _controller.ResamplerRatio;

    /// <summary>
    /// Records one drift reading: <paramref name="contentPosition"/> is heard at <paramref name="timestamp"/>.
    /// When <paramref name="correct"/> is set the reading also drives the rate correction over <paramref name="elapsed"/>.
    /// </summary>
    public TimeSpan AddReading(TimeSpan contentPosition, long timestamp, TimeSpan elapsed, bool correct)
    {
        _latestDrift = _meter.Record(contentPosition, timestamp);
        if (correct)
        {
            _controller.Update(_latestDrift, elapsed);
        }

        return _latestDrift;
    }

    /// <summary>
    /// Closes the record interval. When <paramref name="mayRealign"/> and both the interval's average and the latest
    /// reading are beyond <see cref="RealignThreshold"/>, returns the latest drift as the whole error to remove in one
    /// step; the caller applies it and counts one correction.
    /// </summary>
    public SyncInterval CloseInterval(bool mayRealign)
    {
        var average = _meter.TakeAverage();
        if (!mayRealign || average.Duration() <= RealignThreshold || _latestDrift.Duration() <= RealignThreshold)
        {
            return new SyncInterval(average, TimeSpan.Zero);
        }

        var realignment = _latestDrift;
        _controller.Realigned(_controller.FilteredDrift); // the error is gone: the filter must not keep pushing on it
        _latestDrift = TimeSpan.Zero;
        return new SyncInterval(average, realignment);
    }

    /// <summary>Back to unity rate; call on start, resume and seek.</summary>
    public void Reset() => _controller.Reset();
}
