namespace DualPlayer.Core.Outputs;

/// <summary>
/// Lock-free single-producer/single-consumer ring of interleaved float frames. Every frame carries the
/// playback epoch it was decoded in; the reader skips frames from an older epoch. Pre-allocated, so
/// <see cref="Read"/> never allocates, locks or throws on the render thread.
/// </summary>
public sealed class AudioRingBuffer
{
    private readonly float[] _samples;
    private readonly int[] _epochs;
    private readonly int _capacity;
    private readonly int _channels;
    private long _head; // frames consumed; written by the reader only
    private long _tail; // frames produced; written by the writer only
    private long _lastReadTimestamp;

    public AudioRingBuffer(int capacityFrames, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        _capacity = capacityFrames;
        _channels = channels;
        _samples = new float[capacityFrames * channels];
        _epochs = new int[capacityFrames];
    }

    public int Channels => _channels;

    public int CapacityFrames => _capacity;

    public int FramesAvailable => (int)(Volatile.Read(ref _tail) - Volatile.Read(ref _head));

    public int FreeFrames => _capacity - FramesAvailable;

    /// <summary>The timestamp the reader passed on its most recent <see cref="Read"/>.</summary>
    public long LastReadTimestamp => Volatile.Read(ref _lastReadTimestamp);

    /// <summary>Decode thread. Writes as many whole frames as fit and returns the count accepted; never blocks.</summary>
    public int Write(ReadOnlySpan<float> frames, int epoch)
    {
        var tail = Volatile.Read(ref _tail);
        var free = _capacity - (int)(tail - Volatile.Read(ref _head));
        var count = Math.Min(frames.Length / _channels, free);

        for (var i = 0; i < count; i++)
        {
            var slot = (int)((tail + i) % _capacity);
            frames.Slice(i * _channels, _channels).CopyTo(_samples.AsSpan(slot * _channels, _channels));
            _epochs[slot] = epoch;
        }

        Volatile.Write(ref _tail, tail + count);
        return count;
    }

    /// <summary>
    /// Render thread. Skips frames older than <paramref name="currentEpoch"/> (reported in
    /// <paramref name="discardedFrames"/>), copies the rest into <paramref name="dest"/> and returns frames read.
    /// </summary>
    public int Read(Span<float> dest, int currentEpoch, out int discardedFrames, long readTimestamp = 0)
    {
        Volatile.Write(ref _lastReadTimestamp, readTimestamp);

        var head = Volatile.Read(ref _head);
        var tail = Volatile.Read(ref _tail);
        var maxFrames = dest.Length / _channels;
        var read = 0;
        var discarded = 0;

        while (head < tail)
        {
            var slot = (int)(head % _capacity);
            if (_epochs[slot] < currentEpoch)
            {
                discarded++;
                head++;
                continue;
            }

            if (read >= maxFrames)
            {
                break;
            }

            _samples.AsSpan(slot * _channels, _channels).CopyTo(dest.Slice(read * _channels, _channels));
            read++;
            head++;
        }

        Volatile.Write(ref _head, head);
        discardedFrames = discarded;
        return read;
    }
}
