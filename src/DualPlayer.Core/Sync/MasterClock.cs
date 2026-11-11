namespace DualPlayer.Core.Sync;

/// <summary>
/// The single playback timeline, owned by no device. Advances with the injected <see cref="TimeProvider"/>
/// while running, holds its position when stopped and jumps on seek.
/// </summary>
public sealed class MasterClock(TimeProvider time)
{
    private readonly Lock _gate = new();
    private TimeSpan _basePosition = TimeSpan.Zero;
    private long _baseTimestamp = time.GetTimestamp();
    private bool _running;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _baseTimestamp = time.GetTimestamp();
            _running = true;
        }
    }

    public void StopAt(long timestamp)
    {
        lock (_gate)
        {
            _basePosition = PositionLocked(timestamp);
            _baseTimestamp = timestamp;
            _running = false;
        }
    }

    public void JumpTo(TimeSpan position)
    {
        lock (_gate)
        {
            _basePosition = position;
            _baseTimestamp = time.GetTimestamp();
        }
    }

    public TimeSpan PositionAt(long timestamp)
    {
        lock (_gate)
        {
            return PositionLocked(timestamp);
        }
    }

    private TimeSpan PositionLocked(long timestamp) =>
        _running ? _basePosition + time.GetElapsedTime(_baseTimestamp, timestamp) : _basePosition;
}
