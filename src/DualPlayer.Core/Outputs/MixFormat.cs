namespace DualPlayer.Core.Outputs;

/// <summary>Channel layout of an audio stream.</summary>
public enum ChannelLayout
{
    Mono,
    Stereo,
    Surround51,
    Surround71,
    Other,
}

/// <summary>Shared-mode mix format of a device (32-bit float samples, interleaved).</summary>
public readonly record struct MixFormat(int SampleRate, int Channels, ChannelLayout ChannelLayout);
