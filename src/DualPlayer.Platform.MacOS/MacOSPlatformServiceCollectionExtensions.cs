using DualPlayer.Core.Outputs;
using DualPlayer.Platform.MacOS.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DualPlayer.Platform.MacOS;

public static class MacOSPlatformServiceCollectionExtensions
{
    /// <summary>Registers the dev-only AudioQueue audio backend.</summary>
    public static IServiceCollection AddMacOSPlatform(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAudioBackend>(sp => new AudioQueueBackend(sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
