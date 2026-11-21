using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Sync;

/// <summary>One look at the picture: the content position it should show (due) and the one it showed (presented).</summary>
public readonly record struct PictureTiming(TimeSpan Due, TimeSpan Presented)
{
    /// <summary>Gets the master-clock position at the snapshot's time (due plus the picture delay then).</summary>
    public TimeSpan Clock { get; init; }

    /// <summary>Gets when the presented frame was on screen (the snapshot's timestamp).</summary>
    public long Timestamp { get; init; }

    /// <summary>Gets presented minus due; positive = picture ahead of due.</summary>
    public TimeSpan Error => Presented - Due;

    public double ErrorMs => Error.TotalMilliseconds;
}

/// <summary>
/// Keeps the presented picture on <c>master clock − picture delay</c> (ADR-0002): aligns it on start, resume and
/// seek, steers the renderer speed within ±0.2 % for small errors, seeks for large ones, and moves the picture
/// delay at ≤ 0.2 % speed change (sad.md flows 1–2).
/// </summary>
public sealed class PictureSync : IDisposable
{
    /// <summary>Largest speed deviation from 1.0 the loop may command.</summary>
    public const double MaxSpeedDeviation = 0.002;

    /// <summary>A picture further than this from due is moved by a seek instead of a speed change.</summary>
    public static readonly TimeSpan SeekThreshold = TimeSpan.FromMilliseconds(100);

    // Errors smaller than this leave the speed at exactly 1.0.
    private static readonly TimeSpan Deadband = TimeSpan.FromMilliseconds(0.25);

    // Speed steering removes error/SteerTimeConstant of speed deviation, saturating at MaxSpeedDeviation.
    private const double SteerTimeConstantSeconds = 2.0;

    private readonly Lock _gate = new();
    private readonly MasterClock _clock;
    private readonly IVideoRenderer _renderer;
    private readonly TimeProvider _time;

    private TimeSpan _pictureDelay;
    private bool _moving;
    private TimeSpan _moveFrom;
    private TimeSpan _moveTarget;
    private long _moveStart;
    private TimeSpan _moveDuration;
    private double _moveRate = MaxSpeedDeviation;
    private double _speed = double.NaN;
    private ITimer? _moveTimer;
    private long _moveVersion;

    // While the clock is before this timestamp the renderer holds the first frame: there is no error to correct.
    private long _holdUntil = long.MinValue;

    // When the picture was last re-aligned or moved by a seek: a snapshot stamped before it shows the picture from before.
    private long _commandedAt = long.MinValue;

    private readonly Action<Action>? _post;

    /// <param name="post">
    /// Where the move's own timer runs its end (the playback session's thread, which logs a fault); null = on the
    /// timer's thread.
    /// </param>
    public PictureSync(MasterClock clock, IVideoRenderer renderer, TimeProvider time, TimeSpan pictureDelay, Action<Action>? post = null)
    {
        _post = post;
        _clock = clock;
        _renderer = renderer;
        _time = time;
        _pictureDelay = pictureDelay;
        _moveTarget = pictureDelay;
    }

    /// <summary>Gets the picture delay in effect now (ramping while a move is under way).</summary>
    public TimeSpan PictureDelay
    {
        get
        {
            lock (_gate)
            {
                return DelayAt(_time.GetTimestamp());
            }
        }
    }

    /// <summary>Gets the picture delay the current move ends at (equals <see cref="PictureDelay"/> when idle).</summary>
    public TimeSpan TargetPictureDelay
    {
        get
        {
            lock (_gate)
            {
                return _moving ? _moveTarget : _pictureDelay;
            }
        }
    }

    public bool IsMoving
    {
        get
        {
            lock (_gate)
            {
                return _moving;
            }
        }
    }

    /// <summary>Start, resume or seek: <paramref name="clockTimestamp"/> is when the master clock reads <paramref name="clockPosition"/>.</summary>
    public void AlignAt(TimeSpan clockPosition, long clockTimestamp)
    {
        lock (_gate)
        {
            EndMove(settleAtTarget: true);
            ApplySpeed(1.0, force: true);

            var delay = _pictureDelay;
            _holdUntil = long.MinValue;
            _commandedAt = _time.GetTimestamp();
            if (delay > TimeSpan.Zero && clockPosition < delay)
            {
                // Before the start of the film: hold the first frame until the clock catches up with the delay.
                _holdUntil = clockTimestamp + ToTimestamp(delay - clockPosition);
                _renderer.PlayAt(TimeSpan.Zero, _holdUntil);
            }
            else if (delay > TimeSpan.Zero)
            {
                // A frame held for a start still ahead (a resume of the frozen picture) is not an error to correct before then.
                _holdUntil = clockTimestamp;
                _renderer.PlayAt(clockPosition - delay, clockTimestamp);
            }
            else
            {
                // Zero or negative delay: the picture starts |delay| before the clock reads the position;
                // until then it is pre-rolling and there is no error to correct.
                _holdUntil = clockTimestamp + ToTimestamp(delay);
                _renderer.PlayAt(clockPosition, _holdUntil);
            }
        }
    }

    /// <summary>
    /// One loop step; <c>null</c> when there is nothing to measure: the renderer has no frame yet, the clock is stopped, the
    /// picture is still held for the start of the film, or the snapshot was stamped before the last align or correcting
    /// seek (it shows the picture from before). A renderer that holds a frame paused for a scheduled start and stamps it
    /// with the moment the start reaches it (<see cref="PresentedPicture"/>) yields a timing with zero error by
    /// construction: that is a prediction, not a measurement of the screen.
    /// </summary>
    public PictureTiming? Tick()
    {
        lock (_gate)
        {
            if (MeasureCore() is not { } timing)
            {
                return null;
            }

            var now = _time.GetTimestamp();

            if (_moving && _time.GetElapsedTime(_moveStart, now) >= _moveDuration)
            {
                EndMove(settleAtTarget: true);
            }

            var speed = _moving ? MoveSpeed() : Steer(timing.Error);
            if (timing.Error.Duration() > SeekThreshold)
            {
                _renderer.Seek(TimeSpan.FromTicks(Math.Max(0, (_clock.PositionAt(now) - DelayAt(now)).Ticks)));
                _commandedAt = now;
                speed = _moving ? MoveSpeed() : 1.0;
            }

            ApplySpeed(speed);
            return timing;
        }
    }

    /// <summary>
    /// The picture's timing as <see cref="Tick"/> sees it — the snapshot against the due position at the snapshot's own
    /// time — without steering anything; <c>null</c> when <see cref="Tick"/> would have nothing to measure.
    /// </summary>
    public PictureTiming? Measure()
    {
        lock (_gate)
        {
            return MeasureCore();
        }
    }

    private PictureTiming? MeasureCore()
    {
        if (_renderer.Presented is not { } presented)
        {
            return null;
        }

        var clock = _clock.PositionAt(presented.Timestamp);
        var due = clock - DelayAt(presented.Timestamp);
        if (!_clock.IsRunning || presented.Timestamp < _holdUntil || presented.Timestamp < _commandedAt || due < TimeSpan.Zero)
        {
            // The held first frame is not an error, a picture from before the last command is not the commanded one,
            // and there is no content before position zero to steer or seek to.
            return null;
        }

        return new PictureTiming(due, presented.Position) { Clock = clock, Timestamp = presented.Timestamp };
    }

    /// <summary>
    /// Moves the picture delay to <paramref name="newPictureDelay"/> at ≤ 0.2 % speed change; retargets a move under way.
    /// A <paramref name="moveDuration"/> longer than the change needs at 0.2 % (the outputs' lags move further, AC-14)
    /// stretches the move: the picture slows its rate so it arrives together with them.
    /// </summary>
    public void MoveTo(TimeSpan newPictureDelay, TimeSpan moveDuration = default)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            var current = DelayAt(now);
            var delta = newPictureDelay - current;

            EndMove(settleAtTarget: false);
            _pictureDelay = current;
            if (delta == TimeSpan.Zero)
            {
                ApplySpeed(1.0);
                return;
            }

            _moving = true;
            _moveFrom = current;
            _moveTarget = newPictureDelay;
            _moveStart = now;
            var shortest = MoveDurationFor(delta);
            _moveDuration = moveDuration > shortest ? moveDuration : shortest;
            _moveRate = Math.Abs(delta.Ticks) / (double)_moveDuration.Ticks;
            ApplySpeed(MoveSpeed());

            // The move ends on its own at the target even when nobody ticks: the timer runs on the injected clock.
            var version = ++_moveVersion;
            _moveTimer = _time.CreateTimer(
                _ =>
                {
                    if (_post is { } post)
                    {
                        post(() => FinishMove(version));
                    }
                    else
                    {
                        FinishMove(version);
                    }
                },
                null,
                _moveDuration,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Gets how long a move of <paramref name="change"/> takes at the largest allowed rate, 0.2 %.</summary>
    public static TimeSpan MoveDurationFor(TimeSpan change) =>
        TimeSpan.FromTicks((long)Math.Round(Math.Abs(change.Ticks) / MaxSpeedDeviation));

    /// <summary>Sets the picture delay at once and re-aligns the picture (seek, file change, resume).</summary>
    public void SetAtOnce(TimeSpan newPictureDelay)
    {
        lock (_gate)
        {
            EndMove(settleAtTarget: false);
            _pictureDelay = newPictureDelay;
            var now = _time.GetTimestamp();
            if (_clock.IsRunning)
            {
                AlignAt(_clock.PositionAt(now), now);
                return;
            }

            // Paused: only move the paused picture; resume re-aligns and starts it.
            ApplySpeed(1.0, force: true);
            _holdUntil = long.MinValue;
            _renderer.Seek(TimeSpan.FromTicks(Math.Max(0, (_clock.PositionAt(now) - newPictureDelay).Ticks)));
        }
    }

    /// <summary>Stops the timer and the move; the delay settles at the move's target or stays where it is.</summary>
    private void EndMove(bool settleAtTarget)
    {
        _moveVersion++;
        _moveTimer?.Dispose();
        _moveTimer = null;
        if (_moving && settleAtTarget)
        {
            _pictureDelay = _moveTarget;
        }

        _moving = false;
    }

    private void FinishMove(long version)
    {
        lock (_gate)
        {
            if (!_moving || version != _moveVersion)
            {
                return;
            }

            EndMove(settleAtTarget: true);
            ApplySpeed(1.0);
        }
    }

    private TimeSpan DelayAt(long timestamp)
    {
        if (!_moving)
        {
            return _pictureDelay;
        }

        var elapsed = _time.GetElapsedTime(_moveStart, timestamp);
        if (elapsed <= TimeSpan.Zero)
        {
            return _moveFrom;
        }

        if (elapsed >= _moveDuration)
        {
            return _moveTarget;
        }

        var moved = TimeSpan.FromTicks((long)Math.Round(elapsed.Ticks * _moveRate));
        return _moveTarget > _moveFrom ? _moveFrom + moved : _moveFrom - moved;
    }

    // A larger picture delay means the picture is shown later: it runs slower; a smaller one runs faster.
    private double MoveSpeed() => _moveTarget > _moveFrom ? 1.0 - _moveRate : 1.0 + _moveRate;

    private static double Steer(TimeSpan error)
    {
        if (error.Duration() < Deadband)
        {
            return 1.0;
        }

        return 1.0 - Math.Clamp(error.TotalSeconds / SteerTimeConstantSeconds, -MaxSpeedDeviation, MaxSpeedDeviation);
    }

    private void ApplySpeed(double speed, bool force = false)
    {
        if (!force && speed == _speed)
        {
            return;
        }

        _speed = speed;
        _renderer.SetSpeed(speed);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            EndMove(settleAtTarget: false);
        }
    }

    private long ToTimestamp(TimeSpan span) => (long)Math.Round(span.TotalSeconds * _time.TimestampFrequency);
}
