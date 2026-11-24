using System.Runtime.Versioning;
using DualPlayer.Core;
using DualPlayer.Core.Sync;
using NAudio.CoreAudioApi;

namespace DualPlayer.Platform.Windows.Audio;

[SupportedOSPlatform("windows")]
public sealed class WasapiLatencyProbe : IDeviceLatencyProbe
{
    private static readonly PropertyKey EnumeratorNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 24);
    private static readonly PropertyKey FormFactorKey = new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);

    public Result<DeviceLatencyReport> Probe(string deviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            var store = device.Properties;
            var enumeratorName = Read(store, EnumeratorNameKey) as string;
            var formFactor = Read(store, FormFactorKey) is uint u ? (int?)u : null;
            var (connection, form) = Map(enumeratorName, formFactor);
            return Result<DeviceLatencyReport>.Success(new DeviceLatencyReport(TimeSpan.Zero, "none", connection, form));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return Result<DeviceLatencyReport>.Failure("outputs.device_not_found", $"Device '{deviceId}' is not available: {ex.Message}");
        }
    }

    /// <summary>Pure mapping from the endpoint's enumerator name and EndpointFormFactor value; never the device name.</summary>
    public static (ConnectionKind Connection, FormFactor FormFactor) Map(string? enumeratorName, int? endpointFormFactor)
    {
        var form = endpointFormFactor switch
        {
            1 => FormFactor.Speaker,
            3 => FormFactor.Headphones,
            5 => FormFactor.Headset,
            9 => FormFactor.Display,
            _ => FormFactor.Unknown,
        };

        var connection =
            enumeratorName is not null && enumeratorName.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ? ConnectionKind.Bluetooth :
            string.Equals(enumeratorName, "USB", StringComparison.OrdinalIgnoreCase) ? ConnectionKind.Usb :
            form == FormFactor.Display ? ConnectionKind.DisplayHdmi :
            ConnectionKind.Unknown;
        return (connection, form);
    }

    private static object? Read(PropertyStore store, PropertyKey key)
    {
        for (var i = 0; i < store.Count; i++)
        {
            var k = store.Get(i);
            if (k.formatId == key.formatId && k.propertyId == key.propertyId)
            {
                return store[k].Value;
            }
        }

        return null;
    }
}
