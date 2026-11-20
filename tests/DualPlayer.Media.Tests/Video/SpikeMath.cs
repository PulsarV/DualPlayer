namespace DualPlayer.Media.Tests.Video;

/// <summary>Verdict of one ADR-0002 kill-criterion line, decided by the numbers alone.</summary>
public enum SpikeVerdict
{
    Pass,
    Fail,
    NotMeasured,
}

/// <summary>Spread of absolute presented-vs-due errors, in milliseconds.</summary>
public readonly record struct ErrorSummary(int Count, double P50, double P95, double P99, double Max);

/// <summary>Pure computation of the libmpv spike (D1): percentiles, recovery time and one verdict per kill-criterion line.</summary>
public static class SpikeMath
{
    public const double SteadyLimitMs = 20;
    public const double RecoveryToleranceMs = 20;
    public const double RecoveryLimitMs = 1000;
    public const double LateDroppedPerMinuteLimit = 1;

    /// <summary>Nearest-rank percentile (<paramref name="percent"/> in 0..100); 0 for no values.</summary>
    public static double Percentile(IReadOnlyCollection<double> values, double percent)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var rank = (int)Math.Ceiling(percent / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }

    /// <summary>Percentiles and maximum of the absolute values of <paramref name="errorsMs"/>.</summary>
    public static ErrorSummary Summarize(IReadOnlyCollection<double> errorsMs)
    {
        var abs = errorsMs.Select(Math.Abs).ToArray();
        return new ErrorSummary(
            abs.Length,
            Percentile(abs, 50),
            Percentile(abs, 95),
            Percentile(abs, 99),
            abs.Length == 0 ? 0 : abs.Max());
    }

    /// <summary>
    /// Time of the first sample from which every later sample is within <paramref name="toleranceMs"/>;
    /// <c>null</c> when there are no samples or the last one is still out.
    /// </summary>
    public static double? RecoveryTimeMs(IReadOnlyList<(double AtMs, double ErrorMs)> samples, double toleranceMs)
    {
        double? start = null;
        foreach (var (atMs, errorMs) in samples)
        {
            if (Math.Abs(errorMs) <= toleranceMs)
            {
                start ??= atMs;
            }
            else
            {
                start = null;
            }
        }

        return start;
    }

    public static SpikeVerdict SteadyVerdict(double maxAbsErrorMs) =>
        maxAbsErrorMs <= SteadyLimitMs ? SpikeVerdict.Pass : SpikeVerdict.Fail;

    /// <summary>One entry per pause/resume/seek; <c>null</c> = never came back within tolerance.</summary>
    public static SpikeVerdict RecoveryVerdict(IReadOnlyCollection<double?> recoveryMs)
    {
        if (recoveryMs.Count == 0)
        {
            return SpikeVerdict.NotMeasured;
        }

        return recoveryMs.All(r => r is <= RecoveryLimitMs) ? SpikeVerdict.Pass : SpikeVerdict.Fail;
    }

    public static double PerMinute(long frames, TimeSpan over) =>
        over <= TimeSpan.Zero ? 0 : frames / over.TotalMinutes;

    public static SpikeVerdict LateDroppedVerdict(long lateAndDropped, TimeSpan over)
    {
        if (over <= TimeSpan.Zero)
        {
            return SpikeVerdict.NotMeasured;
        }

        return PerMinute(lateAndDropped, over) <= LateDroppedPerMinuteLimit ? SpikeVerdict.Pass : SpikeVerdict.Fail;
    }
}
