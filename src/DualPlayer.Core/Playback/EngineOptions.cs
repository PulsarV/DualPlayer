namespace DualPlayer.Core.Playback;

/// <summary>Buffer defaults of the audio engine.</summary>
public sealed record EngineOptions
{
    public TimeSpan RingBuffer { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan StartLevel { get; init; } = TimeSpan.FromSeconds(0.5);

    public TimeSpan DeviceBuffer { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>Spec target, not a tuning knob: no console flag.</summary>
    internal TimeSpan DriftTolerance { get; init; } = TimeSpan.FromMilliseconds(10);
}
