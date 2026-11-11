using DualPlayer.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DualPlayer.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers Core services. <paramref name="stateDirectory"/> is the per-user app-data folder.</summary>
    public static IServiceCollection AddCore(this IServiceCollection services, string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        services.AddSingleton<IStateStore>(_ => new JsonFileStateStore(stateDirectory));
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
