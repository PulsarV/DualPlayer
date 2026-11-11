namespace DualPlayer.Core.Sync;

/// <summary>
/// PI controller from drift (content minus master, positive = content ahead) to a near-unity rate, plus the
/// one-step re-alignment decision for errors too large for the rate adjustment (sad.md Flow 7, ADR-0003).
/// The drift readings are low-pass filtered so device-position jitter does not swing the rate.
/// Not thread-safe: owned by one output's render thread; never allocates, locks or throws.
/// </summary>
public sealed class DriftController
{
    /// <summary>Rate authority of the continuous correction, in ppm (ADR-0003: ratios within a few hundred ppm).</summary>
    public const double MaxCorrectionPpm = 500;

    /// <summary>Filtered drift beyond this is removed by a one-step re-alignment instead of the rate adjustment.</summary>
    public static readonly TimeSpan RealignThreshold = TimeSpan.FromMilliseconds(4);

    private const double Kp = 0.2;            // rate change per second of error, per second
    private const double Ki = 0.02;           // per second squared (damping ≈ 0.7)
    private const double FilterSeconds = 1.0; // drift low-pass time constant

    private double _filteredSeconds;
    private bool _hasReading;
    private double _integralSeconds;
    private double _correctionPpm;

    /// <summary>Gets the current continuous correction in ppm; positive slows content consumption.</summary>
    public double CorrectionPpm => _correctionPpm;

    /// <summary>Gets content frames consumed per output frame (below 1 when content is ahead).</summary>
    public double RateRatio => 1 - (_correctionPpm * 1e-6);

    /// <summary>
    /// Gets the value to pass as <c>ratio</c> to <see cref="AdaptiveResampler.Process"/> (output frames per input
    /// frame) — the inverse of <see cref="RateRatio"/>; above 1 when content is ahead.
    /// </summary>
    public double ResamplerRatio => 1 / RateRatio;

    /// <summary>Gets the low-pass filtered drift the controller acts on.</summary>
    public TimeSpan FilteredDrift => TimeSpan.FromSeconds(_filteredSeconds);

    /// <summary>
    /// Gets the drift to remove in one step (the caller re-aligns, then calls <see cref="Realigned"/>);
    /// zero while the filtered drift is within <see cref="RealignThreshold"/>.
    /// </summary>
    public TimeSpan PendingRealignment =>
        Math.Abs(_filteredSeconds) > RealignThreshold.TotalSeconds ? FilteredDrift : TimeSpan.Zero;

    /// <summary>Converts a drift to remove into the frame count for <see cref="AdaptiveResampler.Realign"/> (content ahead → insert, negative).</summary>
    public static int ResamplerRealignFrames(TimeSpan drift, int sampleRate) =>
        -(int)Math.Round(drift.TotalSeconds * sampleRate);

    /// <summary>Back to unity with no history; call on start, resume and seek.</summary>
    public void Reset()
    {
        _filteredSeconds = 0;
        _hasReading = false;
        _integralSeconds = 0;
        _correctionPpm = 0;
    }

    /// <summary>Tells the controller that <paramref name="removed"/> of drift was removed in one step.</summary>
    public void Realigned(TimeSpan removed) => _filteredSeconds -= removed.TotalSeconds;

    public void Update(TimeSpan drift, TimeSpan elapsed)
    {
        var error = drift.TotalSeconds;
        var dt = elapsed.TotalSeconds;
        if (_hasReading)
        {
            _filteredSeconds += (error - _filteredSeconds) * (dt / (FilterSeconds + dt));
        }
        else
        {
            _filteredSeconds = error;
            _hasReading = true;
        }

        var filtered = _filteredSeconds;
        var candidateIntegral = _integralSeconds + (filtered * dt);
        var raw = (Kp * filtered) + (Ki * candidateIntegral);
        var limit = MaxCorrectionPpm * 1e-6;
        var clamped = Math.Clamp(raw, -limit, limit);

        // Anti-windup: only integrate while the output is not saturated.
        if (raw == clamped)
        {
            _integralSeconds = candidateIntegral;
        }

        _correctionPpm = clamped * 1e6;
    }
}
