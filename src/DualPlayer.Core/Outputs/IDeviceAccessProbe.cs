namespace DualPlayer.Core.Outputs;

/// <summary>Opens a device and releases it at once, no playback, to learn before any window opens whether the OS grants access.</summary>
public interface IDeviceAccessProbe
{
    /// <summary>Failure codes: <c>outputs.device_not_found</c>, <c>outputs.device_access_denied</c>.</summary>
    Result CheckAccess(string deviceId);
}
