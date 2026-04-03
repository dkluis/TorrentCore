namespace TorrentCore.WebUI.State;

public sealed class StandardListPageState
{
    public string SearchText { get; set; } = string.Empty;
    public string PrimaryFilter { get; set; } = string.Empty;
    public string SecondaryFilter { get; set; } = string.Empty;
    public string SortBy { get; set; } = string.Empty;
    public bool SortDescending { get; set; } = true;
    public int PageIndex { get; set; }
    public int PageSize { get; set; } = 25;
}
