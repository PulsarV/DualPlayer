using System.Runtime.Versioning;
using DualPlayer.Core;
using DualPlayer.Core.Sync;

namespace DualPlayer.Platform.MacOS.Audio;

[SupportedOSPlatform("macos")]
public sealed class CoreAudioLatencyProbe : IDeviceLatencyProbe
{
    private const uint PropTransportType = 0x7472616E; // 'tran' (kAudioDevicePropertyTransportType)
    private const uint PropDeviceLatency = 0x6C746E63; // 'ltnc' (kAudioDevicePropertyLatency; the stream property shares the code)
    private const uint PropSafetyOffset = 0x73616674; // 'saft' (kAudioDevicePropertySafetyOffset)

    public Result<DeviceLatencyReport> Probe(string deviceId)
    {
        var id = FindOutputDevice(deviceId);
        if (id is null)
        {
            return Result<DeviceLatencyReport>.Failure("outputs.device_not_found", $"Device '{deviceId}' is not available.");
        }

        var (connection, form) = MapTransport(ReadUInt(id.Value, PropTransportType, CoreAudioInterop.ScopeGlobal));
        var frames = ReadUInt(id.Value, PropDeviceLatency, CoreAudioInterop.ScopeOutput)
            + ReadUInt(id.Value, PropSafetyOffset, CoreAudioInterop.ScopeOutput)
            + ReadStreamLatency(id.Value);
        var rate = CoreAudioInterop.GetNominalRate(id.Value);
        var latency = frames > 0 && rate > 0 ? TimeSpan.FromSeconds(frames / rate) : TimeSpan.Zero;
        return Result<DeviceLatencyReport>.Success(new DeviceLatencyReport(latency, latency > TimeSpan.Zero ? "coreaudio" : "none", connection, form));
    }

    /// <summary>Pure mapping from the Core Audio transport type four-char code.</summary>
    public static (ConnectionKind Connection, FormFactor FormFactor) MapTransport(uint transportType)
    {
        var connection = transportType switch
        {
            0x626C7565 or 0x626C6561 => ConnectionKind.Bluetooth, // 'blue', 'blea'
            0x75736220 => ConnectionKind.Usb, // 'usb '
            0x68646D69 => ConnectionKind.DisplayHdmi, // 'hdmi'
            0x64707274 => ConnectionKind.DisplayPort, // 'dprt'
            _ => ConnectionKind.Unknown,
        };
        return (connection, FormFactor.Unknown);
    }

    private static uint? FindOutputDevice(string uid)
    {
        foreach (var id in CoreAudioInterop.GetDeviceIds())
        {
            if (CoreAudioInterop.HasOutput(id) && CoreAudioInterop.GetStringProperty(id, CoreAudioInterop.PropDeviceUid) == uid)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>Reads a UInt32 property; a missing property reads as 0 (unknown / nothing reported).</summary>
    private static unsafe uint ReadUInt(uint objectId, uint selector, uint scope)
    {
        uint value = 0;
        var size = (uint)sizeof(uint);
        return CoreAudioInterop.AudioObjectGetPropertyData(objectId, CoreAudioInterop.Address(selector, scope), 0, null, ref size, &value) == 0 ? value : 0;
    }

    private static unsafe uint ReadStreamLatency(uint deviceId)
    {
        var address = CoreAudioInterop.Address(CoreAudioInterop.PropStreams, CoreAudioInterop.ScopeOutput);
        if (CoreAudioInterop.AudioObjectGetPropertyDataSize(deviceId, address, 0, null, out var size) != 0 || size < sizeof(uint))
        {
            return 0;
        }

        var streams = new uint[size / sizeof(uint)];
        fixed (uint* p = streams)
        {
            return CoreAudioInterop.AudioObjectGetPropertyData(deviceId, address, 0, null, ref size, p) == 0
                ? ReadUInt(streams[0], PropDeviceLatency, CoreAudioInterop.ScopeGlobal)
                : 0;
        }
    }
}
