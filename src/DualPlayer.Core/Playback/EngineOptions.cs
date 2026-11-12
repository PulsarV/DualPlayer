namespace DualPlayer.Core.Playback;

/// <summary>Buffer defaults of the audio engine.</summary>
public sealed record EngineOptions
{
    public TimeSpan RingBuffer { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan StartLevel { get; init; } = TimeSpan.FromSeconds(0.5);

    public TimeSpan DeviceBuffer { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>Spec target, not a tuning knob: no console flag.</summary>
    internal TimeSpan DriftTolerance { get; init; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Real-time bound of the stop wait, counted in poll sleeps (Core has no real clock): twice the fake-time
    /// stop timeout, so on real hardware the timestamp deadline still ends the wait first.
    /// </summary>
    internal int StopWaitMaxPolls { get; init; } = 2 * 1000;
}
