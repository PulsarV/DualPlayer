using DualPlayer.Core;
using DualPlayer.Core.Playback;
using FFmpeg.AutoGen;

namespace DualPlayer.Media.Audio;

/// <summary>
/// One demux pass over a file, decoding every chosen track to interleaved float blocks (one output per request).
/// Each output converts to its device mix format through a MixFormatConverter (swresample + fold-down matrix).
/// </summary>
internal sealed unsafe class FfmpegDecodeSession : IDecodeSession
{
    private const string EndOfStream = "playback.end_of_stream";

    private readonly List<Output> _outputs = [];
    private readonly Queue<DecodedBlock> _pending = new();
    private AVFormatContext* _fmt;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private int _epoch;
    private bool _drained;
    private bool _disposed;

    private FfmpegDecodeSession()
    {
    }

    public static Result<IDecodeSession> Create(string path, IReadOnlyList<DecodeRequest> requests)
    {
        var session = new FfmpegDecodeSession();
        var opened = session.Init(path, requests);
        if (!opened.IsSuccess)
        {
            session.Dispose();
            return Result<IDecodeSession>.Failure(opened.Error);
        }

        return Result<IDecodeSession>.Success(session);
    }

    public TimeSpan? Duration =>
        !_disposed && _fmt->duration > 0 && _fmt->duration != ffmpeg.AV_NOPTS_VALUE
            ? TimeSpan.FromSeconds((double)_fmt->duration / ffmpeg.AV_TIME_BASE)
            : null;

    public Result Seek(TimeSpan position, int epoch)
    {
        if (_disposed)
        {
            return Result.Failure(FfmpegAudioDecoder.Unreadable, "The decode session is closed.");
        }

        var target = (long)(position.TotalSeconds * ffmpeg.AV_TIME_BASE);
        var rc = ffmpeg.av_seek_frame(_fmt, -1, target, ffmpeg.AVSEEK_FLAG_BACKWARD);
        _pending.Clear();
        _epoch = epoch;
        var pastEnd = _fmt->duration > 0 && _fmt->duration != ffmpeg.AV_NOPTS_VALUE && target >= _fmt->duration;
        if (rc < 0 || pastEnd)
        {
            // Seeking beyond the end of a file: nothing is left to play (the engine decides what that means).
            _drained = true;
            return Result.Success();
        }

        _drained = false;
        foreach (var o in _outputs)
        {
            ffmpeg.avcodec_flush_buffers(o.Codec);
            o.ResetResampler(position);
        }

        return Result.Success();
    }

    public Result<DecodedBlock> Read()
    {
        if (_disposed)
        {
            return Result<DecodedBlock>.Failure(EndOfStream, "The decode session is closed.");
        }

        while (_pending.Count == 0)
        {
            if (_drained)
            {
                return Result<DecodedBlock>.Failure(EndOfStream, "End of file.");
            }

            var step = Pump();
            if (!step.IsSuccess)
            {
                return Result<DecodedBlock>.Failure(step.Error);
            }
        }

        return Result<DecodedBlock>.Success(_pending.Dequeue());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var o in _outputs)
        {
            o.Dispose();
        }

        _outputs.Clear();
        var frame = _frame;
        ffmpeg.av_frame_free(&frame);
        var packet = _packet;
        ffmpeg.av_packet_free(&packet);
        var fmt = _fmt;
        ffmpeg.avformat_close_input(&fmt);
        _frame = null;
        _packet = null;
        _fmt = null;
    }

    private Result Init(string path, IReadOnlyList<DecodeRequest> requests)
    {
        var fmt = _fmt;
        var open = FfmpegAudioDecoder.OpenInput(path, &fmt);
        _fmt = fmt;
        if (!open.IsSuccess)
        {
            return open;
        }

        var streams = FfmpegAudioDecoder.AudioStreams(_fmt);
        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
        for (var i = 0; i < requests.Count; i++)
        {
            var number = requests[i].TrackNumber;
            if (number < 1 || number > streams.Count)
            {
                return Result.Failure(
                    "playback.track_invalid",
                    $"Track {number} does not exist; the file has {streams.Count} audio track(s).");
            }

            var created = Output.Create(i, (AVStream*)streams[number - 1], requests[i].Target);
            if (!created.IsSuccess)
            {
                return Result.Failure(created.Error);
            }

            _outputs.Add(created.Value);
        }

        return Result.Success();
    }

    // Reads one packet (or drains at end of file) and queues whatever blocks it yields.
    private Result Pump()
    {
        var rc = ffmpeg.av_read_frame(_fmt, _packet);
        if (rc == ffmpeg.AVERROR_EOF)
        {
            foreach (var o in _outputs)
            {
                Decode(o, null);
                Enqueue(o, flush: true);
            }

            _drained = true;
            return Result.Success();
        }

        if (rc < 0)
        {
            return Result.Failure(FfmpegAudioDecoder.Unreadable, $"The file could not be read: {FfmpegAudioDecoder.ErrorText(rc)}.");
        }

        try
        {
            foreach (var o in _outputs)
            {
                if (o.StreamIndex == _packet->stream_index)
                {
                    Decode(o, _packet);
                }
            }
        }
        finally
        {
            ffmpeg.av_packet_unref(_packet);
        }

        return Result.Success();
    }

    private void Decode(Output o, AVPacket* packet)
    {
        if (ffmpeg.avcodec_send_packet(o.Codec, packet) < 0)
        {
            return; // a corrupt packet is skipped; the next one may be fine
        }

        while (ffmpeg.avcodec_receive_frame(o.Codec, _frame) >= 0)
        {
            o.Convert(_frame);
            ffmpeg.av_frame_unref(_frame);
            Enqueue(o, flush: false);
        }
    }

    private void Enqueue(Output o, bool flush)
    {
        if (flush)
        {
            o.Convert(null);
        }

        if (o.TakeBlock(_epoch) is { } block)
        {
            _pending.Enqueue(block);
        }
    }

    private sealed class Output : IDisposable
    {
        private readonly int _index;
        private readonly int _rate;
        private readonly int _channels;
        private readonly AVChannelLayout _outLayout;
        private MixFormatConverter? _converter;
        private float[] _buffer = [];
        private int _buffered;
        private long _startFrame;

        private Output(int index, AVStream* stream, AVCodecContext* codec, MixFormatInfo target, AVChannelLayout outLayout)
        {
            _index = index;
            StreamIndex = stream->index;
            Codec = codec;
            _rate = target.Rate;
            _channels = target.Channels;
            _outLayout = outLayout;
        }

        public int StreamIndex { get; }

        public AVCodecContext* Codec { get; private set; }

        public static Result<Output> Create(int index, AVStream* stream, Core.Outputs.MixFormat target)
        {
            var decoder = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
            if (decoder is null)
            {
                return Result<Output>.Failure(FfmpegAudioDecoder.Unreadable, "The audio track uses a codec this player cannot decode.");
            }

            var codec = ffmpeg.avcodec_alloc_context3(decoder);
            var rc = ffmpeg.avcodec_parameters_to_context(codec, stream->codecpar);
            if (rc >= 0)
            {
                rc = ffmpeg.avcodec_open2(codec, decoder, null);
            }

            if (rc < 0)
            {
                ffmpeg.avcodec_free_context(&codec);
                return Result<Output>.Failure(
                    FfmpegAudioDecoder.Unreadable,
                    $"The audio track cannot be decoded: {FfmpegAudioDecoder.ErrorText(rc)}.");
            }

            AVChannelLayout layout = default;
            ffmpeg.av_channel_layout_default(&layout, target.Channels);
            var output = new Output(index, stream, codec, new MixFormatInfo(target.SampleRate, target.Channels), layout);
            return Result<Output>.Success(output);
        }

        public void ResetResampler(TimeSpan position)
        {
            _converter?.Dispose();
            _converter = null;
            _buffered = 0;
            _startFrame = (long)(position.TotalSeconds * _rate);
        }

        public void Convert(AVFrame* frame)
        {
            if (_converter is null)
            {
                if (frame is null)
                {
                    return;
                }

                var outLayout = _outLayout;
                _converter = MixFormatConverter.Create(frame, _rate, &outLayout);
                if (_converter is null)
                {
                    return;
                }
            }

            var capacity = _converter.OutputCapacity(frame is null ? 0 : frame->nb_samples) + (frame is null ? 4096 : 0);
            if (capacity <= 0)
            {
                return;
            }

            EnsureCapacity((_buffered + capacity) * _channels);
            fixed (float* dst = &_buffer[_buffered * _channels])
            {
                _buffered += _converter.Convert(frame, dst, capacity);
            }
        }

        public DecodedBlock? TakeBlock(int epoch)
        {
            if (_buffered == 0)
            {
                return null;
            }

            var samples = _buffer.AsSpan(0, _buffered * _channels).ToArray();
            var block = new DecodedBlock(_index, epoch, _startFrame, samples);
            _startFrame += _buffered;
            _buffered = 0;
            return block;
        }

        public void Dispose()
        {
            _converter?.Dispose();
            _converter = null;
            var codec = Codec;
            ffmpeg.avcodec_free_context(&codec);
            Codec = null;
            var layout = _outLayout;
            ffmpeg.av_channel_layout_uninit(&layout);
        }

        private void EnsureCapacity(int floats)
        {
            if (_buffer.Length < floats)
            {
                Array.Resize(ref _buffer, Math.Max(floats, _buffer.Length * 2));
            }
        }

        private readonly record struct MixFormatInfo(int Rate, int Channels);
    }
}
