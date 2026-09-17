using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.TV;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.BetterNextUp;

/// <summary>
/// Wraps Jellyfin's Next Up manager in <see cref="PlexStyleNextUp"/> and installs the Continue Watching merge.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        // Plugins register after the core, so re-registering the core manager under its own type and
        // binding the interface to the wrapper is enough for every consumer to resolve the wrapper.
        var core = serviceCollection.Last(d => d.ServiceType == typeof(ITVSeriesManager));
        var coreType = core.ImplementationType
            ?? throw new InvalidOperationException("Jellyfin registered ITVSeriesManager without an implementation type.");

        serviceCollection.Remove(core);
        serviceCollection.AddSingleton(coreType);
        serviceCollection.AddSingleton(provider => new PlexStyleNextUp(
            (ITVSeriesManager)provider.GetRequiredService(coreType),
            provider.GetRequiredService<ILibraryManager>(),
            provider.GetRequiredService<IUserDataManager>()));
        serviceCollection.AddSingleton<ITVSeriesManager>(provider => provider.GetRequiredService<PlexStyleNextUp>());
        serviceCollection.AddSingleton<INextUpRanking>(provider => provider.GetRequiredService<PlexStyleNextUp>());

        // The plugin instance exists only after the container is built, and its configuration object is
        // replaced on every save, so the filter reads it through a delegate.
        serviceCollection.AddSingleton(provider => new ContinueWatchingMergeFilter(
            provider.GetRequiredService<INextUpRanking>(),
            provider.GetRequiredService<IUserManager>(),
            provider.GetRequiredService<ILibraryManager>(),
            provider.GetRequiredService<IUserDataManager>(),
            provider.GetRequiredService<IDtoService>(),
            () => Plugin.Instance!.Configuration));
        serviceCollection.Configure<MvcOptions>(options => options.Filters.AddService<ContinueWatchingMergeFilter>());
    }
}
