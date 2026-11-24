using DualPlayer.Core.Sync;

namespace DualPlayer.Core.Tests.Fakes;

public sealed class FakeDeviceLatencyProbe : IDeviceLatencyProbe
{
    private readonly Dictionary<string, DeviceLatencyReport> _reports = [];

    public void Set(string deviceId, DeviceLatencyReport report) => _reports[deviceId] = report;

    /// <summary>When set, runs at the start of every <see cref="Probe"/> (a test can raise an event in the window before the picture loads).</summary>
    public Action? BeforeProbe { get; set; }

    public Result<DeviceLatencyReport> Probe(string deviceId)
    {
        BeforeProbe?.Invoke();
        return _reports.TryGetValue(deviceId, out var r)
            ? Result<DeviceLatencyReport>.Success(r)
            : Result<DeviceLatencyReport>.Failure("probe.unknown_device", deviceId);
    }
}
