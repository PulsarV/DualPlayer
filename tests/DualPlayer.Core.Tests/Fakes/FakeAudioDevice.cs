using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Tests.Fakes;

internal sealed class FakeAudioDevice(TimeProvider time, MixFormat format, TimeSpan buffer, IRenderSource source, double ppm) : IAudioDevice
{
    private long _startTimestamp;
    private bool _started;
    private bool _lost;
    private bool _closed;
    private long? _frozenPlayed;

    public bool IsStarted => _started;

    public bool IsClosed => _closed;

    public MixFormat MixFormat => format;

    public TimeSpan GrantedBuffer => buffer;

    private readonly List<float> _rendered = [];

    /// <summary>First-channel sample of every frame handed to the device so far, in order.</summary>
    public IReadOnlyList<float> Rendered => _rendered;

    /// <summary>Frames handed to the device so far.</summary>
    public long WrittenFrames => _rendered.Count;

    /// <summary>The device reports position 0 (plays nothing) for this long after <see cref="Start"/>.</summary>
    public TimeSpan StartLatency { get; set; }

    /// <summary>True after <see cref="Hang"/>: the device no longer renders and its position is frozen.</summary>
    public bool IsHung => _frozenPlayed is not null;

    /// <summary>Frames the device has played at the current time.</summary>
    public long PlayedFramesNow => _frozenPlayed ?? PlayedAt(time.GetTimestamp());

    /// <summary>
    /// The device stops rendering and its reported position freezes, without raising <see cref="DeviceLost"/>
    /// (a stuck driver, not a removed device).
    /// </summary>
    public void Hang() => _frozenPlayed ??= PlayedFramesNow;

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
        if (!_started || _lost || _closed || IsHung)
        {
            return;
        }

        var now = time.GetTimestamp();
        var played = PlayedAt(now);
        var buffer = new float[frames * format.Channels];
        source.Render(buffer, new DevicePosition(played, now));
        for (var i = 0; i < frames; i++)
        {
            _rendered.Add(buffer[i * format.Channels]);
        }
    }

    private long PlayedAt(long timestamp)
    {
        var elapsed = time.GetElapsedTime(_startTimestamp, timestamp) - StartLatency;
        return elapsed <= TimeSpan.Zero ? 0 : (long)Math.Round(elapsed.TotalSeconds * format.SampleRate * (1 + (ppm * 1e-6)));
    }
}
