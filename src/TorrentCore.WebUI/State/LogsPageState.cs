namespace TorrentCore.WebUI.State;

public sealed class LogsPageState
{
    public string SearchText { get; set; } = string.Empty;
    public string LevelFilter { get; set; } = string.Empty;
    public string CategoryFilter { get; set; } = string.Empty;
    public string TorrentIdFilter { get; set; } = string.Empty;
    public string FromLocalFilter { get; set; } = string.Empty;
    public string ToLocalFilter { get; set; } = string.Empty;
    public string SortBy { get; set; } = string.Empty;
    public bool SortDescending { get; set; } = true;
    public int PageIndex { get; set; }
    public int PageSize { get; set; } = 25;
    public long? SelectedLogEntryId { get; set; }
}
