using DualPlayer.App;
using DualPlayer.App.Startup;
using DualPlayer.App.Tests.Views;
using DualPlayer.App.ViewModels;
using DualPlayer.Core;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Settings;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualPlayer.App.Tests;

public sealed class CompositionRootSmokeTests
{
    // Resolving the renderer reads Application.Current.Resources, an Avalonia object: once the headless application of the
    // other tests exists it belongs to its UI thread, so every test here runs on that thread (never on an xunit worker).
    private static void OnTheUiThread(Action body) => HeadlessApp.Run(body);

    [Fact]
    public void Container_builds_and_resolves_the_main_view_model() => OnTheUiThread(() =>
    {
        var paths = AppPaths.FromRoot(Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths, NullLoggerFactory.Instance);

        services.GetRequiredService<MainWindowViewModel>().Title.ShouldBe("DualPlayer");
        services.GetRequiredService<IStateStore>().ShouldNotBeNull();
        services.GetRequiredService<IAudioEngine>().ShouldNotBeNull();
    });

    [Fact]
    public void Container_resolves_the_playback_view_model_screen_catalog_and_latency_probe_for_the_play_command() => OnTheUiThread(() =>
    {
        var paths = AppPaths.FromRoot(Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths, NullLoggerFactory.Instance);

        services.GetService<PlaybackViewModel>().ShouldNotBeNull();
        services.GetService<IScreenCatalog>().ShouldNotBeNull().GetType().Name.ShouldBe("AvaloniaScreenCatalog");
        services.GetService<IDeviceLatencyProbe>().ShouldNotBeNull();
    });

    [Fact]
    public void The_video_renderer_is_media_s_mpv_renderer_decorated_once_for_the_playback_window() => OnTheUiThread(() =>
    {
        var paths = AppPaths.FromRoot(Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths, NullLoggerFactory.Instance);

        services.GetServices<IVideoRenderer>().ShouldHaveSingleItem().ShouldBeOfType<WindowBoundRenderer>();
        services.GetService<DualPlayer.Media.Video.MpvVideoRenderer>().ShouldNotBeNull("AddMedia registers the renderer the App decorates");
    });
}
