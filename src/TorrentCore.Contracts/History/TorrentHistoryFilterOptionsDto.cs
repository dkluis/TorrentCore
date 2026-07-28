namespace TorrentCore.Contracts.History;

public sealed class TorrentHistoryFilterOptionsDto
{
    public required IReadOnlyList<string> CategoryKeys { get; init; }
    public required IReadOnlyList<string> States       { get; init; }
}
