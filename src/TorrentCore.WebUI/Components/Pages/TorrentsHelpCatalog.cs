namespace TorrentCore.WebUI.Components.Pages;

public static class TorrentsHelpCatalog
{
    public static readonly SettingHelpContent Filters = new(
        "Filters",
        "Controls how the WebUI narrows the locally loaded torrent list.",
        "TorrentCore.WebUI loads the current torrent list from the service and then applies these filters locally in the page. Refresh reloads the list from the service. Clear resets the filter inputs without removing any torrents from TorrentCore."
    );

    public static readonly SettingHelpContent Refresh = new(
        "Refresh",
        "Reloads the current torrent list from the service.",
        "Use Refresh when you want a newer snapshot of torrent state from TorrentCore.Service. The page keeps the current local filters and reapplies them after the fresh list is loaded."
    );

    public static readonly SettingHelpContent Clear = new(
        "Clear",
        "Resets the local torrent filters.",
        "Clear removes the current Name, State, and Category filters and leaves the page showing the full current torrent list from the active service endpoint."
    );

    public static readonly SettingHelpContent Name = new(
        "Name",
        "Filters torrents by name text.",
        "Use this for quick free-text narrowing when you know part of the torrent name. Matching is case-insensitive and is applied locally to the currently loaded torrent list."
    );

    public static readonly SettingHelpContent State = new(
        "State",
        "Filters torrents by TorrentCore lifecycle state.",
        """
        Use this when you want to isolate one TorrentCore state. The states mean:
        ResolvingMetadata: the magnet is active and TorrentCore is still trying to obtain metadata and initial peers.
        Queued: the torrent is known but waiting for a metadata or download slot before active work starts.
        Downloading: metadata is resolved and TorrentCore is actively transferring payload data.
        Seeding: payload download is complete and TorrentCore is still uploading according to the current seeding policy.
        Paused: operator action or policy has stopped active transfer work until the torrent is resumed.
        Completed: TorrentCore considers the transfer lifecycle complete and no further transfer work is currently required.
        Error: TorrentCore hit a failure condition that blocks normal progress until the issue is cleared or the torrent is retried.
        Removed: historical terminal state used for removal handling and not normally expected to remain visible in the current torrents grid.
        """
    );

    public static readonly SettingHelpContent Category = new(
        "Category",
        "Filters torrents by the stored TorrentCore category key.",
        "Use this to isolate one routing category such as TV or Movie, or choose Uncategorized to show torrents without a category assignment. Filtering is local to the currently loaded torrent list."
    );

    public static readonly SettingHelpContent AutoRefresh = new(
        "Auto Refresh (5s)",
        "Turns the 5-second automatic torrent-list refresh loop on or off.",
        "When enabled, the Torrents page reloads the current list every 5 seconds as long as the page is idle enough to do so safely. While actions or detail loads are in progress, TorrentCore.WebUI intentionally avoids stepping on the active operator workflow."
    );

    public static readonly SettingHelpContent CurrentTorrents = new(
        "Current Torrents",
        "Shows the filtered torrent grid with header-based sorting and one pager.",
        "Use the grid to scan current torrent state, sort by clicking a column header, page through the current results, and select one torrent for deeper detail and actions below."
    );

    public static readonly SettingHelpContent SelectedTorrent = new(
        "Selected Torrent",
        "Shows full details and available actions for the selected torrent.",
        "Click one row in the grid to open the selected-torrent panel. This section is the operator workspace for one torrent's state, transfer details, callback status, and action buttons."
    );

    public static readonly SettingHelpContent Pause = new(
        "Pause",
        "Stops active transfer work for the selected torrent.",
        "Pause tells TorrentCore to stop active transfer activity for this torrent while keeping it in tracking state so it can be resumed later."
    );

    public static readonly SettingHelpContent Resume = new(
        "Resume",
        "Restarts active transfer work for a paused torrent.",
        "Resume tells TorrentCore to place the selected torrent back into its normal queue/transfer flow so it can continue resolving metadata, downloading, or seeding as appropriate."
    );

    public static readonly SettingHelpContent RefreshMetadata = new(
        "Refresh Metadata",
        "Requests a non-destructive metadata and peer discovery refresh for the selected torrent.",
        "Use this when a magnet or weak swarm looks stale. TorrentCore asks the engine to refresh discovery paths such as DHT and tracker announces without fully rebuilding the torrent session."
    );

    public static readonly SettingHelpContent ResetMetadata = new(
        "Reset Metadata",
        "Recreates the metadata discovery session for the selected torrent.",
        "Use this when a torrent appears stuck and a normal metadata refresh was not enough. This is the stronger recovery step that rebuilds the metadata-discovery session rather than only nudging discovery."
    );

    public static readonly SettingHelpContent RetryCallback = new(
        "Retry Callback",
        "Requeues the completion callback path for a torrent whose callback did not finish successfully.",
        "Use this when the torrent payload is ready but the external completion callback failed or timed out. TorrentCore places the selected torrent back into callback processing instead of forcing a full re-download."
    );

    public static readonly SettingHelpContent LogDetails = new(
        "Log Details",
        "Navigates to the Logs page with the selected torrent id prefilled.",
        "Use this to inspect the full activity log history for the selected torrent without manually copying its GUID into the Logs filters."
    );

    public static readonly SettingHelpContent Peers = new(
        "Peers",
        "Opens the live peer diagnostics dialog for the selected torrent.",
        "Use this to inspect the current peer list, client identities, direction, seeder status, and live transfer rates for the selected torrent."
    );

    public static readonly SettingHelpContent Trackers = new(
        "Trackers",
        "Opens the tracker diagnostics dialog for the selected torrent.",
        "Use this to inspect tracker tiers, active tracker status, announce/scrape results, and tracker failure diagnostics for the selected torrent."
    );

    public static readonly SettingHelpContent Remove = new(
        "Remove",
        "Removes the selected torrent from TorrentCore tracking without deleting downloaded data.",
        "Use this when the torrent should leave TorrentCore's current list but the on-disk payload should remain in place. The torrent is removed from tracking and will no longer appear in the current torrents grid."
    );

    public static readonly SettingHelpContent DeleteData = new(
        "Delete Data",
        "Removes the selected torrent from TorrentCore tracking and deletes its payload data.",
        "Use this only when you want TorrentCore to remove both the torrent record and its downloaded files from disk. This is the destructive removal path."
    );
}
