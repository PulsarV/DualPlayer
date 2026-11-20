namespace DualPlayer.Core.Sync;

/// <summary>An output's lag behind the master clock, moved toward its target at up to <see cref="MaxRatePpm"/> (ADR-0005).</summary>
public sealed class LagRamp
{
    public const double MaxRatePpm = 2000;

    private double _currentTicks;
    private long _targetTicks;

    public TimeSpan Current => TimeSpan.FromTicks((long)Math.Round(_currentTicks));

    public TimeSpan Target => TimeSpan.FromTicks(_targetTicks);

    public bool IsMoving => _currentTicks != _targetTicks;

    /// <summary>Gets the signed ramp rate in ppm; positive while the lag grows.</summary>
    public double RatePpm => _currentTicks < _targetTicks ? MaxRatePpm : _currentTicks > _targetTicks ? -MaxRatePpm : 0;

    public void Advance(TimeSpan elapsed)
    {
        var step = elapsed.Ticks * MaxRatePpm * 1e-6;
        var remaining = _targetTicks - _currentTicks;
        _currentTicks = Math.Abs(remaining) <= step ? _targetTicks : _currentTicks + (Math.Sign(remaining) * step);
    }

    public void Retarget(TimeSpan target) => _targetTicks = target.Ticks;

    public void SetAtOnce(TimeSpan lag)
    {
        _currentTicks = lag.Ticks;
        _targetTicks = lag.Ticks;
    }
}
