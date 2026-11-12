namespace DualPlayer.Core.Sync;

/// <summary>
/// Near-unity fractional-rate resampler (Catmull-Rom cubic) for drift correction. Real-time safe:
/// <see cref="Process"/> never allocates, locks, logs or throws. Not thread-safe.
/// </summary>
public sealed class AdaptiveResampler
{
    private const int HistoryFrames = 4;
    private const int PrimingFrames = 3;

    private readonly int _channels;
    private readonly float[] _history; // frame-major: [frame][channel], oldest first
    private readonly float[] _frame;
    private int _filled;
    private double _phase;
    private int _pendingSteps;
    private int _drop;
    private int _insert;
    private long _framesRead;
    private double _lastOutputPosition = -1;

    public AdaptiveResampler(int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        _channels = channels;
        _history = new float[channels * HistoryFrames];
        _frame = new float[channels];
    }

    /// <summary>
    /// Input-frame position (since the last <see cref="Reset"/>) of the next output frame, counting pending
    /// drops and inserts.
    /// </summary>
    public double NextOutputPosition => _framesRead + _drop - _filled + Math.Max(0, _pendingSteps - _insert) + _phase;

    /// <summary>Frames still to be inserted (repeated) from a negative <see cref="Realign"/>; they are consumed one per output frame.</summary>
    public int PendingInsertFrames => _insert;

    /// <summary>Input-frame position (since the last <see cref="Reset"/>) of the last output frame produced; -1 when none.</summary>
    public double LastOutputPosition => _lastOutputPosition;

    /// <summary>Positive drops input frames, negative inserts (repeats) frames; applied at the next <see cref="Process"/>.</summary>
    public void Realign(int frames)
    {
        if (frames >= 0)
        {
            _drop += frames;
        }
        else
        {
            _insert += -frames;
        }
    }

    /// <summary>Clears history, phase and pending realignment (new epoch).</summary>
    public void Reset()
    {
        Array.Clear(_history);
        _filled = 0;
        _phase = 0;
        _pendingSteps = 0;
        _drop = 0;
        _insert = 0;
        _framesRead = 0;
        _lastOutputPosition = -1;
    }

    /// <summary>Fills <paramref name="output"/> producing <paramref name="ratio"/> output frames per input frame (ratio above 1 stretches: tone frequency scales by 1/ratio); returns frames produced.</summary>
    public int Process(IFrameReader input, Span<float> output, double ratio) =>
        Process(input, output, ratio, double.PositiveInfinity);

    /// <summary>
    /// As <see cref="Process(IFrameReader, Span{float}, double)"/>, but stops before the first output frame whose
    /// input position reaches <paramref name="positionLimit"/> (see <see cref="NextOutputPosition"/>).
    /// </summary>
    public int Process(IFrameReader input, Span<float> output, double ratio, double positionLimit)
    {
        if (NextOutputPosition >= positionLimit)
        {
            return 0;
        }

        var channels = _channels;
        var wanted = output.Length / channels;

        while (_drop > 0)
        {
            if (input.Read(_frame) == 0)
            {
                return 0;
            }

            _framesRead++;
            _drop--;
        }

        while (_filled < PrimingFrames)
        {
            if (!ReadFrame(input))
            {
                return 0;
            }

            _filled++;
        }

        var produced = 0;
        while (produced < wanted)
        {
            if (NextOutputPosition >= positionLimit)
            {
                return produced;
            }

            while (_pendingSteps > 0)
            {
                if (_insert > 0)
                {
                    _insert--;
                }
                else if (!ReadFrame(input))
                {
                    return produced;
                }

                _pendingSteps--;
            }

            var t = (float)_phase;
            for (var c = 0; c < channels; c++)
            {
                var h0 = _history[c];
                var h1 = _history[channels + c];
                var h2 = _history[(2 * channels) + c];
                var h3 = _history[(3 * channels) + c];
                output[(produced * channels) + c] =
                    h1 + (0.5f * t * (h2 - h0 + (t * ((2 * h0) - (5 * h1) + (4 * h2) - h3 + (t * ((3 * (h1 - h2)) + h3 - h0))))));
            }

            _lastOutputPosition = _framesRead - PrimingFrames + _phase;
            produced++;
            _phase += 1.0 / ratio;
            var steps = (int)_phase;
            _phase -= steps;
            _pendingSteps = steps;
        }

        return produced;
    }

    private bool ReadFrame(IFrameReader input)
    {
        if (input.Read(_frame) == 0)
        {
            return false;
        }

        _framesRead++;
        var channels = _channels;
        Array.Copy(_history, channels, _history, 0, channels * (HistoryFrames - 1));
        _frame.CopyTo(_history.AsSpan(channels * (HistoryFrames - 1)));
        return true;
    }
}
