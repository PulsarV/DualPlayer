namespace DualPlayer.Core.Diagnostics;

/// <summary>Accumulates per-output run statistics and builds the end-of-run summary.</summary>
public sealed class RunSummaryBuilder
{
    private readonly Dictionary<string, State> _outputs = [];

    public void OutputStarted(string deviceId, TimeSpan clockPosition) =>
        _outputs[deviceId] = new State(clockPosition);

    public void RecordDrift(string deviceId, double driftMs, bool inRecoveryWindow)
    {
        if (!inRecoveryWindow && _outputs.TryGetValue(deviceId, out var s))
        {
            s.MaxDriftMs = Math.Max(s.MaxDriftMs, Math.Abs(driftMs));
        }
    }

    public void RecordClockMismatch(string deviceId, double ppm)
    {
        if (_outputs.TryGetValue(deviceId, out var s))
        {
            s.MismatchPpm = ppm;
        }
    }

    public void RecordCorrection(string deviceId)
    {
        if (_outputs.TryGetValue(deviceId, out var s))
        {
            s.Corrections++;
        }
    }

    public void RecordUnderruns(string deviceId, long cumulative)
    {
        if (_outputs.TryGetValue(deviceId, out var s))
        {
            s.Underruns = cumulative;
        }
    }

    public void RecordDeviceLost(DeviceLost lost)
    {
        if (_outputs.TryGetValue(lost.DeviceId, out var s))
        {
            s.Lost.Add(lost);
            s.EndedAt ??= lost.ClockPosition;
        }
    }

    public RunSummary Build(RunEndReason reason, TimeSpan clockPosition)
    {
        var outputs = _outputs
            .Select(kv => new OutputSummary(
                kv.Key,
                (kv.Value.EndedAt ?? clockPosition) - kv.Value.StartedAt,
                kv.Value.MaxDriftMs,
                kv.Value.MismatchPpm,
                kv.Value.Corrections,
                kv.Value.Underruns,
                kv.Value.Lost.ToArray()))
            .ToArray();
        return new RunSummary(reason, clockPosition, outputs);
    }

    private sealed class State(TimeSpan startedAt)
    {
        public TimeSpan StartedAt { get; } = startedAt;
        public TimeSpan? EndedAt { get; set; }
        public double MaxDriftMs { get; set; }
        public double MismatchPpm { get; set; }
        public int Corrections { get; set; }
        public long Underruns { get; set; }
        public List<DeviceLost> Lost { get; } = [];
    }
}
