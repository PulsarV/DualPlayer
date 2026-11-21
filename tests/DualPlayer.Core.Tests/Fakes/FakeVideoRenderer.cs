using DualPlayer.Core.Playback;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>Scripted renderer: the presented position follows a <see cref="TimeProvider"/> times the speed.</summary>
internal sealed class FakeVideoRenderer(TimeProvider time) : IVideoRenderer
{
    private readonly Lock _gate = new();
    private readonly List<(string Text, NoteStyle Style)> _notes = [];

    private bool _frozen;
    private PresentedPicture? _frozenSnapshot;
    private bool _hasFrame;
    private bool _playing;
    private double _speed = 1.0;
    private TimeSpan _anchorPosition;
    private long _anchorTimestamp;
    private FrameCounters _counters = FrameCounters.None;

    /// <summary>
    /// When set, a frame held for a scheduled start (<see cref="PlayAt"/> with a future start) is stamped with that start,
    /// as the libmpv renderer does (a prediction ahead of now); otherwise it is stamped now.
    /// </summary>
    public bool StampsHeldFrameWithItsStart { get; set; }

    /// <summary>The film's length: the picture stops on its last frame there and a seek past it shows the last frame (mpv keep-open).</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>How many times a file was loaded.</summary>
    public int LoadCount { get; private set; }

    /// <summary>The smallest content position on screen since <see cref="ResetLowestShown"/>, sampled at every call into the fake.</summary>
    public TimeSpan? LowestShown { get; private set; }

    public void ResetLowestShown() => LowestShown = null;

    /// <summary>The picture's own clock error: it runs at speed × (1 + RateError), as a video clock that drifts from the audio clock.</summary>
    public double RateError { get; set; }

    /// <summary>When set, the picture shows whole frames of this length: a snapshot names the frame on screen and when it was presented.</summary>
    public TimeSpan? FrameDuration { get; set; }

    /// <summary>How old every snapshot is: <see cref="Presented"/> shows the picture as it was this long ago, stamped then.</summary>
    public TimeSpan SnapshotAge { get; set; }

    /// <summary>The content position on screen right now (what a camera would see), independent of <see cref="SnapshotAge"/>.</summary>
    public TimeSpan ShownNow
    {
        get
        {
            lock (_gate)
            {
                return PositionAt(time.GetTimestamp());
            }
        }
    }

    /// <summary>Moves the picture by <paramref name="by"/> at once (negative = it falls behind), as a stall or a skipped stretch does.</summary>
    public void Jump(TimeSpan by)
    {
        lock (_gate)
        {
            var now = time.GetTimestamp();
            _anchorPosition = PositionAt(now) + by;
            _anchorTimestamp = Math.Max(now, _anchorTimestamp);
        }
    }

    /// <summary>When set, <see cref="Load"/> fails with this error.</summary>
    public Error? LoadError { get; set; }

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _playing;
            }
        }
    }

    public double Speed
    {
        get
        {
            lock (_gate)
            {
                return _speed;
            }
        }
    }

    public IReadOnlyList<(string Text, NoteStyle Style)> Notes
    {
        get
        {
            lock (_gate)
            {
                return [.. _notes];
            }
        }
    }

    /// <summary>When set, the screen stops changing: <see cref="Presented"/> keeps returning the snapshot taken when this was set (a stalled decoder presents no new frame).</summary>
    public bool Frozen
    {
        get => _frozen;
        set
        {
            lock (_gate)
            {
                _frozenSnapshot = value ? SnapshotNow() : null;
                _frozen = value;
            }
        }
    }

    public PresentedPicture? Presented
    {
        get
        {
            lock (_gate)
            {
                return _frozen ? _frozenSnapshot : SnapshotNow();
            }
        }
    }

    private PresentedPicture? SnapshotNow()
    {
        if (!_hasFrame)
        {
            return null;
        }

        var now = time.GetTimestamp();
        if (StampsHeldFrameWithItsStart && _playing && now < _anchorTimestamp)
        {
            return new PresentedPicture(_anchorPosition, _anchorTimestamp);
        }

        var at = now - (long)Math.Round(SnapshotAge.TotalSeconds * time.TimestampFrequency);
        var position = PositionAt(at);
        if (FrameDuration is { } frame && _playing && at > _anchorTimestamp)
        {
            // The frame on screen began when the picture reached its position; it has been shown since then.
            var start = TimeSpan.FromTicks(position.Ticks / frame.Ticks * frame.Ticks);
            var shownFor = (position - start) / (_speed * (1 + RateError));
            return new PresentedPicture(start, at - (long)Math.Round(shownFor.TotalSeconds * time.TimestampFrequency));
        }

        return new PresentedPicture(position, at);
    }

    public FrameCounters Counters
    {
        get
        {
            lock (_gate)
            {
                return _counters;
            }
        }
    }

    public event Action<Error>? DecodeFailed;

    public event Action? HardwareDecodingInactive;

    public event Action? EndOfFile;

    /// <summary>When set, runs right after a successful <see cref="Load"/>, as an asynchronous renderer event can.</summary>
    public Action? AfterLoad { get; set; }

    /// <summary>When set, receives a line for each Load, Seek and ShowNote call, so a test can check the order of calls across fakes.</summary>
    public Action<string>? Observer { get; set; }

    /// <summary>How long a load takes on the fake clock; a load given a shorter timeout gives up after that timeout and fails.</summary>
    public TimeSpan LoadTakes { get; set; }

    private readonly List<TimeSpan?> _loadTimeouts = [];

    /// <summary>The wait bound every <see cref="Load"/> was given, in order (null = the renderer's own default).</summary>
    public IReadOnlyList<TimeSpan?> LoadTimeouts
    {
        get
        {
            lock (_gate)
            {
                return [.. _loadTimeouts];
            }
        }
    }

    public Result Load(string file, nint nativeWindowHandle, bool startPaused, TimeSpan? timeout = null)
    {
        Observer?.Invoke(startPaused ? "renderer.Load(paused)" : "renderer.Load(playing)");
        lock (_gate)
        {
            _loadTimeouts.Add(timeout);
            if (LoadTakes > TimeSpan.Zero && time is FakeTimeProvider fake)
            {
                var gaveUp = timeout is { } bound && bound < LoadTakes;
                fake.Advance(gaveUp ? timeout!.Value : LoadTakes);
                if (gaveUp)
                {
                    return Result.Failure("mpv.load_failed", "mpv did not open the file in time.");
                }
            }

            if (LoadError is { } error)
            {
                return Result.Failure(error);
            }

            LoadCount++;
            _hasFrame = true;
            _playing = !startPaused;
            _anchorPosition = TimeSpan.Zero;
            _anchorTimestamp = time.GetTimestamp();
            _counters = FrameCounters.None;
            _notes.Clear();
        }

        AfterLoad?.Invoke();
        return Result.Success();
    }

    /// <summary>The picture starts this much later than a <see cref="PlayAt"/> asks (a slow first present).</summary>
    public TimeSpan PresentsLateBy { get; set; }

    public void PlayAt(TimeSpan position, long startTimestamp)
    {
        startTimestamp += (long)Math.Round(PresentsLateBy.TotalSeconds * time.TimestampFrequency);
        lock (_gate)
        {
            Sample(position);
            _hasFrame = true;
            _playing = true;
            _anchorPosition = position;
            _anchorTimestamp = startTimestamp;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            var now = time.GetTimestamp();
            _anchorPosition = PositionAt(now);
            _anchorTimestamp = now;
            _playing = false;
        }
    }

    /// <summary>Every seek lands this far from where it was asked (a picture that cannot be corrected by seeking).</summary>
    public TimeSpan SeekLandsOff { get; set; }

    public void Seek(TimeSpan position)
    {
        Observer?.Invoke("renderer.Seek");
        position += SeekLandsOff;
        lock (_gate)
        {
            _hasFrame = true;
            _anchorPosition = Duration is { } duration && position > duration ? duration : position;
            Sample(_anchorPosition);
            var now = time.GetTimestamp();
            if (!_playing || _anchorTimestamp <= now)
            {
                _anchorTimestamp = now;
            }
        }
    }

    /// <summary>When set, runs at the start of every <see cref="SetSpeed"/> outside the fake's lock (a test can block a command there).</summary>
    public Action? BeforeSetSpeed { get; set; }

    /// <summary>When set, maps the speed asked for to the speed the player really runs at (and <see cref="Speed"/> reports).</summary>
    public Func<double, double>? AppliesSpeed { get; set; }

    public void SetSpeed(double speed)
    {
        BeforeSetSpeed?.Invoke();
        speed = AppliesSpeed?.Invoke(speed) ?? speed;
        lock (_gate)
        {
            var now = time.GetTimestamp();
            if (_playing && now >= _anchorTimestamp)
            {
                _anchorPosition = PositionAt(now);
                _anchorTimestamp = now;
            }

            _speed = speed;
        }
    }

    public void ShowNote(string text, NoteStyle style)
    {
        Observer?.Invoke("renderer.ShowNote");
        lock (_gate)
        {
            _notes.Add((text, style));
        }
    }

    /// <summary>Raises <see cref="DecodeFailed"/> once.</summary>
    public void InjectDecodeFailure(Error error) => DecodeFailed?.Invoke(error);

    /// <summary>Raises <see cref="HardwareDecodingInactive"/> once.</summary>
    public void InjectHardwareDecodingInactive() => HardwareDecodingInactive?.Invoke();

    /// <summary>Raises <see cref="EndOfFile"/> once.</summary>
    public void InjectEndOfFile() => EndOfFile?.Invoke();

    /// <summary>Adds to the frame counters.</summary>
    public void InjectFrames(long late = 0, long dropped = 0, long skippedDamaged = 0)
    {
        lock (_gate)
        {
            _counters = new FrameCounters(
                _counters.Late + late,
                _counters.Dropped + dropped,
                _counters.SkippedDamaged + skippedDamaged);
        }
    }

    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;

    private TimeSpan PositionAt(long now)
    {
        if (!_playing || now <= _anchorTimestamp)
        {
            return _anchorPosition;
        }

        var position = _anchorPosition + (time.GetElapsedTime(_anchorTimestamp, now) * (_speed * (1 + RateError)));
        return Duration is { } duration && position > duration ? duration : position;
    }

    private void Sample(TimeSpan position)
    {
        if (LowestShown is null || position < LowestShown)
        {
            LowestShown = position;
        }
    }
}
