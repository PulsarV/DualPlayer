using DualPlayer.Console;
using DualPlayer.Core;
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
}
