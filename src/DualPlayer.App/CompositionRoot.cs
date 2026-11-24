using DualPlayer.App.Startup;
using DualPlayer.App.ViewModels;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Media;
using DualPlayer.Media.Video;
using DualPlayer.Platform.MacOS;
using DualPlayer.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App;

/// <summary>The only place modules are wired together and the platform adapter is selected (ADR-0003).</summary>
public static class CompositionRoot
{
    /// <summary>Windows: <c>%APPDATA%/DualPlayer</c>; macOS: <c>~/Library/Application Support/DualPlayer</c>.</summary>
    public static AppPaths PathsForCurrentUser()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return AppPaths.FromRoot(Path.Combine(root, AppPaths.AppFolderName));
    }

    public static ServiceProvider Build(AppPaths paths, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        services.AddCore(paths.StateDirectory);

        // Notes take the design tokens (read when the renderer is first resolved, after Avalonia has loaded them).
        services.AddSingleton(_ => Avalonia.Application.Current is { } app ? OsdNoteTokens.From(app.Resources) : OsdNoteStyle.Default);
        services.AddMedia();

        // The picture goes into the playback window: Media's renderer, decorated explicitly after AddMedia.
        services.AddSingleton<VideoSurface>();
        services.Replace(ServiceDescriptor.Singleton<IVideoRenderer>(sp => new WindowBoundRenderer(
            sp.GetRequiredService<MpvVideoRenderer>(),
            sp.GetRequiredService<VideoSurface>())));
        if (OperatingSystem.IsWindows())
        {
            services.AddWindowsPlatform();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddMacOSPlatform();
        }

        services.AddSingleton<IScreenCatalog, AvaloniaScreenCatalog>();
        services.AddSingleton<IDeviceAccessProbe, BackendDeviceAccessProbe>();
        services.AddSingleton<PlaybackControls>();
        services.AddSingleton<IPlaybackControls>(sp => sp.GetRequiredService<PlaybackControls>());
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<PlaybackViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
