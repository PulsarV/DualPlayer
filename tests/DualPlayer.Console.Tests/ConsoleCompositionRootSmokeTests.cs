using DualPlayer.Console;
using DualPlayer.Console.Commands;
using DualPlayer.Core;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualPlayer.Console.Tests;

public sealed class ConsoleCompositionRootSmokeTests
{
    [Fact]
    public void Container_builds_with_validation_and_resolves_core_services()
    {
        var paths = AppPaths.FromRoot(Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N")));

        using var services = ConsoleCompositionRoot.Build(paths, NullLoggerFactory.Instance);

        services.GetRequiredService<IStateStore>().ShouldNotBeNull();
        services.GetRequiredService<AppPaths>().ShouldBe(paths);
    }

    [Fact]
    public void Container_resolves_the_play_path_commands_and_the_engine()
    {
        var paths = AppPaths.FromRoot(Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N")));

        using var services = ConsoleCompositionRoot.Build(paths, NullLoggerFactory.Instance);

        services.GetRequiredService<PlayCommand>().ShouldNotBeNull();
        services.GetRequiredService<DevicesCommand>().ShouldNotBeNull();
        services.GetRequiredService<TracksCommand>().ShouldNotBeNull();
        services.GetRequiredService<IAudioEngine>().ShouldNotBeNull();
    }
}
