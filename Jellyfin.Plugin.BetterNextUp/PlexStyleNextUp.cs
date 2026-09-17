using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.BetterNextUp;

/// <summary>
/// Orders Next Up the way Plex orders On Deck. Jellyfin ranks a show by when it was last watched.
/// Plex ranks it by its latest activity, which is either that or a new episode arriving, so a show
/// you have not touched in months returns to the front when a new episode lands.
/// </summary>
public sealed class PlexStyleNextUp : ITVSeriesManager, INextUpRanking
{
    private readonly ITVSeriesManager _core;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexStyleNextUp"/> class.
    /// </summary>
    /// <param name="core">Jellyfin's own Next Up manager, which picks the episodes.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    public PlexStyleNextUp(ITVSeriesManager core, ILibraryManager libraryManager, IUserDataManager userDataManager)
    {
        _core = core;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    /// <inheritdoc />
    public QueryResult<BaseItem> GetNextUp(NextUpQuery query, DtoOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Page(query, unpaged => _core.GetNextUp(unpaged, options));
    }

    /// <inheritdoc />
    public QueryResult<BaseItem> GetNextUp(NextUpQuery request, BaseItem[] parentsFolders, DtoOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Page(request, unpaged => _core.GetNextUp(unpaged, parentsFolders, options));
    }

    /// <inheritdoc />
    public IReadOnlyList<(BaseItem Episode, DateTime Activity)> Rank(NextUpQuery query, DtoOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Rank(query, unpaged => _core.GetNextUp(unpaged, options));
    }

    private QueryResult<BaseItem> Page(NextUpQuery query, Func<NextUpQuery, QueryResult<BaseItem>> fetch)
    {
        if (query.SeriesId.HasValue && !query.SeriesId.Value.Equals(Guid.Empty))
        {
            return fetch(query);
        }

        var ordered = Rank(query, fetch).Select(x => x.Episode).ToList();

        var page = ordered.Skip(query.StartIndex ?? 0);
        if (query.Limit > 0)
        {
            page = page.Take(query.Limit.Value);
        }

        return new QueryResult<BaseItem>(query.StartIndex, query.EnableTotalRecordCount ? ordered.Count : 0, page.ToArray());
    }

    private IReadOnlyList<(BaseItem Episode, DateTime Activity)> Rank(NextUpQuery query, Func<NextUpQuery, QueryResult<BaseItem>> fetch)
    {
        // The core drops shows by last play date and pages before returning, both of which would
        // hide shows that only became relevant again through a new episode. Fetch everything and
        // apply the cutoff and the page here.
        var everything = fetch(new NextUpQuery
        {
            User = query.User,
            ParentId = query.ParentId,
            EnableImageTypes = query.EnableImageTypes,
            EnableResumable = query.EnableResumable,
            EnableRewatching = query.EnableRewatching,
            EnableTotalRecordCount = false,
        }).Items;

        var lastPlayedBySeries = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
        return everything
            .Select(episode => (Episode: episode, Activity: LatestActivity(query.User, episode, lastPlayedBySeries)))
            .Where(x => x.Activity >= query.NextUpDateCutoff)
            .OrderByDescending(x => x.Activity)
            .ToList();
    }

    private DateTime LatestActivity(User user, BaseItem episode, Dictionary<string, DateTime?> lastPlayedBySeries)
    {
        var series = (episode as IHasSeries)?.SeriesPresentationUniqueKey;
        DateTime? lastPlayed = null;
        if (!string.IsNullOrEmpty(series) && !lastPlayedBySeries.TryGetValue(series, out lastPlayed))
        {
            lastPlayed = GetLastPlayedDate(user, series);
            lastPlayedBySeries[series] = lastPlayed;
        }

        var added = episode.DateCreated;
        return lastPlayed > added ? lastPlayed.Value : added;
    }

    private DateTime? GetLastPlayedDate(User user, string seriesPresentationUniqueKey)
    {
        var played = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
            IsPlayed = true,
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            Limit = 1,
            DtoOptions = new DtoOptions(false),
        });

        return played.Count == 0 ? null : _userDataManager.GetUserData(user, played[0])?.LastPlayedDate;
    }
}
