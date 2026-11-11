using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Playback;

/// <summary>An audio track of a file. <paramref name="Number"/> counts from 1 among audio tracks only.</summary>
public sealed record AudioTrackInfo(int Number, string Language, ChannelLayout ChannelLayout, int SampleRate);
