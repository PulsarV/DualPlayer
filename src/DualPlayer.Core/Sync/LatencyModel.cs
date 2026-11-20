namespace DualPlayer.Core.Sync;

/// <summary>Per-output estimate: Ai = reported + default - offset; lag = PD - Ai.</summary>
public sealed record OutputLatency(
    string DeviceId,
    DeviceType Type,
    int ReportedMs,
    int TypeDefaultMs,
    int OffsetMs,
    int AssumedMs,
    int LagMs)
{
    /// <summary>Where the reported part came from (the probe's source), or why it is missing (a failed probe).</summary>
    public string? Source { get; init; }
}

public sealed record LatencyEstimate(IReadOnlyList<OutputLatency> Outputs, int PictureDelayMs);

public static class LatencyModel
{
    public static int TypeDefaultMs(DeviceType type) => type switch
    {
        DeviceType.BluetoothHeadphones => 200,
        DeviceType.BluetoothSpeaker => 200,
        _ => 0,
    };

    /// <summary>Works out every output's assumed latency, the picture delay (max Ai) and the lags.</summary>
    public static LatencyEstimate Estimate(IReadOnlyList<(string DeviceId, DeviceType Type, int ReportedMs, int OffsetMs)> outputs)
    {
        var assumed = outputs.Select(o => o.ReportedMs + TypeDefaultMs(o.Type) - o.OffsetMs).ToArray();
        var pd = assumed.Length == 0 ? 0 : assumed.Max();
        var result = outputs
            .Select((o, i) => new OutputLatency(o.DeviceId, o.Type, o.ReportedMs, TypeDefaultMs(o.Type), o.OffsetMs, assumed[i], pd - assumed[i]))
            .ToList();
        return new LatencyEstimate(result, pd);
    }
}
