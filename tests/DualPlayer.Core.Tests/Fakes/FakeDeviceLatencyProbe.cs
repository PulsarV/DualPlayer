using DualPlayer.Core.Sync;

namespace DualPlayer.Core.Tests.Fakes;

public sealed class FakeDeviceLatencyProbe : IDeviceLatencyProbe
{
    private readonly Dictionary<string, DeviceLatencyReport> _reports = [];

    public void Set(string deviceId, DeviceLatencyReport report) => _reports[deviceId] = report;

    public Result<DeviceLatencyReport> Probe(string deviceId) =>
        _reports.TryGetValue(deviceId, out var r)
            ? Result<DeviceLatencyReport>.Success(r)
            : Result<DeviceLatencyReport>.Failure("probe.unknown_device", deviceId);
}
