namespace DualPlayer.Core.Outputs;

/// <summary>
/// Lock-free single-producer/single-consumer ring of interleaved float frames. Every frame carries the
/// playback epoch it was decoded in; the reader skips frames from an older epoch. Pre-allocated, so
/// <see cref="Read(Span{float}, int, out int, long)"/> never allocates, locks, waits or throws on the render thread.
/// </summary>
/// <remarks>
/// The writer normally only moves the tail and the reader only the head. The one exception is
/// <see cref="TryReclaimOldest"/>, where the writer moves the head of a stalled reader. Reading and reclaiming are
/// therefore mutually exclusive through one owner flag that each side takes with a compare-and-swap and never waits
/// for: a read that finds a reclaim in progress returns no frames, and a reclaim that finds a read in progress drops
/// nothing and returns false. So the head and the skip count are only ever moved by whichever side owns the ring,
/// and no frame (or the skip count it carries) is passed twice.
/// </remarks>
public sealed class AudioRingBuffer : DualPlayer.Core.Sync.IFrameReader
{
    private const int Free = 0;
    private const int Reading = 1;
    private const int Reclaiming = 2;

    private readonly float[] _samples;
    private readonly int[] _epochs;
    private readonly int[] _skippedBefore; // frames the writer skipped immediately before this slot's frame
    private readonly int _capacity;
    private readonly int _channels;
    private int _owner; // Free, Reading (render thread) or Reclaiming (decode thread); taken by CAS, released by a volatile write
    private long _head; // frames consumed; written only by the owner
    private long _tail; // frames produced; written by the writer only
    private long _lastReadTimestamp;
    private long _skippedConsumed; // skipped frames the reader has passed; written only by the reader while it owns the ring
    private int _pendingSkip; // writer only
    private int _pendingSkipEpoch; // writer only
    private int _readerEpoch; // written by the render thread only
    private int _lastWrittenEpoch = -1; // writer only; published after the tail
    private long _readerTimestamp; // render thread only

    public AudioRingBuffer(int capacityFrames, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        _capacity = capacityFrames;
        _channels = channels;
        _samples = new float[capacityFrames * channels];
        _epochs = new int[capacityFrames];
        _skippedBefore = new int[capacityFrames];
    }

    public int Channels => _channels;

    /// <summary>
    /// Render thread, once per device period: sets the epoch and timestamp later <see cref="IFrameReader"/> reads
    /// use, refreshes <see cref="LastReadTimestamp"/> (so a paused reader is not seen as stalled) and discards
    /// older-epoch frames at the head even when nothing is read. Returns the frames discarded (0 when a reclaim was
    /// in progress; they are discarded on the next read).
    /// </summary>
    public int BeginRead(int currentEpoch, long readTimestamp)
    {
        Volatile.Write(ref _readerEpoch, currentEpoch);
        _readerTimestamp = readTimestamp;
        _ = Read([], currentEpoch, out var discarded, readTimestamp);
        return discarded;
    }

    /// <summary>Reads frames with the epoch and timestamp of the last <see cref="BeginRead"/>; the resampler's input.</summary>
    public int Read(Span<float> destination) => Read(destination, Volatile.Read(ref _readerEpoch), out _, _readerTimestamp);

    public int CapacityFrames => _capacity;

    public int FramesAvailable => (int)(Volatile.Read(ref _tail) - Volatile.Read(ref _head));

    public int FreeFrames => _capacity - FramesAvailable;

    /// <summary>
    /// Epoch of the most recently written frame; -1 before the first write. Once it reaches an epoch, frames of that
    /// epoch have been written (whether or not they are still in the ring), so a late older-epoch block left in the ring
    /// is never mistaken for audio of a newer epoch.
    /// </summary>
    public int LastWrittenEpoch => Volatile.Read(ref _lastWrittenEpoch);

    /// <summary>The timestamp the reader passed on its most recent <see cref="Read(Span{float}, int, out int, long)"/>.</summary>
    public long LastReadTimestamp => Volatile.Read(ref _lastReadTimestamp);

    /// <summary>Skipped frames (see <see cref="NoteSkipped"/>) the reader has passed in non-discarded frames.</summary>
    public long SkippedFramesConsumed => Volatile.Read(ref _skippedConsumed);

    /// <summary>
    /// Decode thread. Records that <paramref name="frames"/> frames of <paramref name="epoch"/> were not written, so the
    /// reader can keep its content position true; they are attached to the next frame written in that epoch.
    /// </summary>
    public void NoteSkipped(int frames, int epoch)
    {
        if (_pendingSkipEpoch != epoch)
        {
            _pendingSkip = 0;
            _pendingSkipEpoch = epoch;
        }

        _pendingSkip += frames;
    }

    /// <summary>
    /// Decode thread, for a reader that has stopped reading (it hung): frees room for <paramref name="frames"/> more
    /// frames by dropping the oldest ones instead of the new ones, so when the reader returns it plays the newest
    /// audio. Dropped frames of <paramref name="epoch"/> (and the skips they carried) are counted as skipped before
    /// the oldest kept frame, so the content position stays true. Returns false, dropping nothing, when there is
    /// nothing to drop or the reader is reading right now (it is back; check the free space again).
    /// </summary>
    public bool TryReclaimOldest(int frames, int epoch)
    {
        if (Interlocked.CompareExchange(ref _owner, Reclaiming, Free) != Free)
        {
            return false;
        }

        var tail = _tail;
        var head = Volatile.Read(ref _head);
        var drop = Math.Min(tail - head, tail - head + frames - _capacity);
        if (drop <= 0)
        {
            Volatile.Write(ref _owner, Free);
            return false;
        }

        var newHead = head + drop;
        long dropped = 0;
        for (var position = head; position < newHead; position++)
        {
            var slot = (int)(position % _capacity);
            if (_epochs[slot] == epoch)
            {
                dropped += 1 + _skippedBefore[slot];
            }
        }

        if (dropped > 0)
        {
            if (newHead < tail)
            {
                _skippedBefore[(int)(newHead % _capacity)] += (int)dropped;
            }
            else
            {
                NoteSkipped((int)dropped, epoch);
            }
        }

        Volatile.Write(ref _head, newHead);
        Volatile.Write(ref _owner, Free);
        return true;
    }

    /// <summary>Decode thread. Writes as many whole frames as fit and returns the count accepted; never blocks.</summary>
    public int Write(ReadOnlySpan<float> frames, int epoch)
    {
        var tail = _tail;
        var free = _capacity - (int)(tail - Volatile.Read(ref _head));
        var count = Math.Min(frames.Length / _channels, free);

        for (var i = 0; i < count; i++)
        {
            var slot = (int)((tail + i) % _capacity);
            frames.Slice(i * _channels, _channels).CopyTo(_samples.AsSpan(slot * _channels, _channels));
            _epochs[slot] = epoch;
            _skippedBefore[slot] = i == 0 && _pendingSkipEpoch == epoch ? _pendingSkip : 0;
        }

        if (count > 0)
        {
            _pendingSkip = 0;
        }

        Volatile.Write(ref _tail, tail + count);
        if (count > 0)
        {
            Volatile.Write(ref _lastWrittenEpoch, epoch);
        }

        return count;
    }

    /// <summary>
    /// Render thread. Skips frames older than <paramref name="currentEpoch"/> (reported in
    /// <paramref name="discardedFrames"/>), copies the rest into <paramref name="dest"/> and returns frames read.
    /// Returns 0 without waiting while the writer is reclaiming (see <see cref="TryReclaimOldest"/>); that can only
    /// happen on the first read after a stall, and the frames are there for the next read.
    /// </summary>
    public int Read(Span<float> dest, int currentEpoch, out int discardedFrames, long readTimestamp = 0)
    {
        Volatile.Write(ref _lastReadTimestamp, readTimestamp);
        discardedFrames = 0;
        if (Interlocked.CompareExchange(ref _owner, Reading, Free) != Free)
        {
            return 0;
        }

        var head = _head;
        var tail = Volatile.Read(ref _tail);
        var maxFrames = dest.Length / _channels;
        var read = 0;
        var skipped = 0L;

        while (head < tail)
        {
            var slot = (int)(head % _capacity);
            if (_epochs[slot] < currentEpoch)
            {
                discardedFrames++;
                head++;
                continue;
            }

            if (read >= maxFrames)
            {
                break;
            }

            skipped += _skippedBefore[slot];
            _samples.AsSpan(slot * _channels, _channels).CopyTo(dest.Slice(read * _channels, _channels));
            read++;
            head++;
        }

        if (skipped != 0)
        {
            Volatile.Write(ref _skippedConsumed, _skippedConsumed + skipped);
        }

        Volatile.Write(ref _head, head);
        Volatile.Write(ref _owner, Free);
        return read;
    }
}
