namespace DualPlayer.Core.Outputs;

/// <summary>Lists and opens the playback devices of the operating system.</summary>
public interface IAudioBackend
{
    IReadOnlyList<AudioDeviceInfo> ListDevices();

    /// <summary>Failure codes: <c>outputs.device_not_found</c>, <c>outputs.device_access_denied</c>.</summary>
    Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source);
}
