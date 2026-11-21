namespace DualPlayer.Core.Sync;

/// <summary>An output's lag behind the master clock, moved toward its target at up to <see cref="MaxRatePpm"/> (ADR-0005).</summary>
public sealed class LagRamp
{
    public const double MaxRatePpm = 2000;

    private double _currentTicks;
    private long _targetTicks;
    private double _ratePpm = MaxRatePpm;

    public TimeSpan Current => TimeSpan.FromTicks((long)Math.Round(_currentTicks));

    public TimeSpan Target => TimeSpan.FromTicks(_targetTicks);

    public bool IsMoving => _currentTicks != _targetTicks;

    /// <summary>Gets the signed ramp rate in ppm; positive while the lag grows.</summary>
    public double RatePpm => _currentTicks < _targetTicks ? _ratePpm : _currentTicks > _targetTicks ? -_ratePpm : 0;

    public void Advance(TimeSpan elapsed)
    {
        var step = elapsed.Ticks * _ratePpm * 1e-6;
        var remaining = _targetTicks - _currentTicks;
        _currentTicks = Math.Abs(remaining) <= step ? _targetTicks : _currentTicks + (Math.Sign(remaining) * step);
    }

    /// <summary>
    /// Moves toward <paramref name="target"/>: over <paramref name="duration"/> when one is given, so it arrives together
    /// with a longer move (a picture-delay move, AC-14), but never faster than <see cref="MaxRatePpm"/>.
    /// </summary>
    public void Retarget(TimeSpan target, TimeSpan duration = default)
    {
        _targetTicks = target.Ticks;
        var remaining = Math.Abs(_targetTicks - _currentTicks);
        _ratePpm = duration > TimeSpan.Zero && remaining > 0
            ? Math.Min(MaxRatePpm, remaining / duration.Ticks * 1e6)
            : MaxRatePpm;
    }

    public void SetAtOnce(TimeSpan lag)
    {
        _currentTicks = lag.Ticks;
        _targetTicks = lag.Ticks;
        _ratePpm = MaxRatePpm;
    }
}
