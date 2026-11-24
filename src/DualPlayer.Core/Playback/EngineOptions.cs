namespace DualPlayer.Core.Playback;

/// <summary>Buffer defaults of the audio engine.</summary>
public sealed record EngineOptions
{
    public TimeSpan RingBuffer { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan StartLevel { get; init; } = TimeSpan.FromSeconds(0.5);

    public TimeSpan DeviceBuffer { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>Per-output start lag behind the master clock, keyed by device id; joins the silent pre-roll (ADR-0005).</summary>
    public IReadOnlyDictionary<string, TimeSpan> InitialLags { get; init; } = new Dictionary<string, TimeSpan>();

    /// <summary>
    /// Film position the run starts at: the decoder seeks there before the pre-roll fills, the master clock starts there and
    /// nothing from before it is ever rendered. Zero (the default) is the start of the film.
    /// </summary>
    public TimeSpan StartPosition { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// The run continues the file's playback after the run that just ended (a return into the film after its end): its run
    /// summary carries that run's counters over, so it covers the playback since the file was opened, and it publishes no
    /// second <see cref="Diagnostics.AudioStarted"/>, as start lines repeat only for a new file.
    /// </summary>
    public bool ContinuesPreviousRun { get; init; }

    /// <summary>Spec target, not a tuning knob: no console flag.</summary>
    internal TimeSpan DriftTolerance { get; init; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Real-time bound of the stop wait, counted in poll sleeps (Core has no real clock): twice the fake-time
    /// stop timeout, so on real hardware the timestamp deadline still ends the wait first.
    /// </summary>
    internal int StopWaitMaxPolls { get; init; } = 2 * 1000;
}
