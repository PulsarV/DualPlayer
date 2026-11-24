using DualPlayer.Core.Playback;
using DualPlayer.Media.Audio;
using DualPlayer.Media.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Media;

public static class MediaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the FFmpeg audio decoder, the picture probe and the libmpv video renderer. The renderer is registered as
    /// itself too, so a host can decorate <see cref="IVideoRenderer"/> after this call; its note style is
    /// <see cref="OsdNoteStyle.Default"/> unless the host registered one.
    /// </summary>
    public static IServiceCollection AddMedia(this IServiceCollection services)
    {
        services.TryAddSingleton<IAudioDecoder, FfmpegAudioDecoder>();
        services.TryAddSingleton<IPictureProbe, FfmpegPictureProbe>();
        services.TryAddSingleton(OsdNoteStyle.Default);
        services.TryAddSingleton(sp => new MpvVideoRenderer(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<MpvVideoRenderer>>(),
            sp.GetRequiredService<OsdNoteStyle>()));
        services.TryAddSingleton<IVideoRenderer>(sp => sp.GetRequiredService<MpvVideoRenderer>());
        return services;
    }
}
