namespace DualPlayer.Media.Video;

/// <summary>
/// How a start (<see cref="MpvVideoRenderer.PlayAt"/>) is played so the picture shows the right content at the right
/// moment although mpv's seek takes time: the renderer pauses, seeks to where the content will be once the seek is done,
/// and unpauses when the start reaches the frame the seek landed on (<see cref="MpvPictureTimeline.UnpauseDue"/>). A seek
/// that overruns its budget lands on a frame already past due; the start is then re-planned (<see cref="AtRestart"/>), so
/// the picture always resumes on its due frame, never late by the overrun.
/// </summary>
/// <remarks>
/// Budget policy, from the D1 spike on the target PC (1080p HEVC 23.976, d3d11va): seek times swing 4..240 ms with the
/// distance to the previous keyframe, and a cold first seek took ~525 ms. A smoothed mean underestimated the slow seeks
/// (2 of 40 overran, unpausing 35 and 86 ms late), so the budget follows the slowest of the recent seeks instead.
/// </remarks>
internal static class MpvStartPlan
{
    /// <summary>How many recent seek times the tail is taken over.</summary>
    public const int SeekHistory = 8;

    /// <summary>How many times a start is re-planned after an overrun before it is unpaused late.</summary>
    public const int MaxReplans = 3;

    private static readonly TimeSpan MinBudget = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxBudget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BudgetMargin = TimeSpan.FromMilliseconds(20);
    private const double BudgetFactor = 1.25;

    // A restart this little past its frame's due moment is unpaused at once: Core's speed nudge absorbs it, while a
    // re-seek would hold the picture another seek time.
    private static readonly TimeSpan LateTolerance = TimeSpan.FromMilliseconds(5);

    /// <summary>The seek time assumed before any was measured: above a cold HEVC seek on the target PC (~525 ms).</summary>
    public static TimeSpan InitialSeekTime { get; } = TimeSpan.FromMilliseconds(600);

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

    /// <summary>How long a seek is allowed to take: the seek time tail with headroom, within 50 ms..1 s.</summary>
    public static TimeSpan SeekBudget(TimeSpan seekTimeTail)
    {
        var budget = TimeSpan.FromTicks((long)Math.Round(seekTimeTail.Ticks * BudgetFactor)) + BudgetMargin;
        return budget < MinBudget ? MinBudget : budget > MaxBudget ? MaxBudget : budget;
    }

    /// <summary>
    /// The slowest of the last <see cref="SeekHistory"/> measured seek times (oldest first), or
    /// <see cref="InitialSeekTime"/> before any was measured.
    /// </summary>
    public static TimeSpan SeekTimeTail(IReadOnlyList<TimeSpan> measured)
    {
        var tail = TimeSpan.Zero;
        for (var i = Math.Max(0, measured.Count - SeekHistory); i < measured.Count; i++)
        {
            tail = measured[i] > tail ? measured[i] : tail;
        }

        return measured.Count == 0 ? InitialSeekTime : tail;
    }

    /// <summary>
    /// What to do when a start's seek restarted at <paramref name="now"/> on a frame due at <paramref name="due"/>, after
    /// <paramref name="replans"/> re-plans of this start.
    /// </summary>
    public static StartAction AtRestart(long due, long now, int replans, long frequency)
    {
        var late = TimeSpan.FromSeconds((double)(now - due) / frequency);
        return late <= LateTolerance ? StartAction.Unpause : replans < MaxReplans ? StartAction.Replan : StartAction.UnpauseLate;
    }
}

/// <summary>What a planned start does when its seek restarted (<see cref="MpvStartPlan.AtRestart"/>).</summary>
internal enum StartAction
{
    /// <summary>The frame is not (noticeably) past due: unpause when it is due.</summary>
    Unpause,

    /// <summary>The seek overran its budget: keep holding and seek again ahead with a fresh budget.</summary>
    Replan,

    /// <summary>Re-plans are used up: unpause now, late, and let the picture loop correct.</summary>
    UnpauseLate,
}
