namespace DualPlayer.Core.Diagnostics;

/// <summary>Tracks stretches where one output is beyond the drift tolerance, outside recovery windows.</summary>
public sealed class ExcursionTracker(string deviceId, double toleranceMs = 10.0)
{
    private TimeSpan? _start;
    private double _peakMs;

    public Excursion? Observe(TimeSpan clockPosition, double driftMs, bool inRecoveryWindow)
    {
        if (inRecoveryWindow)
        {
            return null;
        }

        var magnitude = Math.Abs(driftMs);
        if (magnitude > toleranceMs)
        {
            _start ??= clockPosition;
            _peakMs = Math.Max(_peakMs, magnitude);
            return null;
        }

        return Close(clockPosition);
    }

    public Excursion? Finish(TimeSpan clockPosition) => Close(clockPosition);

    private Excursion? Close(TimeSpan end)
    {
        if (_start is not { } start)
        {
            return null;
        }

        var excursion = new Excursion(deviceId, start, end - start, _peakMs);
        _start = null;
        _peakMs = 0;
        return excursion;
    }
}
