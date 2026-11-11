using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Playback;

/// <summary>One started output: the track it plays, the device it resolved to and the device's mix format.</summary>
public sealed record StartedOutput(int TrackNumber, string DeviceId, string DeviceName, MixFormat Format);

/// <summary>Result of a successful start: every output and the master-clock position at the start.</summary>
public sealed record RunStarted(IReadOnlyList<StartedOutput> Outputs, TimeSpan ClockPosition);

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

    /// <summary>Ends the current run with a summary; does nothing when no run is active.</summary>
    void Stop(RunEndReason reason);
}
