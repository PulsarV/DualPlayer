using DualPlayer.Core.Sync;

namespace DualPlayer.Core.Tests.Sync;

/// <summary>T65: the contract's device-type names (cli.md section 3) come from one Core mapper.</summary>
public sealed class DeviceTypeNamesTests
{
    [Theory]
    [InlineData(DeviceType.BluetoothHeadphones, "bluetooth-headphones")]
    [InlineData(DeviceType.BluetoothSpeaker, "bluetooth-speaker")]
    [InlineData(DeviceType.Wired, "wired")]
    [InlineData(DeviceType.Hdmi, "hdmi")]
    [InlineData(DeviceType.Other, "other")]
    public void Each_device_type_has_its_cli_md_name(DeviceType type, string name) =>
        DeviceTypeNames.Of(type).ShouldBe(name);

    [Fact]
    public void Every_enum_value_is_covered_by_the_table_above() =>
        Enum.GetValues<DeviceType>().Select(DeviceTypeNames.Of).Distinct().Count().ShouldBe(Enum.GetValues<DeviceType>().Length);
}
