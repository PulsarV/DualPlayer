namespace DualPlayer.Core.Sync;

/// <summary>Source of interleaved float frames that the adaptive resampler pulls from (the ring implements it).</summary>
public interface IFrameReader
{
    /// <summary>Gets the number of interleaved channels per frame.</summary>
    int Channels { get; }

    /// <summary>Reads up to <c>destination.Length / Channels</c> whole frames; returns the number of frames read.</summary>
    int Read(Span<float> destination);
}
