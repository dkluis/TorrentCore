namespace TorrentCore.Core.History;

// Stored on the ordinary history row; ordinary history cleanup also removes this state.
public sealed record TorrentPayloadRejection(
    Guid TorrentId, string InfoHash, string DownloadRootPath, string[] CandidatePaths,
    string[] OffendingFiles, string Phase = "Removing", DateTimeOffset? NextAttemptAtUtc = null)
{
    public string RejectionReason { get; init; } = "Executable";
    public string[] ObservedFiles { get; init; } = [];
}

public interface ITorrentPayloadRejectionStore
{
    Task<TorrentPayloadRejection?> GetAsync(Guid torrentId, CancellationToken token);
    Task<IReadOnlyList<TorrentPayloadRejection>> ListPendingAsync(CancellationToken token);
    Task<bool> IsRejectedAsync(string infoHash, CancellationToken token);
    Task SaveAsync(TorrentPayloadRejection rejection, CancellationToken token);
}

public sealed class TorrentPayloadRejectedException()
    : Exception("This info hash has a media payload rejection in retained history.");
