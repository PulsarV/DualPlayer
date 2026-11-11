using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Playback;

/// <summary>One chosen track decoded to one output's target format.</summary>
public readonly record struct DecodeRequest(int TrackNumber, MixFormat Target);

/// <summary>Interleaved float frames for one output, tagged with the seek epoch they belong to.</summary>
public sealed record DecodedBlock(int OutputIndex, int Epoch, long StartFrame, float[] Samples);

public interface IDecodeSession : IDisposable
{
    /// <summary>Repositions all outputs; subsequent blocks carry <paramref name="epoch"/>.</summary>
    Result Seek(TimeSpan position, int epoch);

    /// <summary>Next block, or failure code <c>playback.end_of_stream</c> at end of file.</summary>
    Result<DecodedBlock> Read();

    /// <summary>Length of the file, when the container reports it; null when unknown.</summary>
    TimeSpan? Duration => null;
}

public interface IAudioDecoder
{
    Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path);

    Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs);
}
