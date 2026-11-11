using System.Runtime.Versioning;
using DualPlayer.Core.Outputs;
using DualPlayer.Platform.Windows.Audio;
using Microsoft.Extensions.DependencyInjection;

namespace DualPlayer.Platform.Windows;

public static class WindowsPlatformServiceCollectionExtensions
{
    /// <summary>Registers the WASAPI audio backend (device watcher and BT diagnostics come later).</summary>
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            AddBackend(services);
        }

        return services;
    }

    [SupportedOSPlatform("windows")]
    private static void AddBackend(IServiceCollection services) =>
        services.AddSingleton<IAudioBackend, WasapiAudioBackend>();
}
