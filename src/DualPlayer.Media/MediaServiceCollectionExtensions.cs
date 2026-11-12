using DualPlayer.Core.Playback;
using DualPlayer.Media.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DualPlayer.Media;

public static class MediaServiceCollectionExtensions
{
    /// <summary>Registers the FFmpeg audio decoder (the libmpv renderer follows).</summary>
    public static IServiceCollection AddMedia(this IServiceCollection services)
    {
        services.TryAddSingleton<IAudioDecoder, FfmpegAudioDecoder>();
        return services;
    }
}
