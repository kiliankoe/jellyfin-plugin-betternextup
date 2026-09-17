using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.BetterNextUp.Tests;

public class PlexStyleNextUpTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly User _user = new("kilian", "auth", "reset");
    private readonly Mock<ITVSeriesManager> _core = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly List<BaseItem> _coreItems = [];
    private readonly List<NextUpQuery> _coreQueries = [];
    private readonly PlexStyleNextUp _sut;

    public PlexStyleNextUpTests()
    {
        _core
            .Setup(c => c.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Returns((NextUpQuery query, DtoOptions _) =>
            {
                _coreQueries.Add(query);
                return new QueryResult<BaseItem>(_coreItems.ToArray());
            });
        _sut = new PlexStyleNextUp(_core.Object, _library.Object, _userData.Object);
    }

    [Fact]
    public void MovesShowWithNewlyAddedEpisodeToTheFront()
    {
        var bingedRecently = NextUp("recent", lastPlayed: Now.AddDays(-2), added: Now.AddDays(-100));
        var dormantWithNewEpisode = NextUp("dormant", lastPlayed: Now.AddDays(-60), added: Now.AddDays(-1));
        var dormantWithOldEpisode = NextUp("stale", lastPlayed: Now.AddDays(-30), added: Now.AddDays(-200));

        var result = _sut.GetNextUp(Query(), new DtoOptions());

        Assert.Equal([dormantWithNewEpisode, bingedRecently, dormantWithOldEpisode], result.Items);
    }

    [Fact]
    public void FallsBackToDateAddedWhenNoPlayDateIsRecorded()
    {
        var undated = NextUp("undated", lastPlayed: null, added: Now.AddDays(-1));
        var dated = NextUp("dated", lastPlayed: Now.AddDays(-5), added: Now.AddDays(-100));

        var result = _sut.GetNextUp(Query(), new DtoOptions());

        Assert.Equal([undated, dated], result.Items);
    }

    [Fact]
    public void AppliesTheCutoffToTheNewestActivityInsteadOfTheLastPlayDate()
    {
        var revived = NextUp("revived", lastPlayed: Now.AddDays(-400), added: Now.AddDays(-5));
        var forgotten = NextUp("forgotten", lastPlayed: Now.AddDays(-400), added: Now.AddDays(-380));

        var result = _sut.GetNextUp(Query(cutoff: Now.AddDays(-365)), new DtoOptions());

        Assert.Equal([revived], result.Items);
        var coreQuery = Assert.Single(_coreQueries);
        Assert.Equal(DateTime.MinValue, coreQuery.NextUpDateCutoff);
        Assert.Null(coreQuery.Limit);
        Assert.Null(coreQuery.StartIndex);
    }

    [Fact]
    public void PagesAfterReordering()
    {
        NextUp("first", lastPlayed: Now.AddDays(-1), added: Now.AddDays(-9));
        var second = NextUp("second", lastPlayed: Now.AddDays(-2), added: Now.AddDays(-9));
        NextUp("third", lastPlayed: Now.AddDays(-9), added: Now.AddDays(-3));

        var result = _sut.GetNextUp(Query(startIndex: 1, limit: 1), new DtoOptions());

        Assert.Equal([second], result.Items);
        Assert.Equal(3, result.TotalRecordCount);
        Assert.Equal(1, result.StartIndex);
    }

    [Fact]
    public void LeavesSingleSeriesQueriesToTheCore()
    {
        var query = Query();
        query.SeriesId = Guid.NewGuid();
        query.Limit = 1;

        _sut.GetNextUp(query, new DtoOptions());

        Assert.Same(query, Assert.Single(_coreQueries));
    }

    [Fact]
    public void ReordersTheParentFolderOverloadToo()
    {
        BaseItem[] parents = [new Folder()];
        _core
            .Setup(c => c.GetNextUp(It.IsAny<NextUpQuery>(), parents, It.IsAny<DtoOptions>()))
            .Returns(() => new QueryResult<BaseItem>(_coreItems.ToArray()));
        var stale = NextUp("stale", lastPlayed: Now.AddDays(-30), added: Now.AddDays(-200));
        var revived = NextUp("revived", lastPlayed: Now.AddDays(-60), added: Now.AddDays(-1));

        var result = _sut.GetNextUp(Query(), parents, new DtoOptions());

        Assert.Equal([revived, stale], result.Items);
    }

    private NextUpQuery Query(DateTime? cutoff = null, int? startIndex = null, int? limit = null) => new()
    {
        User = _user,
        NextUpDateCutoff = cutoff ?? DateTime.MinValue,
        StartIndex = startIndex,
        Limit = limit,
    };

    /// <summary>
    /// Registers a next-up episode for a series and the play history the plugin will look up for it.
    /// </summary>
    private Episode NextUp(string series, DateTime? lastPlayed, DateTime added)
    {
        var nextUp = new Episode { Id = Guid.NewGuid(), SeriesPresentationUniqueKey = series, DateCreated = added };
        _coreItems.Add(nextUp);

        var played = lastPlayed is null ? Array.Empty<BaseItem>() : [new Episode { Id = Guid.NewGuid(), SeriesPresentationUniqueKey = series }];
        _library
            .Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(q => q.SeriesPresentationUniqueKey == series && q.IsPlayed == true && q.Limit == 1)))
            .Returns(played);
        foreach (var episode in played)
        {
            _userData.Setup(u => u.GetUserData(_user, episode)).Returns(new UserItemData { Key = series, LastPlayedDate = lastPlayed });
        }

        return nextUp;
    }
}
