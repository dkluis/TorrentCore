namespace TorrentCore.Contracts.Diagnostics;

public sealed class ActivityLogFilterOptionsDto
{
    public required IReadOnlyList<string> Categories { get; init; }
    public required IReadOnlyList<string> EventTypes { get; init; }
}
