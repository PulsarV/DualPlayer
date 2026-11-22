using System.Runtime.InteropServices;
using DualPlayer.Core;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Audio;
using FFmpeg.AutoGen;

namespace DualPlayer.Media.Video;

/// <summary>
/// Opens a file, finds the picture and decodes its first frame in hardware (D3D11VA on Windows, VideoToolbox on the
/// dev Mac), with no window and no software fallback (ADR-0007). Runs on a worker so the caller is never held past the
/// time limit.
/// </summary>
public sealed unsafe class FfmpegPictureProbe : IPictureProbe
{
    private const string Unreadable = "playback.file_unreadable";
    private const string NoPicture = "video.no_picture";
    private const string NotHardwareDecodable = "video.not_hardware_decodable";
    private const string ProbeTimeout = "video.probe_timeout";

    // Test seam: runs on the worker right after codec and profile are published.
    internal Action? AfterFormatRead { get; init; }

    public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Probe(path, timeLimit, CancellationToken.None);

    public Result<PictureInfo> Probe(string path, TimeSpan timeLimit, CancellationToken cancel)
    {
        var loaded = FfmpegLibrary.Load();
        if (!loaded.IsSuccess)
        {
            return Result<PictureInfo>.Failure(loaded.Error);
        }

        // The worker's own token: tripped when the caller gives up or the limit runs out, so the worker's open or read is
        // interrupted and its file closed instead of the worker being left running.
        var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var known = new KnownFormat();
        var worker = Task.Factory.StartNew(() => ProbeCore(path, known, stop.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            if (worker.Wait(timeLimit, cancel))
            {
                stop.Dispose();
                return worker.Result;
            }
        }
        catch (OperationCanceledException)
        {
            stop.Cancel();
            return Result<PictureInfo>.Failure(Unreadable, $"The probe of '{path}' was cancelled.");
        }
        catch (AggregateException ex)
        {
            stop.Dispose();
            return Result<PictureInfo>.Failure(Unreadable, $"The picture of '{path}' could not be probed: {ex.GetBaseException().Message}");
        }

        stop.Cancel();

        var message = $"The first picture of '{path}' was not decoded in hardware within {timeLimit.TotalSeconds:0.##} s.";
        var format = known.Value;
        if (format is null)
        {
            return Result<PictureInfo>.Failure(ProbeTimeout, message);
        }

        return Result<PictureInfo>.Failure(
            new Error(ProbeTimeout, $"{message} The picture format is {format.Codec} (profile {format.Profile}).")
            {
                Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["codec"] = format.Codec, ["profile"] = format.Profile },
            });
    }

    private sealed record Format(string Codec, string Profile);

    private sealed class KnownFormat
    {
        private volatile Format? _value;

        public Format? Value => _value;

        public void Publish(string codec, string profile) => _value = new Format(codec, profile);
    }

    private Result<PictureInfo> ProbeCore(string path, KnownFormat known, CancellationToken cancel)
    {
        using var interrupt = new FfmpegInterrupt(cancel);
        AVFormatContext* fmt = null;
        AVCodecContext* codec = null;
        AVBufferRef* device = null;
        AVPacket* packet = null;
        AVFrame* frame = null;
        try
        {
            if (!interrupt.Attach(&fmt))
            {
                return Result<PictureInfo>.Failure(Unreadable, $"'{path}' could not be probed: out of memory.");
            }

            var rc = ffmpeg.avformat_open_input(&fmt, path, null, null);
            if (rc < 0)
            {
                return OpenFailure(path, rc);
            }

            if (ffmpeg.avformat_find_stream_info(fmt, null) < 0)
            {
                return Result<PictureInfo>.Failure(Unreadable, $"'{path}' cannot be read as media.");
            }

            // No decoder out-parameter: with one, FFmpeg skips a stream it has no decoder for and reports
            // AVERROR_DECODER_NOT_FOUND, which would hide that the picture exists but cannot be played.
            var index = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (index < 0)
            {
                return index == ffmpeg.AVERROR_STREAM_NOT_FOUND
                    ? Result<PictureInfo>.Failure(NoPicture, $"'{path}' has no picture.")
                    : Result<PictureInfo>.Failure(Unreadable, $"'{path}' cannot be read as media.");
            }

            var parameters = fmt->streams[index]->codecpar;
            var codecName = ffmpeg.avcodec_get_name(parameters->codec_id) ?? "unknown";
            var profileName = ffmpeg.avcodec_profile_name(parameters->codec_id, parameters->profile) ?? "unknown";
            known.Publish(codecName, profileName);
            AfterFormatRead?.Invoke();
            var width = parameters->width;
            var height = parameters->height;
            var notHw = Result<PictureInfo>.Failure(
                new Error(NotHardwareDecodable, $"The picture format {codecName} (profile {profileName}) cannot be decoded in hardware on this PC.")
                {
                    Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["codec"] = codecName, ["profile"] = profileName },
                });

            var decoder = ffmpeg.avcodec_find_decoder(parameters->codec_id);
            if (decoder == null)
            {
                return notHw;
            }

            var type = OperatingSystem.IsWindows() ? AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA : AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;
            if (ffmpeg.av_hwdevice_ctx_create(&device, type, null, null, 0) < 0)
            {
                return notHw;
            }

            codec = ffmpeg.avcodec_alloc_context3(decoder);
            if (codec == null
                || ffmpeg.avcodec_parameters_to_context(codec, parameters) < 0)
            {
                return notHw;
            }

            codec->hw_device_ctx = ffmpeg.av_buffer_ref(device);
            if (ffmpeg.avcodec_open2(codec, decoder, null) < 0)
            {
                return notHw;
            }

            packet = ffmpeg.av_packet_alloc();
            frame = ffmpeg.av_frame_alloc();
            while (ffmpeg.av_read_frame(fmt, packet) >= 0)
            {
                var isPicture = packet->stream_index == index;
                var sent = isPicture ? ffmpeg.avcodec_send_packet(codec, packet) : 0;
                ffmpeg.av_packet_unref(packet);
                if (sent < 0)
                {
                    return notHw;
                }

                if (isPicture && ffmpeg.avcodec_receive_frame(codec, frame) >= 0)
                {
                    // A frame in system memory means the decoder fell back to software.
                    return frame->hw_frames_ctx != null
                        ? Result<PictureInfo>.Success(new PictureInfo(codecName, profileName, width, height))
                        : notHw;
                }
            }

            return notHw;
        }
        catch (Exception ex) when (ex is SEHException or AccessViolationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return Result<PictureInfo>.Failure(Unreadable, $"'{path}' could not be probed: {ex.Message}");
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&codec);
            ffmpeg.av_buffer_unref(&device);
            ffmpeg.avformat_close_input(&fmt);
        }
    }

    internal static Result<PictureInfo> OpenFailure(string path, int rc) =>
        Result<PictureInfo>.Failure(FfmpegErrors.OpenFailure(path, rc));
}
