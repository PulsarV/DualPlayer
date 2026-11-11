using FFmpeg.AutoGen;

namespace DualPlayer.Media.Audio;

/// <summary>
/// One swresample context per output: converts rate, sample format and channel layout into the device mix format
/// (interleaved float) using <see cref="FoldDownMatrix"/>. Frames leave Media already in the device format.
/// </summary>
internal sealed unsafe class MixFormatConverter : IDisposable
{
    private readonly int _outChannels;
    private SwrContext* _swr;

    private MixFormatConverter(SwrContext* swr, int outChannels, string description)
    {
        _swr = swr;
        _outChannels = outChannels;
        Description = description;
    }

    /// <summary>Human-readable conversion, e.g. "6ch 44100 Hz -> 2ch 48000 Hz (LFE dropped)".</summary>
    public string Description { get; }

    public static MixFormatConverter? Create(AVFrame* frame, int outRate, AVChannelLayout* outLayout)
    {
        var inLayout = frame->ch_layout;
        if (inLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC)
        {
            ffmpeg.av_channel_layout_default(&inLayout, inLayout.nb_channels);
        }

        var inChannels = ChannelsOf(&inLayout);
        var outChannels = ChannelsOf(outLayout);
        var matrix = FoldDownMatrix.Build(inChannels, outChannels);

        SwrContext* swr = null;
        if (ffmpeg.swr_alloc_set_opts2(
                &swr, outLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, outRate,
                &inLayout, (AVSampleFormat)frame->format, frame->sample_rate, 0, null) < 0)
        {
            ffmpeg.swr_free(&swr);
            return null;
        }

        int rc;
        fixed (double* m = matrix)
        {
            rc = ffmpeg.swr_set_matrix(swr, m, inChannels.Length);
        }

        if (rc < 0 || ffmpeg.swr_init(swr) < 0)
        {
            ffmpeg.swr_free(&swr);
            return null;
        }

        var lfe = inChannels.Length > outChannels.Length && inChannels.Contains(AVChannel.AV_CHAN_LOW_FREQUENCY) ? ", LFE dropped" : string.Empty;
        var description = $"{inChannels.Length}ch {frame->sample_rate} Hz -> {outChannels.Length}ch {outRate} Hz{lfe}";
        return new MixFormatConverter(swr, outChannels.Length, description);
    }

    /// <summary>Upper bound of the frames produced by converting <paramref name="inFrames"/> input frames.</summary>
    public int OutputCapacity(int inFrames) => ffmpeg.swr_get_out_samples(_swr, inFrames);

    /// <summary>Converts (or, with null input, flushes) into <paramref name="destination"/>; returns frames produced.</summary>
    public int Convert(AVFrame* frame, float* destination, int capacity)
    {
        var outPtr = (byte*)destination;
        var produced = ffmpeg.swr_convert(
            _swr,
            &outPtr,
            capacity,
            frame is null ? null : frame->extended_data,
            frame is null ? 0 : frame->nb_samples);
        return Math.Max(produced, 0);
    }

    public void Dispose()
    {
        var swr = _swr;
        ffmpeg.swr_free(&swr);
        _swr = null;
    }

    private static AVChannel[] ChannelsOf(AVChannelLayout* layout)
    {
        var channels = new AVChannel[layout->nb_channels];
        for (var i = 0; i < channels.Length; i++)
        {
            channels[i] = ffmpeg.av_channel_layout_channel_from_index(layout, (uint)i);
        }

        return channels;
    }
}
