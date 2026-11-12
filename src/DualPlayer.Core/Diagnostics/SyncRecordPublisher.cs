using Microsoft.Extensions.Logging;

namespace DualPlayer.Core.Diagnostics;

/// <summary>One output's counters snapshotted by the control loop for the current second.</summary>
public readonly record struct OutputSample(string DeviceId, TimeSpan Drift, double CorrectionPpm, long Underruns, bool InRecoveryWindow);

/// <summary>Once per second turns output snapshots into sync records and excursions, raises them as events and logs them.</summary>
public sealed partial class SyncRecordPublisher(Action<EngineEvent> publish, ILogger logger)
{
    private readonly Dictionary<string, ExcursionTracker> _trackers = new(StringComparer.Ordinal);

    public void Publish(TimeSpan clockPosition, IReadOnlyList<OutputSample> samples)
    {
        foreach (var sample in samples)
        {
            var driftMs = sample.Drift.TotalMilliseconds;
            var record = new SyncRecord(sample.DeviceId, clockPosition, driftMs, sample.CorrectionPpm, sample.Underruns);
            publish(record);
            LogRecord(sample.DeviceId, clockPosition, driftMs, sample.CorrectionPpm, sample.Underruns);

            if (!_trackers.TryGetValue(sample.DeviceId, out var tracker))
            {
                _trackers[sample.DeviceId] = tracker = new ExcursionTracker(sample.DeviceId);
            }

            Emit(tracker.Observe(clockPosition, driftMs, sample.InRecoveryWindow));
        }
    }

    public void OutputLost(string deviceId, TimeSpan clockPosition)
    {
        if (_trackers.Remove(deviceId, out var tracker))
        {
            Emit(tracker.Finish(clockPosition));
        }
    }

    /// <summary>A seek is starting: close every open excursion at its pre-seek end, before the clock jumps.</summary>
    public void SeekStarting(TimeSpan clockPosition)
    {
        foreach (var tracker in _trackers.Values)
        {
            Emit(tracker.Finish(clockPosition));
        }
    }

    public void Finish(TimeSpan clockPosition)
    {
        foreach (var tracker in _trackers.Values)
        {
            Emit(tracker.Finish(clockPosition));
        }

        _trackers.Clear();
    }

    private void Emit(Excursion? excursion)
    {
        if (excursion is null)
        {
            return;
        }

        publish(excursion);
        LogExcursion(excursion.DeviceId, excursion.Start, excursion.Duration.TotalMilliseconds, excursion.PeakMs);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "sync {DeviceId} clock={ClockPosition} drift={DriftMs}ms correction={CorrectionPpm}ppm underruns={Underruns}")]
    private partial void LogRecord(string deviceId, TimeSpan clockPosition, double driftMs, double correctionPpm, long underruns);

    [LoggerMessage(Level = LogLevel.Warning, Message = "excursion {DeviceId} start={Start} duration={DurationMs}ms peak={PeakMs}ms")]
    private partial void LogExcursion(string deviceId, TimeSpan start, double durationMs, double peakMs);
}
