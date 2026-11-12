using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Tests.Fakes;

internal sealed class FakeAudioBackend(TimeProvider time, MixFormat format) : IAudioBackend
{
    private readonly List<AudioDeviceInfo> _devices = [];
    private readonly HashSet<string> _denied = [];
    private readonly Dictionary<string, TimeSpan> _startLatency = [];

    private readonly Dictionary<string, MixFormat> _formats = [];
    private readonly Dictionary<string, double> _ppm = [];

    public double PpmOffset { get; set; }

    /// <summary>False: the devices do not keep the audio they render (very long runs).</summary>
    public bool KeepRendered { get; set; } = true;

    /// <summary>The device <paramref name="id"/> runs at <paramref name="format"/> instead of the backend's default.</summary>
    public void SetFormat(string id, MixFormat format) => _formats[id] = format;

    /// <summary>The device <paramref name="id"/>'s clock runs <paramref name="ppm"/> fast instead of the backend's <see cref="PpmOffset"/>.</summary>
    public void SetPpm(string id, double ppm) => _ppm[id] = ppm;

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

        return Result<IAudioDevice>.Success(new FakeAudioDevice(time, _formats.GetValueOrDefault(deviceId, format), requestedBuffer, source, _ppm.GetValueOrDefault(deviceId, PpmOffset))
        {
            KeepRendered = KeepRendered,
            StartLatency = _startLatency.GetValueOrDefault(deviceId),
        });
    }
}
