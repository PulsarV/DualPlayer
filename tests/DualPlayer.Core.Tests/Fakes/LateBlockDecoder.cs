using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>
/// Delegates to another decoder, but once armed holds the next read in flight: the block it already decoded is only
/// handed over when the test releases it, so a seek can be requested while a block of the old epoch is still being
/// decoded, and that block lands late (after the devices have applied the new epoch).
/// </summary>
internal sealed class LateBlockDecoder(IAudioDecoder inner) : IAudioDecoder
{
    private readonly ManualResetEventSlim _released = new(true);
    private volatile bool _armed;
    private volatile bool _inRead;
    private volatile bool _seeked;
    private volatile bool _endedAfterSeek;

    /// <summary>The next read decodes its block and then waits for <see cref="Release"/>.</summary>
    public void Arm()
    {
        _released.Reset();
        _armed = true;
    }

    /// <summary>True while a read holds a decoded block back.</summary>
    public bool Holding => _inRead;

    /// <summary>True once the decoder, after a seek, reported the end of the stream.</summary>
    public bool EndedAfterSeek => _endedAfterSeek;

    public void Release()
    {
        _armed = false;
        _released.Set();
    }

    public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path) => inner.ListTracks(path);

    public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs)
    {
        var opened = inner.Open(path, outputs);
        return opened.IsSuccess ? Result<IDecodeSession>.Success(new Session(this, opened.Value)) : opened;
    }

    private sealed class Session(LateBlockDecoder owner, IDecodeSession session) : IDecodeSession
    {
        public TimeSpan? Duration => session.Duration;

        public Result Seek(TimeSpan position, int epoch)
        {
            owner._seeked = true;
            return session.Seek(position, epoch);
        }

        public Result<DecodedBlock> Read()
        {
            var block = session.Read();
            if (owner._armed)
            {
                owner._inRead = true;
                owner._released.Wait(TimeSpan.FromSeconds(30));
                owner._inRead = false;
            }

            if (owner._seeked && !block.IsSuccess)
            {
                owner._endedAfterSeek = true;
            }

            return block;
        }

        public void Dispose() => session.Dispose();
    }
}
