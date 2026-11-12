using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Tests.Fakes;

internal sealed class FakeAudioBackend(TimeProvider time, MixFormat format) : IAudioBackend
{
    private readonly List<AudioDeviceInfo> _devices = [];
    private readonly HashSet<string> _denied = [];
    private readonly Dictionary<string, TimeSpan> _startLatency = [];

    public double PpmOffset { get; set; }

    public void AddDevice(AudioDeviceInfo device) => _devices.Add(device);

    /// <summary>The device <paramref name="id"/> reports position 0 for <paramref name="latency"/> after it starts.</summary>
    public void SetStartLatency(string id, TimeSpan latency) => _startLatency[id] = latency;

    public void DenyAccess(string id) => _denied.Add(id);

    public IReadOnlyList<AudioDeviceInfo> ListDevices() => _devices;

    public Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source)
    {
        var info = _devices.Find(d => d.Id == deviceId);
        if (info is null)
        {
            return Result<IAudioDevice>.Failure("outputs.device_not_found", $"No playback device with id '{deviceId}'.");
        }

        if (_denied.Contains(deviceId))
        {
            return Result<IAudioDevice>.Failure("outputs.device_access_denied", $"The operating system denied access to '{info.Name}'.");
        }

        return Result<IAudioDevice>.Success(new FakeAudioDevice(time, format, requestedBuffer, source, PpmOffset)
        {
            StartLatency = _startLatency.GetValueOrDefault(deviceId),
        });
    }
}
