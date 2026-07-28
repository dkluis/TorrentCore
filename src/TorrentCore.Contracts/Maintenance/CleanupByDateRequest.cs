namespace TorrentCore.Contracts.Maintenance;

public sealed class CleanupByDateRequest
{
    public required DateOnly UpToDate { get; init; }
}
