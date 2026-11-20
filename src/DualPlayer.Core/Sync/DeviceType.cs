namespace DualPlayer.Core.Sync;

/// <summary>Device type that selects the latency default.</summary>
public enum DeviceType
{
    BluetoothHeadphones,
    BluetoothSpeaker,
    Wired,
    Hdmi,
    Other,
}

/// <summary>How the OS says the device is connected.</summary>
public enum ConnectionKind
{
    Unknown,
    Bluetooth,
    Usb,
    Analog,
    DisplayHdmi,
    DisplayPort,
}

/// <summary>Form factor the OS reports for the device.</summary>
public enum FormFactor
{
    Unknown,
    Headphones,
    Speaker,
    Headset,
    Display,
}

public static class DeviceTypeClassifier
{
    public static DeviceType Classify(ConnectionKind connection, FormFactor formFactor) =>
        connection switch
        {
            ConnectionKind.Bluetooth => formFactor == FormFactor.Speaker ? DeviceType.BluetoothSpeaker : DeviceType.BluetoothHeadphones,
            ConnectionKind.Usb => DeviceType.Wired,
            ConnectionKind.DisplayHdmi or ConnectionKind.DisplayPort => DeviceType.Hdmi,
            _ => DeviceType.Other,
        };
}
