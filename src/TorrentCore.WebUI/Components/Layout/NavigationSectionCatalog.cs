using MudBlazor;

namespace TorrentCore.WebUI.Components.Layout;

public static class NavigationSectionCatalog
{
    public static readonly IReadOnlyList<NavigationSection> All =
    [
        new NavigationSection("Dashboard", "/dashboard", Icons.Material.Filled.Dashboard, MatchAll: false),
        new NavigationSection("Torrents", "/torrents", Icons.Material.Filled.Download),
        new NavigationSection("Logs", "/logs", Icons.Material.Filled.Article),
        new NavigationSection("Settings", "/settings", Icons.Material.Filled.Settings),
        new NavigationSection("Service", "/service-connection", Icons.Material.Filled.Router),
    ];
}
