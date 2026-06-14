namespace TorrentCore.WebUI.State;

public sealed class HistoryPageState
{
    public string TorrentName { get; set; } = string.Empty;
    public string CategoryKey { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string RemovedFilter { get; set; } = string.Empty;
    public string FromDate { get; set; } = string.Empty;
    public string ToDate { get; set; } = string.Empty;
    public string SortBy { get; set; } = string.Empty;
    public bool SortDescending { get; set; } = true;
    public int PageIndex { get; set; }
    public Guid? SelectedTorrentId { get; set; }
    public int PageSize { get; set; } = 25;
}
