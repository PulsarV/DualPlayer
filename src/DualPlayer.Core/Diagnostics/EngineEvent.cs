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
