using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Tests.Fakes;

internal sealed class FakeAudioDevice(TimeProvider time, MixFormat format, TimeSpan buffer, IRenderSource source, double ppm) : IAudioDevice
{
    private long _startTimestamp;
    private bool _started;
    private bool _lost;
    private bool _closed;

    public bool IsStarted => _started;

    public bool IsClosed => _closed;

    public MixFormat MixFormat => format;

    public TimeSpan GrantedBuffer => buffer;

    public event EventHandler? DeviceLost;

    public void Start()
    {
        _startTimestamp = time.GetTimestamp();
        _started = true;
    }

    public void Close() => _closed = true;

    /// <summary>Raises the loss notification once; afterwards the device never renders.</summary>
    public void SimulateLoss()
    {
        if (_lost)
        {
            return;
        }

        _lost = true;
        DeviceLost?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Calls the source once with <paramref name="frames"/> frames and the current device position.</summary>
    public void PumpRender(int frames)
    {
        if (!_started || _lost || _closed)
        {
            return;
        }

        var now = time.GetTimestamp();
        var elapsed = time.GetElapsedTime(_startTimestamp, now).TotalSeconds;
        var played = (long)Math.Round(elapsed * format.SampleRate * (1 + (ppm * 1e-6)));
        source.Render(new float[frames * format.Channels], new DevicePosition(played, now));
    }
}
