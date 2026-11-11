using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>Deterministic decoder: every output gets a ramp (frame index / 1000) in blocks of 480 frames.</summary>
internal sealed class FakeAudioDecoder(IReadOnlyList<AudioTrackInfo> tracks, long totalFrames = 48_000) : IAudioDecoder
{
    public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path) => Result<IReadOnlyList<AudioTrackInfo>>.Success(tracks);

    public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs)
    {
        foreach (var o in outputs)
        {
            if (!tracks.Any(t => t.Number == o.TrackNumber))
            {
                return Result<IDecodeSession>.Failure("playback.track_not_found", $"Audio track {o.TrackNumber} does not exist.");
            }
        }

        return Result<IDecodeSession>.Success(new Session(outputs, totalFrames));
    }

    private sealed class Session(IReadOnlyList<DecodeRequest> outputs, long totalFrames) : IDecodeSession
    {
        private const int BlockFrames = 480;
        private long _position;
        private int _epoch;
        private int _next;

        public TimeSpan? Duration => TimeSpan.FromSeconds((double)totalFrames / outputs[0].Target.SampleRate);

        public Result Seek(TimeSpan position, int epoch)
        {
            _position = (long)(position.TotalSeconds * outputs[0].Target.SampleRate);
            _epoch = epoch;
            _next = 0;
            return Result.Success();
        }

        public Result<DecodedBlock> Read()
        {
            if (_position >= totalFrames)
            {
                return Result<DecodedBlock>.Failure("playback.end_of_stream", "End of file.");
            }

            var target = outputs[_next].Target;
            var samples = new float[BlockFrames * target.Channels];
            for (var i = 0; i < BlockFrames; i++)
            {
                for (var c = 0; c < target.Channels; c++)
                {
                    samples[(i * target.Channels) + c] = (_position + i) / 1000f;
                }
            }

            var block = new DecodedBlock(_next, _epoch, _position, samples);
            _next++;
            if (_next == outputs.Count)
            {
                _next = 0;
                _position += BlockFrames;
            }

            return Result<DecodedBlock>.Success(block);
        }

        public void Dispose()
        {
        }
    }
}
