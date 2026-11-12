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
    private long _startTime; // container start_time (AV_TIME_BASE); content position 0 is this timestamp
    private long? _duration;  // length of the content (AV_TIME_BASE), null when unknown
    private int _epoch;
    private bool _drained;
    private bool _restoreFailed;
    private bool _disposed;

    private readonly Func<int>? _rewindSeekHook;

    private string _path = "";

    private FfmpegDecodeSession(Func<int>? rewindSeekHook)
    {
        _rewindSeekHook = rewindSeekHook;
    }

    // rewindSeekHook is a test seam: when set it replaces every seek back to the start after the duration probe (the
    // seek is not made, so the demuxer stays where the probe left it) and returns that seek's result code
    // (negative = failed), so a rewind that cannot be done by seeking can be forced.
    public static Result<IDecodeSession> Create(string path, IReadOnlyList<DecodeRequest> requests, Func<int>? rewindSeekHook = null)
    {
        var session = new FfmpegDecodeSession(rewindSeekHook);
        var opened = session.Init(path, requests);
        if (!opened.IsSuccess)
        {
            session.Dispose();
            return Result<IDecodeSession>.Failure(opened.Error);
        }

        return Result<IDecodeSession>.Success(session);
    }

    public TimeSpan? Duration => !_disposed && _duration is { } d ? TimeSpan.FromSeconds((double)d / ffmpeg.AV_TIME_BASE) : null;

    // Length of the content in AV_TIME_BASE units, or null when unknown. The Matroska Duration element is the span
    // for mkvmerge but the end timestamp (start offset included) for FFmpeg's muxer, and the demuxer reports it as is;
    // with a start offset the two are told apart by looking for packets beyond it. MPEG-TS reports the span.
    private long? ContentDuration()
    {
        var duration = _fmt->duration;
        if (duration <= 0 || duration == ffmpeg.AV_NOPTS_VALUE)
        {
            return null;
        }

        var name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)_fmt->iformat->name) ?? "";
        if (name.Contains("matroska", StringComparison.Ordinal) && _startTime > 0 && !HasPacketsBeyond(duration))
        {
            duration -= _startTime;
        }

        return duration > 0 ? duration : null;
    }

    // True when a packet starts later than the timestamp (plus a tolerance): the value was a span, not an end.
    private bool HasPacketsBeyond(long timestamp)
    {
        var beyond = false;
        var tolerance = ffmpeg.AV_TIME_BASE / 10;
        if (ffmpeg.av_seek_frame(_fmt, -1, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD) >= 0)
        {
            while (!beyond && ffmpeg.av_read_frame(_fmt, _packet) >= 0)
            {
                var pts = _packet->pts == ffmpeg.AV_NOPTS_VALUE ? _packet->dts : _packet->pts;
                if (pts != ffmpeg.AV_NOPTS_VALUE)
                {
                    var stream = _fmt->streams[_packet->stream_index];
                    beyond = ffmpeg.av_rescale_q(pts, stream->time_base, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }) > timestamp + tolerance;
                }

                ffmpeg.av_packet_unref(_packet);
            }
        }

        _restoreFailed = (_rewindSeekHook?.Invoke() ?? ffmpeg.av_seek_frame(_fmt, -1, _startTime, ffmpeg.AVSEEK_FLAG_BACKWARD)) < 0;
        return beyond;
    }

    public Result Seek(TimeSpan position, int epoch)
    {
        if (_disposed)
        {
            return Result.Failure(FfmpegAudioDecoder.Unreadable, "The decode session is closed.");
        }

        var target = (long)(position.TotalSeconds * ffmpeg.AV_TIME_BASE);
        var rc = ffmpeg.av_seek_frame(_fmt, -1, target + _startTime, ffmpeg.AVSEEK_FLAG_BACKWARD);
        _pending.Clear();
        _epoch = epoch;
        if (rc < 0)
        {
            // Seeking beyond the end of a file: nothing is left to play (the engine decides what that means, once the
            // decoder delivers no audio at the target).
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

        _path = path;
        _startTime = _fmt->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : _fmt->start_time;
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

            var created = Output.Create(i, (AVStream*)streams[number - 1], requests[i].Target, _startTime);
            if (!created.IsSuccess)
            {
                return Result.Failure(created.Error);
            }

            _outputs.Add(created.Value);
        }

        _duration = ContentDuration();
        return _restoreFailed ? RestoreStart() : Result.Success();
    }

    // The seek back to the start after the duration probe failed (no index to serve it). Seeking again cannot be
    // trusted (the demuxer may sit at end of file), so the input is reopened: the stream layout and the computed
    // duration are kept, the codecs are flushed, and playback starts from content 0.
    private Result RestoreStart()
    {
        var old = _fmt;
        ffmpeg.avformat_close_input(&old);
        _fmt = null;
        var fmt = _fmt;
        var open = FfmpegAudioDecoder.OpenInput(_path, &fmt);
        _fmt = fmt;
        if (!open.IsSuccess)
        {
            return Result.Failure(FfmpegAudioDecoder.Unreadable, "The file could not be rewound to its start after reading its duration.");
        }

        foreach (var o in _outputs)
        {
            ffmpeg.avcodec_flush_buffers(o.Codec);
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
        private readonly AVRational _timeBase;
        private readonly long _startOffset; // container start_time in output frames, subtracted from every pts
        private long _startFrame; // content position (output frames) of _buffer[0]; the next expected one when empty
        private long _trimTarget; // frames before this are discarded (seek target)
        private bool _anchored;   // _startFrame has been taken from a pts since the last seek

        private Output(int index, AVStream* stream, AVCodecContext* codec, MixFormatInfo target, AVChannelLayout outLayout, long startTime)
        {
            _startOffset = ffmpeg.av_rescale_q(startTime, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, new AVRational { num = 1, den = target.Rate });
            _index = index;
            StreamIndex = stream->index;
            _timeBase = stream->time_base;
            Codec = codec;
            _rate = target.Rate;
            _channels = target.Channels;
            _outLayout = outLayout;
        }

        public int StreamIndex { get; }

        public AVCodecContext* Codec { get; private set; }

        public static Result<Output> Create(int index, AVStream* stream, Core.Outputs.MixFormat target, long startTime)
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
            var output = new Output(index, stream, codec, new MixFormatInfo(target.SampleRate, target.Channels), layout, startTime);
            return Result<Output>.Success(output);
        }

        public void ResetResampler(TimeSpan position)
        {
            _converter?.Dispose();
            _converter = null;
            _buffered = 0;
            _trimTarget = (long)(position.TotalSeconds * _rate);
            _startFrame = _trimTarget;
            _anchored = false;
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

            if (frame is not null && _buffered == 0)
            {
                Anchor(frame);
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

            Trim();
        }

        // Takes the content position of the frame about to be converted from its pts (the timeline of the file), so
        // the first block after a seek lands where the demuxer really delivered, and real gaps stay visible.
        // Jitter within one time-base tick (container rounding) keeps the blocks contiguous.
        private void Anchor(AVFrame* frame)
        {
            var pts = frame->best_effort_timestamp;
            if (pts == ffmpeg.AV_NOPTS_VALUE)
            {
                return;
            }

            var heldByResampler = _converter!.DelayFrames(_rate);
            var start = ffmpeg.av_rescale_q(pts, _timeBase, new AVRational { num = 1, den = _rate }) - _startOffset - heldByResampler;
            var tolerance = Math.Max(1, ffmpeg.av_rescale_q(1, _timeBase, new AVRational { num = 1, den = _rate })) + 1;
            if (!_anchored || Math.Abs(start - _startFrame) > tolerance)
            {
                _startFrame = start;
            }

            _anchored = true;
        }

        // Discards decoded samples before the seek target (the demuxer starts at the previous keyframe).
        private void Trim()
        {
            if (_startFrame >= _trimTarget)
            {
                return;
            }

            var drop = (int)Math.Min(_buffered, _trimTarget - _startFrame);
            if (drop > 0)
            {
                Array.Copy(_buffer, drop * _channels, _buffer, 0, (_buffered - drop) * _channels);
                _buffered -= drop;
                _startFrame += drop;
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
