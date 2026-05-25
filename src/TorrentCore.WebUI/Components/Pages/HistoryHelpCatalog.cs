namespace TorrentCore.WebUI.Components.Pages;

public static class HistoryHelpCatalog
{
    public static readonly SettingHelpContent Filters = new(
        "Filters",
        "Controls which history rows TorrentCore.WebUI requests from the service.",
        "Search sends the current History filter fields to TorrentCore.Service. Clear resets every filter field, reloads the unfiltered history list, and keeps sorting local to the grid."
    );

    public static readonly SettingHelpContent Refresh = new(
        "Refresh",
        "Reloads history rows from the service using the current filter inputs.",
        "Use Refresh when you want a newer history snapshot from TorrentCore.Service without changing the current filter form."
    );

    public static readonly SettingHelpContent Clear = new(
        "Clear Filters",
        "Resets every History filter field and reloads the unfiltered list.",
        "This clears From Date, To Date, Torrent Name, Category, State, and Removed so the page returns to the default full history view."
    );

    public static readonly SettingHelpContent FromDate = new(
        "From Date",
        "Limits history rows to torrents submitted on or after this local date.",
        "Use the local yyyy-MM-dd format. TorrentCore applies this as an inclusive start date."
    );

    public static readonly SettingHelpContent ToDate = new(
        "To Date",
        "Limits history rows to torrents submitted on or before this local date.",
        "Use the local yyyy-MM-dd format. TorrentCore applies this as an inclusive end date."
    );

    public static readonly SettingHelpContent TorrentName = new(
        "Torrent Name",
        "Filters history rows by torrent name text.",
        "Matching is case-insensitive and uses contains semantics, so partial text is enough."
    );

    public static readonly SettingHelpContent Category = new(
        "Category",
        "Filters history rows by TorrentCore category text.",
        "Matching is case-insensitive and uses contains semantics. Leave it blank to include every category."
    );

    public static readonly SettingHelpContent State = new(
        "State",
        "Filters history rows by TorrentCore lifecycle state text.",
        "Matching is case-insensitive and uses contains semantics, so values like seed, down, or pause are valid."
    );

    public static readonly SettingHelpContent Removed = new(
        "Removed",
        "Controls whether the list includes active history rows, removed rows, or both.",
        "All leaves the removal state unfiltered. Active Only shows rows still tracked as active. Removed Only shows rows with a recorded removal timestamp."
    );

    public static readonly SettingHelpContent HistoryResults = new(
        "History Results",
        "Shows the current history result set with local sorting and paging.",
        "Search loads rows from the service using the filter form. After that, sort from grid headers and page locally in the browser."
    );

    public static readonly SettingHelpContent SelectedHistoryEntry = new(
        "Selected History Entry",
        "Shows the full stored history record for the selected row.",
        "Click a row in the grid to inspect lifecycle timestamps, callback state, removal outcome, paths, and the latest recorded torrent summary."
    );
}
