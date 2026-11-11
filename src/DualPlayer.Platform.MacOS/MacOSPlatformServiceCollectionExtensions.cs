using Microsoft.Extensions.DependencyInjection;

namespace DualPlayer.Platform.MacOS;

public static class MacOSPlatformServiceCollectionExtensions
{
    /// <summary>Registers the dev-only AudioQueue audio backend (none yet).</summary>
    public static IServiceCollection AddMacOSPlatform(this IServiceCollection services) => services;
}
