using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>
/// Decoder fake that behaves like a demuxer plus resampler instead of a lockstep generator: every output has its own
/// block size (e.g. 1536 and 1024 frames) and its own sample rate and blocks come in content-time order across the
/// outputs. A seek can land early (like a keyframe: the decoder starts before the target, on its own block grid, and
/// discards up to the target, so the first block is a short one starting exactly there) or late (the first block starts
/// after the target and its start frame says so).
/// Frame <c>n</c> of an output carries the value <c>n / 1000</c> on every channel.
/// </summary>
internal sealed class RealisticAudioDecoder(
    IReadOnlyList<AudioTrackInfo> tracks,
    double totalSeconds,
    int[] blockFrames,
    TimeSpan[]? seekLandsEarly = null,
    TimeSpan[]? seekLandsLate = null) : IAudioDecoder
{
    private Session? _session;

    /// <summary>Length of the fake file in seconds.</summary>
    public double TotalSeconds => totalSeconds;

    /// <summary>Content seconds the decoder has produced so far for output <paramref name="index"/> in the current epoch.</summary>
    public double DecodedSeconds(int index) => _session?.DecodedSeconds(index) ?? 0;

    public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path) => Result<IReadOnlyList<AudioTrackInfo>>.Success(tracks);

    public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs)
    {
        var session = new Session(outputs, totalSeconds, blockFrames, seekLandsEarly ?? [], seekLandsLate ?? []);
        _session = session;
        return Result<IDecodeSession>.Success(session);
    }

    private sealed class Session : IDecodeSession
    {
        private readonly IReadOnlyList<DecodeRequest> _outputs;
        private readonly double _total;
        private readonly int[] _blockFrames;
        private readonly TimeSpan[] _early;
        private readonly TimeSpan[] _late;
        private readonly long[] _position;
        private readonly long[] _target;
        private int _epoch;

        public Session(IReadOnlyList<DecodeRequest> outputs, double total, int[] blockFrames, TimeSpan[] early, TimeSpan[] late)
        {
            _outputs = outputs;
            _total = total;
            _blockFrames = blockFrames;
            _early = early;
            _late = late;
            _position = new long[outputs.Count];
            _target = new long[outputs.Count];
        }

        public TimeSpan? Duration => TimeSpan.FromSeconds(_total);

        public double DecodedSeconds(int index) => (double)Volatile.Read(ref _position[index]) / _outputs[index].Target.SampleRate;

        public Result Seek(TimeSpan position, int epoch)
        {
            for (var i = 0; i < _outputs.Count; i++)
            {
                var rate = _outputs[i].Target.SampleRate;
                var early = i < _early.Length ? _early[i] : TimeSpan.Zero;
                var late = i < _late.Length ? _late[i] : TimeSpan.Zero;
                _target[i] = (long)Math.Round(position.TotalSeconds * rate);
                var start = (long)Math.Round(Math.Max(0, (position - early + late).TotalSeconds) * rate);
                Volatile.Write(ref _position[i], start);
            }

            _epoch = epoch;
            return Result.Success();
        }

        public Result<DecodedBlock> Read()
        {
            var next = -1;
            var earliest = double.MaxValue;
            for (var i = 0; i < _outputs.Count; i++)
            {
                var rate = _outputs[i].Target.SampleRate;
                if (_position[i] >= (long)(_total * rate))
                {
                    continue;
                }

                var at = (double)_position[i] / rate;
                if (at < earliest)
                {
                    earliest = at;
                    next = i;
                }
            }

            if (next < 0)
            {
                return Result<DecodedBlock>.Failure("playback.end_of_stream", "End of file.");
            }

            var target = _outputs[next].Target;
            var gridStart = _position[next];
            var count = (int)Math.Min(_blockFrames[next % _blockFrames.Length], (long)(_total * target.SampleRate) - gridStart);
            Volatile.Write(ref _position[next], gridStart + count);
            if (gridStart + count <= _target[next])
            {
                return Read(); // entirely before the seek target: decoded and discarded
            }

            var start = Math.Max(gridStart, _target[next]);
            count -= (int)(start - gridStart);
            var samples = new float[count * target.Channels];
            for (var f = 0; f < count; f++)
            {
                var value = (start + f) / 1000f;
                for (var c = 0; c < target.Channels; c++)
                {
                    samples[(f * target.Channels) + c] = value;
                }
            }

            return Result<DecodedBlock>.Success(new DecodedBlock(next, _epoch, start, samples));
        }

        public void Dispose()
        {
        }
    }
}
