using DualPlayer.Core.Playback;
using DualPlayer.Media.Audio;
using DualPlayer.Media.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DualPlayer.Media;

public static class MediaServiceCollectionExtensions
{
    /// <summary>Registers the FFmpeg audio decoder and the libmpv video renderer.</summary>
    public static IServiceCollection AddMedia(this IServiceCollection services)
    {
        services.TryAddSingleton<IAudioDecoder, FfmpegAudioDecoder>();
        services.TryAddSingleton<IVideoRenderer, MpvVideoRenderer>();
        return services;
    }
}
