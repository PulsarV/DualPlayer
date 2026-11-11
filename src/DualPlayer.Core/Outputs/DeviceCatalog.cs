namespace DualPlayer.Core.Outputs;

/// <summary>The connected playback devices sorted by OS endpoint id; list numbers run from 1 in that order.</summary>
public sealed class DeviceCatalog
{
    public DeviceCatalog(IEnumerable<AudioDeviceInfo> devices) =>
        Devices = [.. devices.OrderBy(d => d.Id, StringComparer.Ordinal)];

    public IReadOnlyList<AudioDeviceInfo> Devices { get; }

    /// <summary>Resolves a device by its 1-based list number or full OS id; null when not connected.</summary>
    public AudioDeviceInfo? Find(DeviceRef reference) =>
        reference.Number is { } n
            ? (n >= 1 && n <= Devices.Count ? Devices[n - 1] : null)
            : Devices.FirstOrDefault(d => string.Equals(d.Id, reference.Id, StringComparison.Ordinal));
}
