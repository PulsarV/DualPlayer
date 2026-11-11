using DualPlayer.Core.Sync;

namespace DualPlayer.Core.Outputs;

/// <summary>
/// Per-output source the device thread pulls from: drops older-epoch frames, applies the controller's
/// resampler ratio, falls silent at the commanded content frame and counts underruns. Control-loop setters
/// are atomic; <see cref="Render"/> never allocates, locks, logs or throws.
/// </summary>
public sealed class OutputRenderSource : IRenderSource
{
    private readonly AudioRingBuffer _ring;
    private readonly AdaptiveResampler _resampler;
    private readonly CountingReader _reader;

    private EpochState _epochState = new(0, 0);
    private EpochState? _appliedState; // render thread only
    private int _appliedEpoch = -1;
    private long _silenceFrom = long.MaxValue;
    private long _ratioBits = BitConverter.DoubleToInt64Bits(1.0);
    private int _pendingRealign;
    private long _underruns;
    private long _framesPlayed;
    private long _timestamp;
    private long _framesWritten;
    private long _lastContentFrame = -1;
    private long _previousEpochLastContentFrame = -1;
    private long _oldEpochEndFramesWritten;
    private bool _awaitingFirstFrame = true; // render thread only

    public OutputRenderSource(AudioRingBuffer ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        _ring = ring;
        _resampler = new AdaptiveResampler(ring.Channels);
        _reader = new CountingReader(ring);
    }

    public long UnderrunCount => Interlocked.Read(ref _underruns);

    public long LatestFramesPlayed => Interlocked.Read(ref _framesPlayed);

    public long LatestTimestamp => Interlocked.Read(ref _timestamp);

    /// <summary>Frames written to the device and not yet played (never understated by a torn read).</summary>
    public long QueuedFrames
    {
        get
        {
            // Render publishes played first and written last, so read in the same order.
            var played = LatestFramesPlayed;
            return Interlocked.Read(ref _framesWritten) - played;
        }
    }

    /// <summary>Content frame of the last audio frame handed to the device in the current epoch.</summary>
    public long LastContentFrameHandedOut => Interlocked.Read(ref _lastContentFrame);

    /// <summary>Epoch the render thread has applied (old-epoch ring frames discarded); -1 before the first render.</summary>
    public int AppliedEpoch => Volatile.Read(ref _appliedEpoch);

    /// <summary>Content frame of the last audio frame handed out in the epoch before <see cref="AppliedEpoch"/>.</summary>
    public long PreviousEpochLastContentFrame => Interlocked.Read(ref _previousEpochLastContentFrame);

    /// <summary>Frames written to the device when <see cref="AppliedEpoch"/> was applied: every older-epoch frame lies before it.</summary>
    public long OldEpochEndFramesWritten => Interlocked.Read(ref _oldEpochEndFramesWritten);

    /// <summary>
    /// Seek-confirmation gate: true once <paramref name="epoch"/> (or newer) is applied and the device has played
    /// every frame written before it, so no older-epoch audio can still be heard.
    /// </summary>
    public bool HasPlayedOutOldEpoch(int epoch) =>
        AppliedEpoch >= epoch && LatestFramesPlayed >= OldEpochEndFramesWritten;

    public void BeginEpoch(int epoch, long startContentFrame) =>
        Volatile.Write(ref _epochState, new EpochState(epoch, startContentFrame));

    public void SetSilenceFrom(long contentFrame) => Interlocked.Exchange(ref _silenceFrom, contentFrame);

    public void ClearSilence() => Interlocked.Exchange(ref _silenceFrom, long.MaxValue);

    public void SetResamplerRatio(double ratio) =>
        Interlocked.Exchange(ref _ratioBits, BitConverter.DoubleToInt64Bits(ratio));

    public void RequestRealign(int frames) => Interlocked.Add(ref _pendingRealign, frames);

    public void Render(Span<float> buffer, DevicePosition position)
    {
        Interlocked.Exchange(ref _framesPlayed, position.FramesPlayed);
        Interlocked.Exchange(ref _timestamp, position.Timestamp);

        var state = Volatile.Read(ref _epochState);
        var epochChanged = !ReferenceEquals(state, _appliedState);
        if (epochChanged)
        {
            ApplyEpoch(state);
        }

        // Refreshes the stall timestamp and drops old-epoch frames even while silent.
        _ = _ring.BeginRead(state.Epoch, position.Timestamp);
        if (epochChanged)
        {
            Volatile.Write(ref _appliedEpoch, state.Epoch);
        }

        var realign = Interlocked.Exchange(ref _pendingRealign, 0);
        if (realign != 0)
        {
            _resampler.Realign(realign);
        }

        var frames = buffer.Length / _ring.Channels;
        var produced = 0;
        var limit = (double)(Interlocked.Read(ref _silenceFrom) - state.StartContentFrame);
        var startPosition = _resampler.NextOutputPosition;
        if (startPosition < limit)
        {
            var ratio = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _ratioBits));
            _reader.FramesRead = 0;
            produced = _resampler.Process(_reader, buffer, ratio, limit);
            _awaitingFirstFrame &= _reader.FramesRead == 0;

            // Underrun: the ring ran dry while the whole buffer should have been audio.
            var lastNeeded = startPosition + ((frames - 1) / ratio);
            if (produced < frames && lastNeeded < limit && !_awaitingFirstFrame)
            {
                Interlocked.Increment(ref _underruns);
            }
        }

        buffer[(produced * _ring.Channels)..].Clear();

        if (produced > 0)
        {
            var last = state.StartContentFrame + (long)Math.Floor(_resampler.LastOutputPosition);
            Interlocked.Exchange(ref _lastContentFrame, last);
        }

        Interlocked.Add(ref _framesWritten, frames);
    }

    private void ApplyEpoch(EpochState state)
    {
        if (_appliedState is not null)
        {
            Interlocked.Exchange(ref _previousEpochLastContentFrame, Interlocked.Read(ref _lastContentFrame));
        }

        Interlocked.Exchange(ref _oldEpochEndFramesWritten, Interlocked.Read(ref _framesWritten));
        _appliedState = state;
        _resampler.Reset();
        _awaitingFirstFrame = true;
        Interlocked.Exchange(ref _lastContentFrame, state.StartContentFrame - 1);
    }

    private sealed record EpochState(int Epoch, long StartContentFrame);

    private sealed class CountingReader(AudioRingBuffer ring) : IFrameReader
    {
        public int FramesRead { get; set; }

        public int Channels => ring.Channels;

        public int Read(Span<float> destination)
        {
            var read = ring.Read(destination);
            FramesRead += read;
            return read;
        }
    }
}
