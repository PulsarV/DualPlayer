using DualPlayer.App.ViewModels;
using DualPlayer.Core;
using DualPlayer.Media;
using DualPlayer.Platform.MacOS;
using DualPlayer.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddMedia();
        if (OperatingSystem.IsWindows())
        {
            services.AddWindowsPlatform();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddMacOSPlatform();
        }

        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
