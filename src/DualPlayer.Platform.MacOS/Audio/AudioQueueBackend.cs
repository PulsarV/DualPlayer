using System.Runtime.Versioning;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;

namespace DualPlayer.Platform.MacOS.Audio;

/// <summary>Dev-only AudioQueue backend; never shipped and not held to sync targets.</summary>
[SupportedOSPlatform("macos")]
internal sealed class AudioQueueBackend(TimeProvider time) : IAudioBackend
{
    public IReadOnlyList<AudioDeviceInfo> ListDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        foreach (var id in CoreAudioInterop.GetDeviceIds())
        {
            if (!CoreAudioInterop.HasOutput(id))
            {
                continue;
            }

            var uid = CoreAudioInterop.GetStringProperty(id, CoreAudioInterop.PropDeviceUid);
            if (uid.Length == 0)
            {
                continue;
            }

            var name = CoreAudioInterop.GetStringProperty(id, CoreAudioInterop.PropName);
            devices.Add(new AudioDeviceInfo(name.Length == 0 ? uid : name, null, uid));
        }

        return devices;
    }

    public Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source)
    {
        foreach (var id in CoreAudioInterop.GetDeviceIds())
        {
            if (CoreAudioInterop.HasOutput(id) && CoreAudioInterop.GetStringProperty(id, CoreAudioInterop.PropDeviceUid) == deviceId)
            {
                return AudioQueueDevice.Create(id, deviceId, requestedBuffer, source, time);
            }
        }

        return Result<IAudioDevice>.Failure("outputs.device_not_found", $"Audio device '{deviceId}' was not found.");
    }
}
