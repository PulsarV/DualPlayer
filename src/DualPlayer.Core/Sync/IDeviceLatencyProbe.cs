namespace DualPlayer.Core.Sync;

/// <summary>What the OS reports about a device: hidden latency beyond the device position, plus its kind.</summary>
public sealed record DeviceLatencyReport(
    TimeSpan ReportedLatency,
    string Source,
    ConnectionKind Connection,
    FormFactor FormFactor);

public interface IDeviceLatencyProbe
{
    Result<DeviceLatencyReport> Probe(string deviceId);
}
