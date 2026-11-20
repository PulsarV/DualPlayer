using DualPlayer.Core.Settings;

namespace DualPlayer.Core.Tests.Fakes;

internal sealed class DeviceOffsetsBuilder
{
    private readonly Dictionary<string, int> _offsets = [];

    public DeviceOffsetsBuilder With(string deviceId, int offsetMs)
    {
        _offsets[deviceId] = offsetMs;
        return this;
    }

    public DeviceOffsets Build() => new() { Offsets = new Dictionary<string, int>(_offsets) };
}
