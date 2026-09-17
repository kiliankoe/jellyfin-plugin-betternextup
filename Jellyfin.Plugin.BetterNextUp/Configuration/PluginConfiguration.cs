using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BetterNextUp.Configuration;

/// <summary>
/// Plugin configuration. There is no settings page; edit the XML file in the plugin configuration directory.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the comma-separated client name prefixes whose Continue Watching requests get Next Up
    /// merged in. Infuse reports itself as "Infuse-Direct", "Infuse-Library" or "Infuse-Download".
    /// </summary>
    public string ContinueWatchingMergeClients { get; set; } = "Infuse";
}
