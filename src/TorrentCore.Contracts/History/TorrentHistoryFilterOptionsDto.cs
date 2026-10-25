namespace TorrentCore.Contracts.History;

public sealed class TorrentHistoryFilterOptionsDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RejectionReasons { get; init; }
    public required IReadOnlyList<string> CategoryKeys { get; init; }
    public required IReadOnlyList<string> States       { get; init; }
}
