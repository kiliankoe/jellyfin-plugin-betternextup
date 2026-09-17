using System.Security.Claims;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.BetterNextUp.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.BetterNextUp;

/// <summary>
/// Folds Next Up into the Continue Watching response for clients that never ask for Next Up.
/// Infuse builds its Watching shelf from Continue Watching alone, so without this it never shows
/// the next episode of a show. Both lists are merged into one, ordered by activity like Plex does.
/// </summary>
public sealed class ContinueWatchingMergeFilter : IAsyncActionFilter
{
    // Claim names live in Jellyfin.Api, which is not part of the plugin SDK.
    private const string ClientClaim = "Jellyfin-Client";
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly INextUpRanking _nextUp;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IDtoService _dtoService;
    private readonly Func<PluginConfiguration> _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContinueWatchingMergeFilter"/> class.
    /// </summary>
    /// <param name="nextUp">The Next Up ranking.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="dtoService">The dto service.</param>
    /// <param name="configuration">Returns the current plugin configuration.</param>
    public ContinueWatchingMergeFilter(
        INextUpRanking nextUp,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        IDtoService dtoService,
        Func<PluginConfiguration> configuration)
    {
        _nextUp = nextUp;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _dtoService = dtoService;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!IsContinueWatchingForMergedClient(context))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Paging has to happen after the merge, so take the whole list from the core.
        var startIndex = Argument<int?>(context, "startIndex");
        var limit = Argument<int?>(context, "limit");
        context.ActionArguments["startIndex"] = null;
        context.ActionArguments["limit"] = null;

        var executed = await next().ConfigureAwait(false);
        if (executed.Result is not ObjectResult { Value: QueryResult<BaseItemDto> continueWatching })
        {
            return;
        }

        var user = _userManager.GetUserById(Argument<Guid?>(context, "userId") ?? GetUserId(context.HttpContext.User));
        if (user is null)
        {
            return;
        }

        var inProgress = continueWatching.Items.Select(dto => (Item: dto, Activity: LastPlayed(user, dto)));
        var inProgressSeries = continueWatching.Items.Select(dto => dto.SeriesId).OfType<Guid>().ToHashSet();
        var options = DtoOptions(context);
        var ranked = _nextUp
            .Rank(new NextUpQuery { User = user, ParentId = Argument<Guid?>(context, "parentId"), EnableTotalRecordCount = false }, options)
            .Where(x => x.Episode is not IHasSeries series || !inProgressSeries.Contains(series.SeriesId))
            .ToList();
        var nextUp = _dtoService
            .GetBaseItemDtos(ranked.Select(x => x.Episode).ToList(), options, user)
            .Zip(ranked, (dto, x) => (Item: dto, x.Activity));

        var merged = inProgress.Concat(nextUp).OrderByDescending(x => x.Activity).Select(x => x.Item).ToList();
        var page = merged.Skip(startIndex ?? 0);
        if (limit > 0)
        {
            page = page.Take(limit.Value);
        }

        continueWatching.Items = page.ToList();
        continueWatching.StartIndex = startIndex ?? 0;
        continueWatching.TotalRecordCount = Argument<bool?>(context, "enableTotalRecordCount") ?? true ? merged.Count : 0;
    }

    private bool IsContinueWatchingForMergedClient(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || !string.Equals(action.ControllerName, "Items", StringComparison.Ordinal)
            || !action.ActionName.StartsWith("GetResumeItems", StringComparison.Ordinal))
        {
            return false;
        }

        var client = context.HttpContext.User.FindFirstValue(ClientClaim) ?? string.Empty;
        var prefixes = _configuration().ContinueWatchingMergeClients.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!prefixes.Any(prefix => client.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var mediaTypes = Argument<MediaType[]>(context, "mediaTypes") ?? [];
        var includeItemTypes = Argument<BaseItemKind[]>(context, "includeItemTypes") ?? [];
        return (mediaTypes.Length == 0 || mediaTypes.Contains(MediaType.Video))
            && (includeItemTypes.Length == 0 || includeItemTypes.Contains(BaseItemKind.Episode));
    }

    private DateTime LastPlayed(User user, BaseItemDto dto)
    {
        var item = _libraryManager.GetItemById(dto.Id);
        return item is null ? DateTime.MinValue : _userDataManager.GetUserData(user, item)?.LastPlayedDate ?? DateTime.MinValue;
    }

    private static Guid GetUserId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(UserIdClaim), out var id) ? id : Guid.Empty;

    private static T? Argument<T>(ActionExecutingContext context, string name)
        => context.ActionArguments.TryGetValue(name, out var value) && value is T typed ? typed : default;

    // Mirrors what the Continue Watching action itself builds from the same arguments.
    private static DtoOptions DtoOptions(ActionExecutingContext context)
    {
        var options = new DtoOptions
        {
            Fields = Argument<ItemFields[]>(context, "fields") ?? [],
            EnableImages = Argument<bool?>(context, "enableImages") ?? true,
        };

        if (Argument<int?>(context, "imageTypeLimit") is { } imageTypeLimit)
        {
            options.ImageTypeLimit = imageTypeLimit;
        }

        if (Argument<bool?>(context, "enableUserData") is { } enableUserData)
        {
            options.EnableUserData = enableUserData;
        }

        if (Argument<ImageType[]>(context, "enableImageTypes") is { Length: > 0 } imageTypes)
        {
            options.ImageTypes = imageTypes;
        }

        return options;
    }
}
