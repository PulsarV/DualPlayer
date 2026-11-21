namespace DualPlayer.Core.Outputs;

/// <summary>Checks access by opening the device through the audio backend and closing it again, with nothing rendered.</summary>
public sealed class BackendDeviceAccessProbe(IAudioBackend backend) : IDeviceAccessProbe
{
    private static readonly TimeSpan Buffer = TimeSpan.FromMilliseconds(100);

    public Result CheckAccess(string deviceId)
    {
        var opened = backend.Open(deviceId, Buffer, new SilentSource());
        if (!opened.IsSuccess)
        {
            return Result.Failure(opened.Error);
        }

        opened.Value.Close();
        return Result.Success();
    }

    private sealed class SilentSource : IRenderSource
    {
        public void Render(Span<float> buffer, DevicePosition position) => buffer.Clear();
    }
}
