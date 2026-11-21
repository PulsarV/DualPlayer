namespace DualPlayer.Core.Playback;

/// <summary>How a lag target is reached: ramped at up to 2000 ppm, or set at once (seek, file change, resume).</summary>
public enum LagChangeMode
{
    Ramp,
    AtOnce,
}

/// <summary>An output's position error (how far its heard content is from the master clock, in ms, positive = ahead) and when it was measured.</summary>
/// <param name="ErrorMs">The error of the latest 100 ms reading.</param>
/// <param name="Timestamp">When that reading was taken (the engine's <c>TimeProvider</c> timeline); <see cref="long.MinValue"/> when the output has none yet.</param>
public readonly record struct OutputPositionError(double ErrorMs, long Timestamp);

/// <summary>Lag targets per output for the running engine (ADR-0005); implemented by <see cref="AudioEngine"/>.</summary>
public interface IOutputLagControl
{
    /// <summary>
    /// Sets each listed output's lag target, keyed by device id; lost or unknown outputs are ignored. A ramp given a
    /// <paramref name="moveDuration"/> moves every output over that time, so all arrive together with the picture
    /// (AC-14), never faster than 2000 ppm; without one each output ramps at 2000 ppm.
    /// </summary>
    Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode, TimeSpan moveDuration = default);

    /// <summary>Gets an output's current lag, or null when the output is not in the run.</summary>
    TimeSpan? CurrentLag(string deviceId);

    /// <summary>
    /// Gets the pre-roll-to-frame-0 instant: the timestamp (the engine's <c>TimeProvider</c> timeline) at which the
    /// slowest output hears film frame 0, that is the moment the master clock starts plus the largest start lag.
    /// A negative picture delay starts the picture before it. Null until the pre-roll release has fixed it, and when
    /// no run is active.
    /// </summary>
    long? FrameZeroTimestamp { get; }

    /// <summary>
    /// Gets when the engine started the master clock (its <c>TimeProvider</c> timeline): frame 0 is heard on each output
    /// still connected its lag after this. Null until the pre-roll release has fixed it, and when no run is active.
    /// </summary>
    long? ClockStartTimestamp { get; }

    /// <summary>
    /// Gets when the output first handed film audio to its device (the engine's <c>TimeProvider</c> timeline); null
    /// before that and when the output is not in the run. Heard = this plus the output's assumed latency.
    /// </summary>
    long? FirstAudioTimestamp(string deviceId);

    /// <summary>
    /// Gets the latest position error reading of every output still connected, keyed by device id; empty when no run is
    /// active. Unlike a sync record's one-second average this is the current error, so recovery after a command shows
    /// within a reading or two.
    /// </summary>
    IReadOnlyDictionary<string, OutputPositionError> PositionErrors();

    /// <summary>Gets a value indicating whether any output's lag is still moving toward its target.</summary>
    bool IsRamping { get; }
}
