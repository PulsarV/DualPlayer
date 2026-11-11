using Microsoft.Extensions.DependencyInjection;

namespace DualPlayer.Platform.Windows;

public static class WindowsPlatformServiceCollectionExtensions
{
    /// <summary>Registers the WASAPI audio backend, device watcher and BT diagnostics (none yet).</summary>
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services) => services;
}
