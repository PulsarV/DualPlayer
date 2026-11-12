using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Tests.Fakes;

internal sealed class FakeAudioDevice(TimeProvider time, MixFormat format, TimeSpan buffer, IRenderSource source, double ppm) : IAudioDevice
{
    private long _startTimestamp;
    private bool _started;
    private bool _lost;
    private bool _closed;
    private long? _frozenPlayed;
    private long _written;
    private long _positionStep;
    private float[] _scratch = [];

    public bool IsStarted => _started;

    public bool IsClosed => _closed;

    /// <summary>True after <see cref="SimulateLoss"/>: the device no longer renders.</summary>
    public bool IsLost => _lost;

    public MixFormat MixFormat => format;

    public TimeSpan GrantedBuffer => buffer;

    private readonly List<float> _rendered = [];

    /// <summary>First-channel sample of every frame handed to the device so far, in order.</summary>
    public IReadOnlyList<float> Rendered => _rendered;

    /// <summary>Frames handed to the device so far.</summary>
    public long WrittenFrames => _written;

    /// <summary>When false the rendered samples are not kept (<see cref="Rendered"/> stays empty); for runs too long to store.</summary>
    public bool KeepRendered { get; set; } = true;

    /// <summary>From now on the device reports <paramref name="frames"/> more played frames (a step error in its position).</summary>
    public void StepPosition(long frames) => _positionStep += frames;

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
        var length = frames * format.Channels;
        var buffer = KeepRendered ? new float[length] : (_scratch.Length == length ? _scratch : _scratch = new float[length]);
        source.Render(buffer, new DevicePosition(played, now));
        _written += frames;
        if (KeepRendered)
        {
            for (var i = 0; i < frames; i++)
            {
                _rendered.Add(buffer[i * format.Channels]);
            }
        }
    }

    /// <summary>Frames the device has played when the fake clock read <paramref name="timestamp"/>.</summary>
    public long PlayedAt(long timestamp)
    {
        var elapsed = time.GetElapsedTime(_startTimestamp, timestamp) - StartLatency;
        return elapsed <= TimeSpan.Zero ? 0 : (long)Math.Round(elapsed.TotalSeconds * format.SampleRate * (1 + (ppm * 1e-6))) + _positionStep;
    }
}
