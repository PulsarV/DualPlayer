using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>Scripted renderer: the presented position follows a <see cref="TimeProvider"/> times the speed.</summary>
internal sealed class FakeVideoRenderer(TimeProvider time) : IVideoRenderer
{
    private readonly Lock _gate = new();
    private readonly List<(string Text, NoteStyle Style)> _notes = [];

    private bool _hasFrame;
    private bool _playing;
    private double _speed = 1.0;
    private TimeSpan _anchorPosition;
    private long _anchorTimestamp;
    private FrameCounters _counters = FrameCounters.None;

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

    public PresentedPicture? Presented
    {
        get
        {
            lock (_gate)
            {
                if (!_hasFrame)
                {
                    return null;
                }

                var now = time.GetTimestamp();
                return new PresentedPicture(PositionAt(now), now);
            }
        }
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

    public Result Load(string file, nint nativeWindowHandle, bool startPaused)
    {
        lock (_gate)
        {
            if (LoadError is { } error)
            {
                return Result.Failure(error);
            }

            _hasFrame = false;
            _playing = !startPaused;
            _anchorPosition = TimeSpan.Zero;
            _anchorTimestamp = time.GetTimestamp();
            _counters = FrameCounters.None;
            _notes.Clear();
            return Result.Success();
        }
    }

    public void PlayAt(TimeSpan position, long startTimestamp)
    {
        lock (_gate)
        {
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

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            _hasFrame = true;
            _anchorPosition = position;
            _anchorTimestamp = time.GetTimestamp();
        }
    }

    public void SetSpeed(double speed)
    {
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

    public void Dispose()
    {
    }

    private TimeSpan PositionAt(long now)
    {
        if (!_playing || now <= _anchorTimestamp)
        {
            return _anchorPosition;
        }

        return _anchorPosition + (time.GetElapsedTime(_anchorTimestamp, now) * _speed);
    }
}
