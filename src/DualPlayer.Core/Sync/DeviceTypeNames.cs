namespace DualPlayer.Core.Sync;

/// <summary>The contract's device-type names (cli.md section 3), the one table every log line uses.</summary>
public static class DeviceTypeNames
{
    public static string Of(DeviceType type) => type switch
    {
        DeviceType.BluetoothHeadphones => "bluetooth-headphones",
        DeviceType.BluetoothSpeaker => "bluetooth-speaker",
        DeviceType.Wired => "wired",
        DeviceType.Hdmi => "hdmi",
        _ => "other",
    };
}
