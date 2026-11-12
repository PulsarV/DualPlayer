using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using FFmpeg.AutoGen;

namespace DualPlayer.Media.Audio;

/// <summary>FFmpeg-backed <see cref="IAudioDecoder"/>: lists audio tracks and opens one-pass decode sessions.</summary>
public sealed unsafe class FfmpegAudioDecoder : IAudioDecoder
{
    internal const string Unreadable = "playback.file_unreadable";

    public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path)
    {
        var loaded = FfmpegLibrary.Load();
        if (!loaded.IsSuccess)
        {
            return Result<IReadOnlyList<AudioTrackInfo>>.Failure(Unreadable, loaded.Error.Message);
        }

        AVFormatContext* fmt = null;
        try
        {
            var open = OpenInput(path, &fmt);
            if (!open.IsSuccess)
            {
                return Result<IReadOnlyList<AudioTrackInfo>>.Failure(open.Error);
            }

            var tracks = new List<AudioTrackInfo>();
            foreach (var ptr in AudioStreams(fmt))
            {
                var stream = (AVStream*)ptr;
                var par = stream->codecpar;
                tracks.Add(new AudioTrackInfo(
                    tracks.Count + 1,
                    LanguageOf(stream),
                    ToLayout(par->ch_layout.nb_channels),
                    par->sample_rate)
                {
                    Channels = par->ch_layout.nb_channels,
                    HasLfe = HasLfe(par->ch_layout),
                });
            }

            return Result<IReadOnlyList<AudioTrackInfo>>.Success(tracks);
        }
        finally
        {
            ffmpeg.avformat_close_input(&fmt);
        }
    }

    public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs)
    {
        var loaded = FfmpegLibrary.Load();
        if (!loaded.IsSuccess)
        {
            return Result<IDecodeSession>.Failure(Unreadable, loaded.Error.Message);
        }

        return FfmpegDecodeSession.Create(path, outputs);
    }

    internal static Result OpenInput(string path, AVFormatContext** fmt)
    {
        if (!File.Exists(path))
        {
            return Result.Failure(Unreadable, $"The file '{path}' does not exist.");
        }

        var rc = ffmpeg.avformat_open_input(fmt, path, null, null);
        if (rc < 0)
        {
            return Result.Failure(Unreadable, $"The file '{path}' cannot be opened as media: {ErrorText(rc)}.");
        }

        rc = ffmpeg.avformat_find_stream_info(*fmt, null);
        if (rc < 0)
        {
            return Result.Failure(Unreadable, $"The file '{path}' cannot be read as media: {ErrorText(rc)}.");
        }

        return Result.Success();
    }

    internal static List<nint> AudioStreams(AVFormatContext* fmt)
    {
        var list = new List<nint>();
        for (var i = 0; i < fmt->nb_streams; i++)
        {
            var s = fmt->streams[i];
            if (s->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
            {
                list.Add((nint)s);
            }
        }

        return list;
    }

    // Same fact MixFormatConverter uses to say the LFE is dropped when folding onto fewer channels.
    private static bool HasLfe(AVChannelLayout layout)
    {
        if (layout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC)
        {
            ffmpeg.av_channel_layout_default(&layout, layout.nb_channels);
        }

        for (var i = 0; i < layout.nb_channels; i++)
        {
            if (ffmpeg.av_channel_layout_channel_from_index(&layout, (uint)i) == AVChannel.AV_CHAN_LOW_FREQUENCY)
            {
                return true;
            }
        }

        return false;
    }

    internal static string ErrorText(int code)
    {
        var buffer = stackalloc byte[256];
        ffmpeg.av_strerror(code, buffer, 256);
        return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"error {code}";
    }

    internal static ChannelLayout ToLayout(int channels) => channels switch
    {
        1 => ChannelLayout.Mono,
        2 => ChannelLayout.Stereo,
        6 => ChannelLayout.Surround51,
        8 => ChannelLayout.Surround71,
        _ => ChannelLayout.Other,
    };

    private static string LanguageOf(AVStream* stream)
    {
        var entry = ffmpeg.av_dict_get(stream->metadata, "language", null, 0);
        var lang = entry is null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)entry->value);
        return string.IsNullOrWhiteSpace(lang) ? "und" : lang;
    }
}
