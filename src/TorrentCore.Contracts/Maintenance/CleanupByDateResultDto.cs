namespace TorrentCore.Contracts.Maintenance;

public sealed class CleanupByDateResultDto
{
    public required DateOnly       UpToDate          { get; init; }
    public required DateTimeOffset CutoffUtc         { get; init; }
    public required int            DeletedRecordCount { get; init; }
}
