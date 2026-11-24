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
    public static double? RecoveryTimeMs(IReadOnlyList<(double AtMs, double ErrorMs)> samples, double toleranceMs) =>
        HeldAwareRecoveryTimeMs(samples.Select(s => (s.AtMs, s.ErrorMs, Held: false)).ToList(), toleranceMs);

    /// <summary>
    /// As above, but a sample taken while the picture was held paused for a start (<c>Held</c>) never counts as back:
    /// its frame is stamped with the planned start, so its error is ~0 by construction while the screen is frozen.
    /// </summary>
    public static double? HeldAwareRecoveryTimeMs(IReadOnlyList<(double AtMs, double ErrorMs, bool Held)> samples, double toleranceMs)
    {
        double? start = null;
        foreach (var (atMs, errorMs, held) in samples)
        {
            if (!held && Math.Abs(errorMs) <= toleranceMs)
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

    /// <summary>The report line on mpv's presentation feedback; zero samples is stated, never left out.</summary>
    public static string PresentationDelayText(int samples, int used, double? medianMs) =>
        medianMs is { } median && samples > 0
            ? FormattableString.Invariant($"presentation-delay samples: {samples} (used {used}), median {median:0.0} ms")
            : FormattableString.Invariant($"presentation-delay samples: {samples} (used {used}), median n/a - mpv gave no presentation feedback (property missing or not provided by the output); the display schedule ran uncorrected");

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

    /// <summary>The seeks of one start that overran their budget (each one re-planned the start); empty when none did.</summary>
    public static string OverrunsText(IEnumerable<(double BudgetMs, double TookMs)> overruns)
    {
        var parts = overruns.Select(o => FormattableString.Invariant($"{o.TookMs:0} ms of {o.BudgetMs:0} ms budget")).ToList();
        return parts.Count == 0 ? string.Empty : $" (overran: {string.Join("; ", parts)})";
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
