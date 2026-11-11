using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;

namespace DualPlayer.Platform.Windows.Audio;

/// <summary>Lists active render endpoints and opens them in shared mode.</summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioBackend(ILogger<WasapiAudioBackend>? logger = null) : IAudioBackend
{
    private static readonly PropertyKey FormFactorKey = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

    private readonly ILogger _logger = logger ?? NullLogger<WasapiAudioBackend>.Instance;

    public IReadOnlyList<AudioDeviceInfo> ListDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    result.Add(new AudioDeviceInfo(device.FriendlyName, ReadType(device), device.ID));
                }
                catch (Exception ex) when (WasapiErrorMapper.IsDeviceFailure(ex))
                {
                    // The endpoint went away mid-enumeration; it is no longer offered, so skip it.
                    _logger.LogDebug(ex, "Skipped a render endpoint that failed during enumeration");
                }
            }
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return result;
    }

    public Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        try
        {
            device = enumerator.GetDevice(deviceId);
        }
        catch (Exception ex) when (WasapiErrorMapper.IsDeviceFailure(ex))
        {
            return Result<IAudioDevice>.Failure(WasapiErrorMapper.Map(ex, deviceId, "outputs.device_not_found"));
        }

        var name = deviceId;
        try
        {
            name = device.FriendlyName;
            var opened = WasapiAudioDevice.Create(device, requestedBuffer, _logger);
            if (!opened.IsSuccess)
            {
                device.Dispose();
            }

            return opened;
        }
        catch (Exception ex) when (WasapiErrorMapper.IsDeviceFailure(ex))
        {
            device.Dispose();
            return Result<IAudioDevice>.Failure(WasapiErrorMapper.Map(ex, name, "outputs.device_open_failed"));
        }
    }

    private static string? ReadType(MMDevice device)
    {
        try
        {
            if (!device.Properties.Contains(FormFactorKey))
            {
                return null;
            }

            return device.Properties[FormFactorKey].Value is uint form ? FormFactorName(form) : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? FormFactorName(uint form) => form switch
    {
        0 => "remote network device",
        1 => "speakers",
        2 => "line level",
        3 => "headphones",
        4 => "microphone",
        5 => "headset",
        6 => "handset",
        7 => "digital passthrough",
        8 => "spdif",
        9 => "hdmi",
        _ => null,
    };
}
