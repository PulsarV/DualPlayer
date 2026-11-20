namespace DualPlayer.Media.Video;

/// <summary>
/// How a start (<see cref="MpvVideoRenderer.PlayAt"/>) is played so the picture shows the right content at the right
/// moment although mpv's seek takes time: the renderer pauses, seeks to where the content will be once the seek is done,
/// and unpauses when the start reaches the frame the seek landed on (<see cref="MpvPictureTimeline.UnpauseDue"/>).
/// </summary>
internal static class MpvStartPlan
{
    private static readonly TimeSpan MinBudget = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxBudget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BudgetMargin = TimeSpan.FromMilliseconds(20);
    private const double BudgetFactor = 1.5;
    private const double SeekTimeWeight = 0.3;

    /// <summary>The seek time assumed before any was measured.</summary>
    public static TimeSpan InitialSeekTime { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The position to seek to: <paramref name="position"/> when the seek can be done before <paramref name="start"/>,
    /// otherwise the position the start reaches when the seek is expected to be done.
    /// </summary>
    public static TimeSpan SeekTarget(TimeSpan position, long start, long now, TimeSpan seekBudget, double speed, long frequency)
    {
        var ready = now + (long)Math.Round(seekBudget.TotalSeconds * frequency);
        if (ready <= start)
        {
            return position;
        }

        var ahead = TimeSpan.FromSeconds((double)(ready - start) / frequency);
        return position + TimeSpan.FromTicks((long)Math.Round(ahead.Ticks * speed));
    }

    /// <summary>How long a seek is allowed to take: the measured seek time with headroom, within 50 ms..1 s.</summary>
    public static TimeSpan SeekBudget(TimeSpan seekTime)
    {
        var budget = TimeSpan.FromTicks((long)Math.Round(seekTime.Ticks * BudgetFactor)) + BudgetMargin;
        return budget < MinBudget ? MinBudget : budget > MaxBudget ? MaxBudget : budget;
    }

    /// <summary>The measured seek time after one more measurement (exponential smoothing).</summary>
    public static TimeSpan NextSeekTime(TimeSpan current, TimeSpan measured) =>
        TimeSpan.FromTicks((long)Math.Round((current.Ticks * (1 - SeekTimeWeight)) + (measured.Ticks * SeekTimeWeight)));
}
