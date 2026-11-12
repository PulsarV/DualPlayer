using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Playback;

/// <summary>
/// An audio track of a file. <paramref name="Number"/> counts from 1 among audio tracks only.
/// <see cref="Channels"/> and <see cref="HasLfe"/> are the decoder's facts about the source (so layouts that
/// <see cref="ChannelLayout.Other"/> lumps together, such as 2.1 or 6.0, stay distinguishable); when not given they
/// follow <paramref name="ChannelLayout"/>.
/// </summary>
public sealed record AudioTrackInfo(int Number, string Language, ChannelLayout ChannelLayout, int SampleRate)
{
    /// <summary>The number of channels of the source.</summary>
    public int Channels { get; init; } = ChannelLayout switch
    {
        ChannelLayout.Mono => 1,
        ChannelLayout.Stereo => 2,
        ChannelLayout.Surround51 => 6,
        ChannelLayout.Surround71 => 8,
        _ => 0,
    };

    /// <summary>Whether the source has an LFE channel (dropped when folded onto fewer channels).</summary>
    public bool HasLfe { get; init; } = ChannelLayout is ChannelLayout.Surround51 or ChannelLayout.Surround71;
}
