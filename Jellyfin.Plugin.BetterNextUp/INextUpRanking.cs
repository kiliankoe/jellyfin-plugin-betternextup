using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.BetterNextUp;

/// <summary>
/// Next Up together with the activity date each show was ranked by.
/// </summary>
public interface INextUpRanking
{
    /// <summary>
    /// Ranks Next Up without paging, newest activity first.
    /// </summary>
    /// <param name="query">The next up query. Paging fields are ignored.</param>
    /// <param name="options">The dto options.</param>
    /// <returns>Episodes with the activity date they are ranked by.</returns>
    IReadOnlyList<(BaseItem Episode, DateTime Activity)> Rank(NextUpQuery query, DtoOptions options);
}
