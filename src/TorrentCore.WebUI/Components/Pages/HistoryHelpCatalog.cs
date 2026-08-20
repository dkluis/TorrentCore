namespace TorrentCore.WebUI.Components.Pages;

public static class HistoryHelpCatalog
{
    public static readonly SettingHelpContent Filters = new(
        "Filters",
        "Controls which history rows TorrentCore.WebUI requests from the service.",
        "Search sends the current History filter fields to TorrentCore.Service. Date fields apply to the last time each history record changed, for every outcome. Clear resets every filter field and reloads the unfiltered history list."
    );

    public static readonly SettingHelpContent Refresh = new(
        "Refresh",
        "Reloads history rows from the service using the current filter inputs.",
        "Use Refresh when you want a newer history snapshot from TorrentCore.Service without changing the current filter form."
    );

    public static readonly SettingHelpContent Clear = new(
        "Clear Filters",
        "Resets every History filter field and reloads the unfiltered list.",
        "This clears Last Updated From, Last Updated To, Torrent Name, Category, State, and Outcome."
    );

    public static readonly SettingHelpContent FromDate = new(
        "Last Updated From Date",
        "Limits history rows to records last updated on or after this local date.",
        "Use the local yyyy-MM-dd format. TorrentCore applies this as an inclusive start date to every history outcome."
    );

    public static readonly SettingHelpContent ToDate = new(
        "Last Updated To Date",
        "Limits history rows to records last updated on or before this local date.",
        "Use the local yyyy-MM-dd format. TorrentCore applies this as an inclusive end date to every history outcome."
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

    public static readonly SettingHelpContent Outcome = new(
        "Outcome",
        "Filters history by active, removed, or abandoned lifecycle outcome.",
        "Abandoned identifies cold downloads removed by the automatic abandonment policy. Last-updated date filters apply consistently to every outcome."
    );

    public static readonly SettingHelpContent HistoryResults = new(
        "History Results",
        "Shows the current history result set with local sorting and paging.",
        "Search loads rows from the service using the filter form. Rows default to newest Last Updated first; after that, sort from grid headers and page locally in the browser."
    );

    public static readonly SettingHelpContent SelectedHistoryEntry = new(
        "Selected History Entry",
        "Shows the full stored history record for the selected row.",
        "Click a row in the grid to inspect lifecycle timestamps, callback state, stored TVMaze feedback, removal outcome, paths, and the latest recorded torrent summary."
    );
}
