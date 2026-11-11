using Microsoft.Extensions.DependencyInjection;

namespace DualPlayer.Media;

public static class MediaServiceCollectionExtensions
{
    /// <summary>Registers the FFmpeg decoder and libmpv renderer adapters (none yet).</summary>
    public static IServiceCollection AddMedia(this IServiceCollection services) => services;
}
