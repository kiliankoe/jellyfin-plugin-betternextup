using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Jellyfin.Plugin.BetterNextUp.Tests;

public class PluginServiceRegistratorTests
{
    [Fact]
    public void WrapsTheNextUpManagerJellyfinRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITVSeriesManager, CoreManager>();
        services.AddSingleton(Mock.Of<ILibraryManager>());
        services.AddSingleton(Mock.Of<IUserDataManager>());

        new PluginServiceRegistrator().RegisterServices(services, Mock.Of<IServerApplicationHost>());

        using var provider = services.BuildServiceProvider();
        Assert.IsType<PlexStyleNextUp>(Assert.Single(provider.GetServices<ITVSeriesManager>()));
        Assert.NotNull(provider.GetService<CoreManager>());
        Assert.Same(provider.GetRequiredService<ITVSeriesManager>(), provider.GetRequiredService<INextUpRanking>());
    }

    private sealed class CoreManager : ITVSeriesManager
    {
        public QueryResult<BaseItem> GetNextUp(NextUpQuery query, DtoOptions options) => new();

        public QueryResult<BaseItem> GetNextUp(NextUpQuery request, BaseItem[] parentsFolders, DtoOptions options) => new();
    }
}
