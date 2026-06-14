namespace TorrentCore.WebUI.State;

public sealed class TorrentsPageState
{
    public string SearchText { get; set; } = string.Empty;
    public string StateFilter { get; set; } = string.Empty;
    public string CategoryFilter { get; set; } = string.Empty;
    public string SortBy { get; set; } = string.Empty;
    public bool SortDescending { get; set; } = true;
    public int PageIndex { get; set; }
    public int PageSize { get; set; } = 25;
    public Guid? SelectedTorrentId { get; set; }
    public bool AutoRefreshEnabled { get; set; } = true;
}
