namespace TorrentCore.Core.Diagnostics;

public sealed class ActivityLogFilterOptions
{
    public required IReadOnlyList<string> Categories { get; init; }
    public required IReadOnlyList<string> EventTypes { get; init; }
}
