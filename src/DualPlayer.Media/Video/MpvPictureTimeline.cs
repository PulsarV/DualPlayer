using DualPlayer.Core.Playback;

namespace DualPlayer.Media.Video;

/// <summary>
/// Turns what mpv reports into the picture on screen: which frame, and the monotonic timestamp it is shown at.
/// Single-threaded: the renderer's event thread owns it. Timestamps are on the <see cref="TimeProvider.GetTimestamp"/>
/// base whose frequency is given to the constructor.
/// </summary>
/// <remarks>
/// <para>
/// mpv semantics this relies on (libmpv 0.40–0.42 and its 2026 master; <c>player/video.c</c>, <c>video/out/vo.c</c>):
/// </para>
/// <list type="bullet">
/// <item><c>time-pos</c> is the pts of the frame the player has just handed to the video output (<c>vo_queue_frame</c>),
/// not the frame on screen. The output shows it later, at the frame's display time, after rendering it ahead
/// (<c>--video-timing-offset</c>, 50 ms by default, plus the pipeline lead mpv adds when presents come late). Measured on
/// the target PC (d3d11, 60 Hz): reported 60–66 ms before display in steady play at 24, 25 and 60 fps; at a start the
/// first frame is reported at once, one frame interval before it is due.</item>
/// <item>Without audio (<c>video-sync=audio</c> falls back to the system clock), the display times run on mpv's own
/// schedule: each frame is due its pts distance after the previous one, divided by the speed. The schedule starts at a
/// resume from the frame on screen (the time spent paused is not counted) and at a playback restart (seek, load) from
/// the restart frame, which mpv shows before it reports the restart.</item>
/// <item>While seeking, <c>time-pos</c> reports the seek target, not a frame; the restart frame is reported with the
/// restart.</item>
/// <item><c>vo-presentation-delay</c> (where the build and output provide presentation feedback) is the display time
/// the output measured minus the schedule's display time for a recent frame; positive = shown late.</item>
/// </list>
/// <para>
/// A frame is published once a later report finds its display time passed, so <see cref="PresentedPicture.Timestamp"/>
/// is the moment it went up (at most a frame plus mpv's lead after it happened). A frame that reaches the output after
/// its display time (a stall) is shown no earlier than it reached it. A resume without a seek assumes the next frame is
/// a whole frame interval away; mpv keeps the rest of the interval it paused in, so such a reading can be up to a frame
/// early until the next restart (the picture loop always seeks before it resumes).
/// </para>
/// </remarks>
internal sealed class MpvPictureTimeline
{
    private const int PendingLimit = 16;
    private const int DelaySamples = 5;
    private const int DelayLogLimit = 100_000;

    // Feedback reports a flip up to the swapchain depth (at most 2 here) plus one flips after it, so the first samples
    // after a resume or restart can still be about the paused redraws, measured against the old schedule.
    private const int StaleDelaySamples = 3;

    private readonly long _frequency;
    private readonly List<PresentedPicture> _pending = [];
    private readonly Queue<TimeSpan> _delays = new();
    private readonly List<double> _usedDelaysMs = [];

    private bool _running;
    private bool _seeking;
    private bool _anchored;
    private TimeSpan _anchorPosition;
    private long _anchorAt;
    private double _speed = 1.0;
    private TimeSpan? _lastReported;
    private TimeSpan? _lastSeen;
    private int _delaySamples;
    private int _usedDelays;
    private int _delaysToSkip;
    private bool _planned;
    private bool _planSeekStarted;
    private TimeSpan _planPosition;
    private long _planStart;

    public MpvPictureTimeline(long timestampFrequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        _frequency = timestampFrequency;
    }

    /// <summary>
    /// When the planned start reaches the frame the last restart put up: the moment to unpause; null without a plan.
    /// </summary>
    public long? UnpauseDue { get; private set; }

    /// <summary>
    /// The presentation-feedback samples of this file: how many mpv gave, how many corrected the schedule, and the median
    /// of those (null when none). Zero samples means mpv gave no feedback (property missing or no feedback from the output).
    /// </summary>
    public PresentationDelayStats PresentationDelays
    {
        get
        {
            double? median = null;
            if (_usedDelaysMs.Count > 0)
            {
                var sorted = _usedDelaysMs.Order().ToArray();
                median = sorted[sorted.Length / 2];
            }

            return new PresentationDelayStats(_delaySamples, _usedDelays, median);
        }
    }

    /// <summary>A new file starts, playing or paused. The speed is mpv's and outlives the file.</summary>
    public void Reset(bool paused)
    {
        DropPlan();
        _running = !paused;
        _seeking = false;
        _anchored = false;
        _lastReported = null;
        _lastSeen = null;
        _pending.Clear();
        _delays.Clear();
        _usedDelaysMs.Clear();
        _delaySamples = 0;
        _usedDelays = 0;
    }

    /// <summary>mpv began a seek: reports until the restart are the target, not frames.</summary>
    public void SeekStarted()
    {
        _seeking = true;
        _planSeekStarted = _planned;
        _anchored = false;
        _lastReported = null;
        _pending.Clear();
    }

    /// <summary>
    /// A paused start is planned: content <paramref name="position"/> is due at <paramref name="start"/> and on from
    /// there at the speed. The restart of the seek that follows holds its frame until the start reaches it.
    /// </summary>
    public void PlanStart(TimeSpan position, long start)
    {
        _planned = true;
        _planSeekStarted = false;
        _planPosition = position;
        _planStart = start;
        UnpauseDue = null;
    }

    /// <summary>
    /// Playback restarted with <paramref name="restartFrame"/> on screen (null when mpv could not say: the last position
    /// reported, the seek target while seeking, stands in); the schedule starts from it. Under a planned start the frame
    /// is held: it is published at the moment it is due (<see cref="UnpauseDue"/>), or now when that has passed. That
    /// timestamp is a prediction until the unpause is applied.
    /// </summary>
    public PresentedPicture Restarted(TimeSpan? restartFrame, long at)
    {
        var shown = restartFrame ?? _lastSeen ?? TimeSpan.Zero;
        _seeking = false;
        _lastReported = shown;
        _lastSeen = shown;
        Anchor(shown, at);
        if (_planned && _planSeekStarted && !_running)
        {
            var due = PlanTime(shown);
            UnpauseDue = due;
            return new PresentedPicture(shown, Math.Max(due, at));
        }

        return new PresentedPicture(shown, at);
    }

    /// <summary>mpv applied an unpause at <paramref name="at"/>; null when it was playing or the shown frame is not known yet.</summary>
    public PresentedPicture? Resumed(long at)
    {
        if (_running)
        {
            return null;
        }

        _running = true;
        DropPlan();
        if (_seeking || _lastReported is not { } shown)
        {
            return null;
        }

        Anchor(shown, at);
        return new PresentedPicture(shown, at);
    }

    /// <summary>mpv applied a pause: the last frame handed over still goes up at its display time and stays.</summary>
    public PresentedPicture? Paused(long at)
    {
        _ = at;
        DropPlan();
        if (!_running)
        {
            return null;
        }

        _running = false;
        var last = _pending.Count > 0 ? _pending[^1] : null;
        _pending.Clear();
        return last;
    }

    /// <summary>mpv applied <paramref name="speed"/> at <paramref name="at"/>: the schedule bends there.</summary>
    public void SpeedApplied(double speed, long at)
    {
        if (_running && _anchored)
        {
            _anchorPosition = PositionAt(at);
            _anchorAt = at;
        }

        _speed = speed;
    }

    /// <summary>
    /// A presentation-feedback sample, or null when mpv has none (display-sync mode, no feedback). Only samples about
    /// the current run count: none while paused, and not the first ones after a resume or restart.
    /// </summary>
    public void PresentationDelay(TimeSpan? delay)
    {
        if (delay is not { } value)
        {
            _delays.Clear();
            return;
        }

        _delaySamples++;
        if (!_running || !_anchored)
        {
            return;
        }

        if (_delaysToSkip > 0)
        {
            _delaysToSkip--;
            return;
        }

        _usedDelays++;
        if (_usedDelaysMs.Count < DelayLogLimit)
        {
            _usedDelaysMs.Add(value.TotalMilliseconds);
        }

        _delays.Enqueue(value);
        if (_delays.Count > DelaySamples)
        {
            _ = _delays.Dequeue();
        }
    }

    /// <summary>mpv reported <paramref name="position"/> (<c>time-pos</c>) at <paramref name="at"/>; returns the picture to publish, if any.</summary>
    public PresentedPicture? Queued(TimeSpan position, long at)
    {
        _lastSeen = position;
        if (_seeking)
        {
            // The seek target, not a frame: a prediction that stands in until the restart frame (when the start was
            // planned, at the moment the start reaches it).
            return new PresentedPicture(position, _planned ? PlanTime(position) : at);
        }

        var repeat = _lastReported == position;
        _lastReported = position;
        if (repeat && _running && _anchored && position == _anchorPosition)
        {
            // A late (coalesced) report of the frame the schedule was anchored on: already published at the anchor.
            return null;
        }
        if (!_running || !_anchored)
        {
            // Paused (or not yet started): nothing is scheduled ahead, the reported frame is the one up; under a planned
            // start it is held until the start reaches it.
            return new PresentedPicture(position, UnpauseDue is null ? at : Math.Max(PlanTime(position), at));
        }

        var due = _anchorAt + ToTimestamp((position - _anchorPosition) / _speed) + ToTimestamp(MedianDelay());
        _pending.Add(new PresentedPicture(position, Math.Max(due, at)));
        if (_pending.Count > PendingLimit)
        {
            _pending.RemoveAt(0);
        }

        PresentedPicture? shown = null;
        while (_pending.Count > 0 && _pending[0].Timestamp <= at)
        {
            shown = _pending[0];
            _pending.RemoveAt(0);
        }

        return shown;
    }

    private void Anchor(TimeSpan position, long at)
    {
        _anchorPosition = position;
        _anchorAt = at;
        _anchored = true;
        _pending.Clear();
        _delays.Clear();
        _delaysToSkip = StaleDelaySamples;
    }

    private void DropPlan()
    {
        _planned = false;
        _planSeekStarted = false;
        UnpauseDue = null;
    }

    private long PlanTime(TimeSpan position) => _planStart + ToTimestamp((position - _planPosition) / _speed);

    private TimeSpan PositionAt(long at) =>
        _anchorPosition + TimeSpan.FromSeconds((double)(at - _anchorAt) / _frequency * _speed);

    private TimeSpan MedianDelay()
    {
        if (_delays.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var sorted = _delays.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private long ToTimestamp(TimeSpan span) => (long)Math.Round(span.TotalSeconds * _frequency);
}

/// <summary>
/// Presentation-feedback samples of one file: <c>Samples</c> mpv gave, <c>Used</c> to correct the schedule, and the
/// median of the used ones in milliseconds (null when none was used).
/// </summary>
internal sealed record PresentationDelayStats(int Samples, int Used, double? MedianMs);
