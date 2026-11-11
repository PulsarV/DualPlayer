using DualPlayer.App;
using DualPlayer.App.ViewModels;
using DualPlayer.Core;
using DualPlayer.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualPlayer.App.Tests;

public sealed class CompositionRootSmokeTests
{
    [Fact]
    public void Container_builds_and_resolves_the_main_view_model()
    {
        var paths = AppPaths.FromRoot(Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths, NullLoggerFactory.Instance);

        services.GetRequiredService<MainWindowViewModel>().Title.ShouldBe("DualPlayer");
        services.GetRequiredService<IStateStore>().ShouldNotBeNull();
    }
}
