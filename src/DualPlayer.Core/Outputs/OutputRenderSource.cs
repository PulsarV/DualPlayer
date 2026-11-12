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
    private int _realignsApplied;
    private long _underruns;
    private long _framesPlayed;
    private long _timestamp;
    private long _framesWritten;
    private long _lastContentFrame = -1;
    private long _previousEpochLastContentFrame = -1;
    private long _oldEpochEndFramesWritten;
    private int _positionVersion; // odd while the position pair is being written
    private bool _hasPosition; // set after the first render published a position
    private int _bufferVersion; // odd while the audio-buffer pair below is being written
    private long _bufferDeviceFrame = -1;
    private long _bufferContentBits;
    private bool _awaitingFirstFrame = true; // render thread only
    private long _skipBase; // render thread only: ring skipped-frame count when the epoch was applied

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

    /// <summary>A consistent pair: the device position the last render saw and the time it was read; false while a render is updating it.</summary>
    public bool TryGetLatestPosition(out long framesPlayed, out long timestamp)
    {
        var before = Volatile.Read(ref _positionVersion);
        framesPlayed = Interlocked.Read(ref _framesPlayed);
        timestamp = Interlocked.Read(ref _timestamp);
        return Volatile.Read(ref _hasPosition) && before % 2 == 0 && before == Volatile.Read(ref _positionVersion);
    }

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

    /// <summary>Content frame the newest requested epoch starts at.</summary>
    public long EpochStartContentFrame => Volatile.Read(ref _epochState).StartContentFrame;

    public void BeginEpoch(int epoch, long startContentFrame) =>
        Volatile.Write(ref _epochState, new EpochState(epoch, startContentFrame));

    /// <summary>Content frame the source falls silent at; <see cref="long.MaxValue"/> when it plays on.</summary>
    public long SilenceFrom => Interlocked.Read(ref _silenceFrom);

    /// <summary>
    /// The most recent device buffer filled entirely with audio since the epoch began or since
    /// <see cref="ClearSilence"/>: its first device frame and the content position (in frames, fractional) of its
    /// first audio frame. False until one exists, or while a render is updating it.
    /// </summary>
    public bool TryGetLatestAudioBuffer(out long deviceFrame, out double contentPosition)
    {
        var before = Volatile.Read(ref _bufferVersion);
        deviceFrame = Interlocked.Read(ref _bufferDeviceFrame);
        contentPosition = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _bufferContentBits));
        return before % 2 == 0 && deviceFrame >= 0 && before == Volatile.Read(ref _bufferVersion);
    }

    public void SetSilenceFrom(long contentFrame) => Interlocked.Exchange(ref _silenceFrom, contentFrame);

    public void ClearSilence()
    {
        PublishAudioBuffer(-1, 0);
        Interlocked.Exchange(ref _silenceFrom, long.MaxValue);
    }

    public void SetResamplerRatio(double ratio) =>
        Interlocked.Exchange(ref _ratioBits, BitConverter.DoubleToInt64Bits(ratio));

    /// <summary>Number of re-alignments the render thread has applied; each one invalidates <see cref="TryGetLatestAudioBuffer"/>.</summary>
    public int RealignsApplied => Volatile.Read(ref _realignsApplied);

    public void RequestRealign(int frames) => Interlocked.Add(ref _pendingRealign, frames);

    public void Render(Span<float> buffer, DevicePosition position)
    {
        Interlocked.Increment(ref _positionVersion);
        Interlocked.Exchange(ref _framesPlayed, position.FramesPlayed);
        Interlocked.Exchange(ref _timestamp, position.Timestamp);
        Interlocked.Increment(ref _positionVersion);
        Volatile.Write(ref _hasPosition, true);

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
            PublishAudioBuffer(-1, 0); // later buffers are the ones that show the effect
            Interlocked.Increment(ref _realignsApplied);
        }

        var frames = buffer.Length / _ring.Channels;
        var produced = 0;
        var skippedBefore = _ring.SkippedFramesConsumed - _skipBase;
        var limit = (double)(Interlocked.Read(ref _silenceFrom) - state.StartContentFrame - skippedBefore);
        var startPosition = _resampler.NextOutputPosition;
        var insertPending = _resampler.PendingInsertFrames != 0;
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
            var skipped = _ring.SkippedFramesConsumed - _skipBase;
            if (produced == frames && !insertPending && _resampler.PendingInsertFrames == 0 && skipped == skippedBefore)
            {
                PublishAudioBuffer(Interlocked.Read(ref _framesWritten), state.StartContentFrame + skippedBefore + startPosition);
            }

            var last = state.StartContentFrame + skipped + (long)Math.Floor(_resampler.LastOutputPosition);
            Interlocked.Exchange(ref _lastContentFrame, last);
        }

        Interlocked.Add(ref _framesWritten, frames);
    }

    private void PublishAudioBuffer(long deviceFrame, double contentPosition)
    {
        Interlocked.Increment(ref _bufferVersion);
        Interlocked.Exchange(ref _bufferDeviceFrame, deviceFrame);
        Interlocked.Exchange(ref _bufferContentBits, BitConverter.DoubleToInt64Bits(contentPosition));
        Interlocked.Increment(ref _bufferVersion);
    }

    private void ApplyEpoch(EpochState state)
    {
        if (_appliedState is not null)
        {
            Interlocked.Exchange(ref _previousEpochLastContentFrame, Interlocked.Read(ref _lastContentFrame));
            Interlocked.Exchange(ref _pendingRealign, 0); // a correction measured in the old epoch does not apply to the new one
        }

        Interlocked.Exchange(ref _oldEpochEndFramesWritten, Interlocked.Read(ref _framesWritten));
        _appliedState = state;
        _skipBase = _ring.SkippedFramesConsumed;
        _resampler.Reset();
        PublishAudioBuffer(-1, 0);
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
