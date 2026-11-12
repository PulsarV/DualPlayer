using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>
/// Deterministic decoder whose tracks end at different times (e.g. a commentary track shorter than the film): each
/// output gets a ramp (frame index / 1000) in blocks of 480 frames until its own track's length, round robin across the
/// outputs that still have audio. The file is as long as its longest track; the end of the stream is reached only once
/// every track has ended.
/// </summary>
internal sealed class PerTrackLengthDecoder(IReadOnlyList<AudioTrackInfo> tracks, IReadOnlyDictionary<int, TimeSpan> trackLengths) : IAudioDecoder
{
    public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path) => Result<IReadOnlyList<AudioTrackInfo>>.Success(tracks);

    public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
        Result<IDecodeSession>.Success(new Session(outputs, trackLengths));

    private sealed class Session : IDecodeSession
    {
        private const int BlockFrames = 480;
        private readonly IReadOnlyList<DecodeRequest> _outputs;
        private readonly long[] _length;
        private readonly long[] _position;
        private int _epoch;
        private int _next;

        public Session(IReadOnlyList<DecodeRequest> outputs, IReadOnlyDictionary<int, TimeSpan> trackLengths)
        {
            _outputs = outputs;
            _length = [.. outputs.Select(o => (long)Math.Round(trackLengths[o.TrackNumber].TotalSeconds * o.Target.SampleRate))];
            _position = new long[outputs.Count];
        }

        public TimeSpan? Duration => LongestTrack();

        public Result Seek(TimeSpan position, int epoch)
        {
            for (var i = 0; i < _outputs.Count; i++)
            {
                _position[i] = (long)Math.Round(position.TotalSeconds * _outputs[i].Target.SampleRate);
            }

            _epoch = epoch;
            _next = 0;
            return Result.Success();
        }

        public Result<DecodedBlock> Read()
        {
            for (var tried = 0; tried < _outputs.Count; tried++)
            {
                var index = (_next + tried) % _outputs.Count;
                if (_position[index] >= _length[index])
                {
                    continue;
                }

                var target = _outputs[index].Target;
                var start = _position[index];
                var count = (int)Math.Min(BlockFrames, _length[index] - start);
                var samples = new float[count * target.Channels];
                for (var f = 0; f < count; f++)
                {
                    for (var c = 0; c < target.Channels; c++)
                    {
                        samples[(f * target.Channels) + c] = (start + f) / 1000f;
                    }
                }

                _position[index] = start + count;
                _next = (index + 1) % _outputs.Count;
                return Result<DecodedBlock>.Success(new DecodedBlock(index, _epoch, start, samples));
            }

            return Result<DecodedBlock>.Failure("playback.end_of_stream", "End of file.");
        }

        public void Dispose()
        {
        }

        private TimeSpan LongestTrack() => _outputs
            .Select((o, i) => TimeSpan.FromSeconds((double)_length[i] / o.Target.SampleRate))
            .Max();
    }
}
