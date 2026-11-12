using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Playback;

/// <summary>One started output: the track it plays, the device it resolved to and the device's mix format.</summary>
public sealed record StartedOutput(int TrackNumber, string DeviceId, string DeviceName, MixFormat Format)
{
    /// <summary>The source track as the file declares it; null when the engine did not report it.</summary>
    public AudioTrackInfo? Source { get; init; }
}

/// <summary>Result of a successful start: every output and the master-clock position at the start.</summary>
public sealed record RunStarted(IReadOnlyList<StartedOutput> Outputs, TimeSpan ClockPosition);

/// <summary>Outcome of a seek: where playback is now, whether the target was outside the file and whether playback is paused.</summary>
public sealed record SeekResult(TimeSpan Position, bool OutsideFile, bool Paused);

/// <summary>Facade of the audio engine: starts and ends runs and publishes engine events.</summary>
public interface IAudioEngine
{
    /// <summary>Events in the order the engine-control loop produced them.</summary>
    IObservable<EngineEvent> Events { get; }

    /// <summary>
    /// Validates the assignments, opens every device, starts decoding and, once every ring holds its start
    /// level, starts the master clock and the devices together.
    /// </summary>
    Result<RunStarted> StartRun(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options);

    /// <summary>
    /// Silences every output at one master-clock instant beyond the audio already queued and stops the clock;
    /// returns the paused position. Pausing while paused returns the current position.
    /// </summary>
    Result<TimeSpan> Pause();

    /// <summary>Restarts the clock and every output from the paused position once every ring holds its start level.</summary>
    Result<TimeSpan> Resume();

    /// <summary>
    /// Moves every output to <paramref name="target"/> (playing or paused) and replies once no output can still play
    /// audio from the old position. A target before the start plays from the beginning; one past the end ends the run.
    /// </summary>
    Result<SeekResult> Seek(TimeSpan target);

    /// <summary>Ends the current run with a summary; does nothing when no run is active.</summary>
    void Stop(RunEndReason reason);
}
