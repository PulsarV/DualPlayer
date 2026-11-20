namespace DualPlayer.Core.Playback;

/// <summary>How a lag target is reached: ramped at up to 2000 ppm, or set at once (seek, file change, resume).</summary>
public enum LagChangeMode
{
    Ramp,
    AtOnce,
}

/// <summary>Lag targets per output for the running engine (ADR-0005); implemented by <see cref="AudioEngine"/>.</summary>
public interface IOutputLagControl
{
    /// <summary>Sets each listed output's lag target, keyed by device id; lost or unknown outputs are ignored.</summary>
    Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode);

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

    /// <summary>Gets a value indicating whether any output's lag is still moving toward its target.</summary>
    bool IsRamping { get; }
}
