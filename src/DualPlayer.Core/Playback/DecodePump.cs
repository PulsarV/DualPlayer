using DualPlayer.Core.Outputs;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Core.Playback;

/// <summary>
/// The decode thread: pulls blocks from one <see cref="IDecodeSession"/> and fans them out to one ring per
/// output. A full ring never stalls the others: its frames are dropped while another non-stalled ring has room.
/// An output whose ring has not been read for more than <see cref="StallThreshold"/> no longer paces the decoder.
/// </summary>
public sealed partial class DecodePump : IDisposable
{
    public static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan WaitSlice = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan DropLogInterval = TimeSpan.FromSeconds(1);

    private readonly IDecodeSession _session;
    private readonly AudioRingBuffer[] _rings;
    private readonly TimeProvider _time;
    private readonly ILogger<DecodePump> _logger;
    private readonly bool[] _detached;
    private readonly int[] _stallLatched;
    private readonly long[] _lastDropLog;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _seekLock = new();
    private readonly ManualResetEventSlim _seekRequested = new();
    private (TimeSpan Position, int Epoch)? _pendingSeek;
    private Thread? _thread;
    private long _startTimestamp;
    private int _epoch;
    private int _endedEpoch = -1;
    private int _disposed;
    private bool _releaseOnExit; // pump thread only

    public DecodePump(IDecodeSession session, IReadOnlyList<AudioRingBuffer> rings, TimeProvider time, ILogger<DecodePump> logger)
    {
        _session = session;
        _rings = [.. rings];
        _time = time;
        _logger = logger;
        _detached = new bool[_rings.Length];
        _stallLatched = new int[_rings.Length];
        _lastDropLog = new long[_rings.Length];
    }

    /// <summary>
    /// End of file reached; all blocks of the epoch in <see cref="EndedEpoch"/> were delivered. The pump thread then
    /// waits for a seek, so a later <see cref="Seek"/> still decodes.
    /// </summary>
    public event Action? Ended;

    /// <summary>Epoch whose blocks reached the end of the file; -1 while no epoch has.</summary>
    public int EndedEpoch => Volatile.Read(ref _endedEpoch);

    /// <summary>An unexpected decode failure (a bug); the pump thread ends after raising this once.</summary>
    public event Action<Exception>? Faulted;

    /// <summary>An expected decoder failure (read or seek); the pump thread ends after raising this once.</summary>
    public event Action<Error>? DecodeFailed;

    public void Start(int epoch)
    {
        _epoch = epoch;
        _startTimestamp = _time.GetTimestamp();
        _thread = new Thread(Run) { Name = "DecodePump", IsBackground = true };
        _thread.Start();
    }

    /// <summary>
    /// Stops writing to the output's ring for good (device lost). A block already being written when this is
    /// called may still land; nothing after it does.
    /// </summary>
    public void Detach(int outputIndex) => Volatile.Write(ref _detached[outputIndex], true);

    /// <summary>
    /// Re-seeks the decoder on the pump thread (also after the end of the file); frames decoded afterwards carry
    /// <paramref name="epoch"/>.
    /// </summary>
    public void Seek(TimeSpan position, int epoch)
    {
        lock (_seekLock)
        {
            _pendingSeek = (position, epoch);
            _seekRequested.Set();
        }
    }

    /// <summary>True when the output is attached and its ring has not been read for more than one second.</summary>
    public bool IsStalled(int outputIndex)
    {
        if (Volatile.Read(ref _detached[outputIndex]))
        {
            return false;
        }

        var lastRead = Math.Max(_rings[outputIndex].LastReadTimestamp, Volatile.Read(ref _startTimestamp));
        var stalled = _time.GetElapsedTime(lastRead) >= StallThreshold;
        if (stalled)
        {
            Volatile.Write(ref _stallLatched[outputIndex], 1);
        }

        return stalled;
    }

    /// <summary>
    /// True once after an output that was stalled has started reading again, so its drift controller can
    /// re-align it in one step. Consumes the flag.
    /// </summary>
    public bool TryConsumeResumedFromStall(int outputIndex) =>
        Volatile.Read(ref _stallLatched[outputIndex]) == 1
        && !IsStalled(outputIndex)
        && Interlocked.CompareExchange(ref _stallLatched[outputIndex], 0, 1) == 1;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        if (_thread == Thread.CurrentThread)
        {
            _releaseOnExit = true; // disposed from an event handler: the pump thread releases on its way out
            return;
        }

        _thread?.Join();
        Release();
    }

    private void Run()
    {
        try
        {
            Pump();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogDecodeFault(ex);
            Faulted?.Invoke(ex);
        }
        finally
        {
            if (_releaseOnExit)
            {
                Release();
            }
        }
    }

    private void Release()
    {
        _cts.Dispose();
        _seekRequested.Dispose();
        _session.Dispose();
    }

    private void Pump()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            if (!ApplyPendingSeek())
            {
                return;
            }

            ObserveStalls();
            var read = _session.Read();
            if (!read.IsSuccess)
            {
                if (read.Error.Code != "playback.end_of_stream")
                {
                    ReportDecodeFailure(read.Error);
                    return;
                }

                Volatile.Write(ref _endedEpoch, _epoch);
                Ended?.Invoke();
                WaitForSeek(token);
                continue;
            }

            Deliver(read.Value, token);
        }
    }

    /// <summary>Blocks the pump thread after the end of the file until a seek is requested or the pump is disposed.</summary>
    private void WaitForSeek(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (_seekLock)
            {
                if (_pendingSeek is not null)
                {
                    return;
                }

                _seekRequested.Reset();
            }

            _seekRequested.Wait(token);
        }
    }

    /// <summary>Applies a requested seek; false when the decoder failed to seek (already reported).</summary>
    private bool ApplyPendingSeek()
    {
        (TimeSpan Position, int Epoch)? seek;
        lock (_seekLock)
        {
            seek = _pendingSeek;
            _pendingSeek = null;
        }

        if (seek is not { } s)
        {
            return true;
        }

        var result = _session.Seek(s.Position, s.Epoch);
        if (!result.IsSuccess)
        {
            ReportDecodeFailure(result.Error);
            return false;
        }

        _epoch = s.Epoch;
        return true;
    }

    private void ReportDecodeFailure(Error error)
    {
        LogDecodeFailed(error.Code, error.Message);
        DecodeFailed?.Invoke(error);
    }

    /// <summary>Latches every stall on the pump thread, so a resume is flagged even if nobody else asked.</summary>
    private void ObserveStalls()
    {
        for (var i = 0; i < _rings.Length; i++)
        {
            _ = IsStalled(i);
        }
    }

    private void Deliver(DecodedBlock block, CancellationToken token)
    {
        var index = block.OutputIndex;
        var ring = _rings[index];
        ReadOnlySpan<float> pending = block.Samples;

        while (!Volatile.Read(ref _detached[index]))
        {
            var frames = pending.Length / ring.Channels;
            if (frames == 0)
            {
                return;
            }

            // A block larger than the ring goes in ring-sized pieces.
            var piece = Math.Min(frames, ring.CapacityFrames);
            if (ring.FreeFrames >= piece)
            {
                if (Volatile.Read(ref _detached[index]))
                {
                    return;
                }

                var written = ring.Write(pending, _epoch);
                pending = pending[(written * ring.Channels)..];
                continue;
            }

            if (OtherNonStalledRingHasRoom(index, piece))
            {
                LogDrop(index);
                return;
            }

            // Every non-stalled ring is full: wait for a reader, but let a seek restart the block.
            Task.Delay(WaitSlice, _time, token).Wait(token);
            ObserveStalls();
            lock (_seekLock)
            {
                if (_pendingSeek is not null)
                {
                    return;
                }
            }
        }
    }

    private bool OtherNonStalledRingHasRoom(int self, int frames)
    {
        for (var i = 0; i < _rings.Length; i++)
        {
            if (i != self && !Volatile.Read(ref _detached[i]) && !IsStalled(i) && _rings[i].FreeFrames >= frames)
            {
                return true;
            }
        }

        return false;
    }

    private void LogDrop(int index)
    {
        var now = _time.GetTimestamp();
        if (_lastDropLog[index] != 0 && _time.GetElapsedTime(_lastDropLog[index], now) < DropLogInterval)
        {
            return;
        }

        _lastDropLog[index] = now;
        LogDroppedFrames(index);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Output {OutputIndex} ring is full; dropping its decoded frames")]
    private partial void LogDroppedFrames(int outputIndex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Decoder failed ({ErrorCode}): {ErrorMessage}")]
    private partial void LogDecodeFailed(string errorCode, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Decode pump failed")]
    private partial void LogDecodeFault(Exception ex);
}
