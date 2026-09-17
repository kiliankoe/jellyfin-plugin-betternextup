using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.BetterNextUp.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;

namespace Jellyfin.Plugin.BetterNextUp.Tests;

public class ContinueWatchingMergeFilterTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly User _user = new("kilian", "auth", "reset");
    private readonly Mock<INextUpRanking> _nextUp = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly List<BaseItemDto> _inProgress = [];
    private readonly List<(BaseItem Episode, DateTime Activity)> _ranked = [];
    private IDictionary<string, object?>? _argumentsSeenByAction;
    private readonly ContinueWatchingMergeFilter _sut;

    public ContinueWatchingMergeFilterTests()
    {
        _userManager.Setup(m => m.GetUserById(_user.Id)).Returns(_user);
        _nextUp.Setup(n => n.Rank(It.Is<NextUpQuery>(q => q.User == _user), It.IsAny<DtoOptions>())).Returns(() => _ranked);
        _dtoService
            .Setup(d => d.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), _user, null, false))
            .Returns((IReadOnlyList<BaseItem> items, DtoOptions _, User _, BaseItem? _, bool _) =>
                items.Select(i => new BaseItemDto { Id = i.Id, SeriesId = (i as Episode)?.SeriesId }).ToList());
        _sut = new ContinueWatchingMergeFilter(
            _nextUp.Object,
            _userManager.Object,
            _library.Object,
            _userData.Object,
            _dtoService.Object,
            () => new PluginConfiguration());
    }

    [Fact]
    public async Task MergesNextUpIntoContinueWatchingByActivityForInfuse()
    {
        var pausedSeries = Guid.NewGuid();
        var paused = InProgress(pausedSeries, lastPlayed: Now.AddDays(-2));
        NextUp(pausedSeries, activity: Now.AddDays(-2));
        var revived = NextUp(Guid.NewGuid(), activity: Now.AddDays(-1));
        NextUp(Guid.NewGuid(), activity: Now.AddDays(-10));

        var result = await Run("Infuse-Direct", new() { ["limit"] = 2, ["startIndex"] = 0 });

        Assert.Equal([revived.Id, paused.Id], result.Items.Select(i => i.Id));
        Assert.Equal(3, result.TotalRecordCount);
        Assert.Null(_argumentsSeenByAction!["limit"]);
        Assert.Null(_argumentsSeenByAction["startIndex"]);
    }

    [Fact]
    public async Task LeavesOtherClientsAlone()
    {
        var paused = InProgress(Guid.NewGuid(), lastPlayed: Now.AddDays(-2));
        NextUp(Guid.NewGuid(), activity: Now.AddDays(-1));

        var result = await Run("Jellyfin Web", new() { ["limit"] = 2 });

        Assert.Equal([paused.Id], result.Items.Select(i => i.Id));
        Assert.Equal(2, _argumentsSeenByAction!["limit"]);
        _nextUp.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LeavesRequestsWithoutVideoAlone()
    {
        InProgress(Guid.NewGuid(), lastPlayed: Now.AddDays(-2));
        NextUp(Guid.NewGuid(), activity: Now.AddDays(-1));

        var result = await Run("Infuse-Direct", new() { ["mediaTypes"] = new[] { Jellyfin.Data.Enums.MediaType.Audio } });

        Assert.Single(result.Items);
        _nextUp.VerifyNoOtherCalls();
    }

    private BaseItemDto InProgress(Guid seriesId, DateTime lastPlayed)
    {
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = seriesId };
        _library.Setup(l => l.GetItemById(episode.Id)).Returns(episode);
        _userData.Setup(u => u.GetUserData(_user, episode)).Returns(new UserItemData { Key = "k", LastPlayedDate = lastPlayed });
        var dto = new BaseItemDto { Id = episode.Id, SeriesId = seriesId };
        _inProgress.Add(dto);
        return dto;
    }

    private Episode NextUp(Guid seriesId, DateTime activity)
    {
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = seriesId };
        _ranked.Add((episode, activity));
        return episode;
    }

    private async Task<QueryResult<BaseItemDto>> Run(string client, Dictionary<string, object?> arguments)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-Client", client), new Claim("Jellyfin-UserId", _user.Id.ToString("N"))])),
        };
        var descriptor = new ControllerActionDescriptor { ControllerName = "Items", ActionName = "GetResumeItems" };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var executing = new ActionExecutingContext(actionContext, [], arguments, new object());
        var executed = new ActionExecutedContext(actionContext, [], new object());

        await _sut.OnActionExecutionAsync(executing, () =>
        {
            _argumentsSeenByAction = new Dictionary<string, object?>(executing.ActionArguments);
            executed.Result = new ObjectResult(new QueryResult<BaseItemDto>(
                arguments.GetValueOrDefault("startIndex") as int?,
                _inProgress.Count,
                _inProgress.Take(arguments.GetValueOrDefault("limit") as int? ?? int.MaxValue).ToList()));
            return Task.FromResult(executed);
        });

        return (QueryResult<BaseItemDto>)((ObjectResult)executed.Result!).Value!;
    }
}
