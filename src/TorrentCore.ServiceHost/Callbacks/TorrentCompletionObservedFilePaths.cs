namespace TorrentCore.Service.Callbacks;

public sealed class TorrentCompletionObservedFilePaths
{
    public required string  CompletePath   { get; init; }
    public required string  ActivePath     { get; init; }
    public          string? IncompletePath { get; init; }
}
