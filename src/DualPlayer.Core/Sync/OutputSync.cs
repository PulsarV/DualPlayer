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

    /// <summary>
    /// A drift step is confirmed by this many readings in a row on the same side, the first beyond
    /// <see cref="RealignThreshold"/> and the rest still beyond the tolerance:
    /// one stray reading does not move the output, and at one reading per 100 ms a step beyond that threshold is
    /// removed within the 0.5 s of spec §6. A smaller error is left to the rate correction.
    /// </summary>
    public const int StepReadings = 3;

    private readonly DriftMeter _meter = new(clock);
    private readonly DriftController _controller = new();
    private readonly LagRamp _lag = new();
    private TimeSpan _latestDrift;
    private int _readingsBeyond;
    private int _beyondSide;

    /// <summary>
    /// Gets the drift beyond which the rate adjustment cannot bring the output back within the tolerance in
    /// <see cref="RecoveryTime"/>, so it is removed in one step instead.
    /// </summary>
    public TimeSpan RealignThreshold { get; } = driftTolerance + RateCorrectableDrift;

    /// <summary>Gets the output's current lag behind the master clock (the ramp's current value).</summary>
    public TimeSpan Lag => _lag.Current;

    public void RetargetLag(TimeSpan target)
    {
        _lag.Retarget(target);
        _controller.RampPpm = _lag.RatePpm;
    }

    public void SetLagAtOnce(TimeSpan lag)
    {
        _lag.SetAtOnce(lag);
        _controller.RampPpm = 0;
    }

    public void AdvanceLag(TimeSpan elapsed)
    {
        _lag.Advance(elapsed);
        _controller.RampPpm = _lag.RatePpm;
    }

    public double CorrectionPpm => _controller.CorrectionPpm;

    /// <summary>Gets the ratio to hand to the output's adaptive resampler.</summary>
    public double ResamplerRatio => _controller.ResamplerRatio;

    /// <summary>
    /// Records one drift reading: <paramref name="contentPosition"/> is heard at <paramref name="timestamp"/>.
    /// When <paramref name="correct"/> is set the reading also drives the rate correction over <paramref name="elapsed"/>.
    /// </summary>
    public TimeSpan AddReading(TimeSpan contentPosition, long timestamp, TimeSpan elapsed, bool correct)
    {
        _latestDrift = _meter.Record(contentPosition, timestamp, _lag.Current);
        if (correct)
        {
            _controller.Update(_latestDrift, elapsed);
            CountBeyond();
        }

        return _latestDrift;
    }

    /// <summary>
    /// A drift step (the device stopped taking audio for a moment, or its position jumped): when
    /// <paramref name="mayRealign"/> and the last <see cref="StepReadings"/> rate-corrected readings were all beyond
    /// <see cref="RealignThreshold"/> on the same side, returns the latest drift as the whole error to remove in one
    /// step now, without waiting for the record interval to close; the caller applies it and counts one correction.
    /// Zero for none.
    /// </summary>
    public TimeSpan TakeStepRealignment(bool mayRealign)
    {
        if (!mayRealign || _readingsBeyond < StepReadings)
        {
            return TimeSpan.Zero;
        }

        return TakeRealignment();
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

        return new SyncInterval(average, TakeRealignment());
    }

    /// <summary>Back to unity rate; call on start, resume and seek.</summary>
    public void Reset()
    {
        _controller.Reset();
        _controller.RampPpm = _lag.RatePpm;
        _readingsBeyond = 0;
    }

    private void CountBeyond()
    {
        // Once a reading is beyond the threshold, the following ones only have to stay beyond the tolerance: the rate
        // correction is already shrinking the error, and a step just over the threshold must not slip back under it.
        var bar = _readingsBeyond > 0 ? driftTolerance : RealignThreshold;
        var side = _latestDrift.Duration() <= bar ? 0 : Math.Sign(_latestDrift.Ticks);
        _readingsBeyond = side == 0 ? 0 : side == _beyondSide ? _readingsBeyond + 1 : 1;
        _beyondSide = side;
    }

    private TimeSpan TakeRealignment()
    {
        var realignment = _latestDrift;
        _controller.Realigned(_controller.FilteredDrift); // the error is gone: the filter must not keep pushing on it
        _latestDrift = TimeSpan.Zero;
        _readingsBeyond = 0;
        return realignment;
    }
}
