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

    /// <summary>
    /// As <see cref="ListTracks(string)"/>, but stops promptly, closes the file and fails when <paramref name="cancel"/> is
    /// cancelled. A decoder that cannot be interrupted keeps the default, which ignores the token.
    /// </summary>
    Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path, CancellationToken cancel) => ListTracks(path);

    Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs);
}
