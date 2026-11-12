using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>
/// Mono decode session whose outputs get blocks of different sizes (e.g. 1536 and 1024 frames), interleaved
/// one block per output in turn. Every sample equals its own frame index, so content is checkable from the audio.
/// </summary>
internal sealed class MismatchedBlockSession(int[] blockFrames, long totalFramesPerOutput) : IDecodeSession
{
    private readonly long[] _position = new long[blockFrames.Length];
    private int _epoch;
    private int _next;

    public Result Seek(TimeSpan position, int epoch)
    {
        _epoch = epoch;
        return Result.Success();
    }

    public Result<DecodedBlock> Read()
    {
        for (var tries = 0; tries < blockFrames.Length; tries++)
        {
            var index = _next;
            _next = (_next + 1) % blockFrames.Length;
            if (_position[index] >= totalFramesPerOutput)
            {
                continue;
            }

            var start = _position[index];
            var count = (int)Math.Min(blockFrames[index], totalFramesPerOutput - start);
            var samples = new float[count];
            for (var i = 0; i < count; i++)
            {
                samples[i] = start + i;
            }

            _position[index] += count;
            return Result<DecodedBlock>.Success(new DecodedBlock(index, _epoch, start, samples));
        }

        return Result<DecodedBlock>.Failure("playback.end_of_stream", "End of file.");
    }

    public void Dispose()
    {
    }
}
