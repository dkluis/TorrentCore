namespace TorrentCore.WebUI.Components.Pages;

public static class LogsHelpCatalog
{
    public static readonly SettingHelpContent Filters = new(
        "Filters",
        "Controls how the WebUI narrows the locally loaded log set.",
        "TorrentCore.WebUI loads the available log rows from the current service endpoint and then applies these filters locally in the page. Refresh reloads the full log set from the service and applies the current filter values. Clear resets the filter inputs and reloads the page with no active narrowing."
    );

    public static readonly SettingHelpContent SearchMessage = new(
        "Search Message",
        "Filters logs by text found in the message or event name.",
        "Use this for quick free-text narrowing when you know part of the log message or event type. Matching is case-insensitive and is applied locally to the rows already loaded into the page."
    );

    public static readonly SettingHelpContent Level = new(
        "Level",
        "Filters logs to one severity level.",
        "Choose a specific level such as Information, Warning, or Error when you want to isolate one class of events. Leaving it on All keeps every level in the current result set."
    );

    public static readonly SettingHelpContent Category = new(
        "Category",
        "Filters logs by category text.",
        "Use this when you want to isolate one service area such as engine activity, callback handling, or API behavior. Matching is case-insensitive and works as a contains filter rather than an exact match."
    );

    public static readonly SettingHelpContent TorrentId = new(
        "Torrent Id",
        "Filters logs to one torrent by GUID.",
        "Paste a torrent GUID when you want to follow one torrent across metadata, download, callback, and cleanup events. Leave it blank for no torrent-specific narrowing. The value must be a valid GUID before Refresh will apply it."
    );

    public static readonly SettingHelpContent FromLocalDateTime = new(
        "From (Local Date/Time)",
        "Applies the inclusive lower time bound for log rows.",
        "Enter a local date/time value to keep only rows at or after that point. TorrentCore.WebUI converts the entered local time to UTC and applies the filter locally after the logs are loaded."
    );

    public static readonly SettingHelpContent ToLocalDateTime = new(
        "To (Local Date/Time)",
        "Applies the inclusive upper time bound for log rows.",
        "Enter a local date/time value to keep only rows at or before that point. TorrentCore.WebUI converts the entered local time to UTC and applies the filter locally after the logs are loaded."
    );

    public static readonly SettingHelpContent Refresh = new(
        "Refresh",
        "Reloads logs from the current service endpoint and applies the current filter values.",
        "Use Refresh after changing filters or when you want a newer copy of the log stream from the service. TorrentCore does not ask the API to pre-filter the logs for this page; the page reloads the available rows and then applies the filters locally."
    );

    public static readonly SettingHelpContent Clear = new(
        "Clear",
        "Resets the filter inputs and reloads the log page without active filters.",
        "Clear removes the current search, level, category, torrent, and date/time inputs, clears the current selection, and then reloads the page so the table returns to the full currently loaded log set."
    );

    public static readonly SettingHelpContent DeleteOrphanedTorrentLogs = new(
        "Delete Orphan Logs",
        "Deletes log rows whose torrent ids no longer exist in TorrentCore's current torrent list.",
        "Use this when torrents were removed before their normal cleanup path and their old log history should be pruned. The service deletes only rows tied to torrent ids that no longer exist in the current torrent store. Service-level logs and logs for still-tracked torrents are kept."
    );

    public static readonly SettingHelpContent RecentActivity = new(
        "Recent Activity",
        "Shows the filtered log table with one pager and header-based sorting.",
        "The table displays the current filtered result set, not just the visible page. Use the column headers to sort, the pager to move through the current results, and row click to send one entry into the details panel below."
    );

    public static readonly SettingHelpContent SelectedLogEntry = new(
        "Selected Log Entry",
        "Shows the full details for the currently selected log row.",
        "Click a row in the table to inspect its IDs, timestamps, category, event, message, and details JSON without leaving the Logs page. This is the compact operator inspection view for one log record."
    );
}
