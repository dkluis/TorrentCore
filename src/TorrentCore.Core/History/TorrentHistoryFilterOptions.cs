namespace TorrentCore.Core.History;

public sealed class TorrentHistoryFilterOptions
{
    public required IReadOnlyList<string> CategoryKeys { get; init; }
    public required IReadOnlyList<string> States       { get; init; }
}
