namespace TorrentCore.WebUI2.Client.Components.Tables;

public sealed record OperatorRow(
    string Id,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, IComparable?>? SortValues = null)
{
    public string Value(string key) => Values.TryGetValue(key, out var value) ? value : string.Empty;
}

public sealed record OperatorColumn(
    string Key,
    string Title,
    int DefaultWidth,
    bool IsVisibleByDefault = true,
    bool CanHide = true,
    bool CanSort = true);

public sealed record OperatorSort(string ColumnKey, bool Descending);

public sealed record OperatorCell(OperatorRow Row, OperatorColumn Column);

internal sealed class StoredOperatorTableLayout
{
    public List<string> OrderedColumnKeys { get; set; } = [];
    public Dictionary<string, double> Widths { get; set; } = [];
    public HashSet<string> HiddenColumnKeys { get; set; } = [];
    public List<OperatorSort> Sorts { get; set; } = [];
    public int PageSize { get; set; } = 25;
    public double OverlayWidth { get; set; } = 480;
}

public sealed record OperatorGridLayoutChange(
    IReadOnlyList<string> OrderedColumnKeys,
    IReadOnlyDictionary<string, double> Widths);
