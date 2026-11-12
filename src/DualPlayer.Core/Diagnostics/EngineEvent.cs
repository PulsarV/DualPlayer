namespace DualPlayer.Core.Diagnostics;

/// <summary>Why a run ended.</summary>
public enum RunEndReason
{
    FileEnd,
    Stop,
    Interrupt,
    SeekPastEnd,
    AllDevicesLost,
}

/// <summary>Closed set of events the engine-control loop publishes.</summary>
public abstract record EngineEvent;

public sealed record SyncRecord(string DeviceId, TimeSpan ClockPosition, double DriftMs, double CorrectionPpm, long Underruns) : EngineEvent;

public sealed record Excursion(string DeviceId, TimeSpan Start, TimeSpan Duration, double PeakMs) : EngineEvent;

/// <summary>A seek target lay before the start or past the end of the file; <paramref name="Position"/> is where playback went instead.</summary>
public sealed record SeekTargetOutsideFile(TimeSpan Target, TimeSpan Position) : EngineEvent;

/// <summary>An output's device was lost. <paramref name="OutputIndex"/> is 0-based (the position in the run's outputs); the console prints it 1-based like the start lines.</summary>
public sealed record DeviceLost(int OutputIndex, string DeviceId, TimeSpan ClockPosition, bool OthersContinue) : EngineEvent;

public sealed record OutputSummary(
    string DeviceId,
    TimeSpan Played,
    double MaxDriftMs,
    double ClockMismatchPpm,
    int Corrections,
    long Underruns,
    IReadOnlyList<DeviceLost> DeviceLostEvents);

public sealed record RunSummary(RunEndReason EndReason, TimeSpan ClockPosition, IReadOnlyList<OutputSummary> Outputs) : EngineEvent;
