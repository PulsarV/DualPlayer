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

    private readonly List<float> _rendered = [];

    /// <summary>First-channel sample of every frame handed to the device so far, in order.</summary>
    public IReadOnlyList<float> Rendered => _rendered;

    /// <summary>Frames handed to the device so far.</summary>
    public long WrittenFrames => _rendered.Count;

    /// <summary>Frames the device has played at the current time.</summary>
    public long PlayedFramesNow =>
        (long)Math.Round(time.GetElapsedTime(_startTimestamp, time.GetTimestamp()).TotalSeconds * format.SampleRate * (1 + (ppm * 1e-6)));

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
        var buffer = new float[frames * format.Channels];
        source.Render(buffer, new DevicePosition(played, now));
        for (var i = 0; i < frames; i++)
        {
            _rendered.Add(buffer[i * format.Channels]);
        }
    }
}
