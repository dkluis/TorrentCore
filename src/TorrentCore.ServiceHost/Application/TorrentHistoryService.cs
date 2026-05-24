#region

using TorrentCore.Core.History;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Application;

public sealed class TorrentHistoryService(ITorrentHistoryStore torrentHistoryStore,
    ServiceInstanceContext serviceInstanceContext) : ITorrentHistoryService
{
    public Task CreateOnAddAsync(TorrentCore.Contracts.Torrents.TorrentDetailDto torrent,
        ResolvedTorrentCategorySelection categorySelection, CancellationToken cancellationToken)
    {
        var submittedAtUtc = torrent.AddedAtUtc;

        return torrentHistoryStore.InsertAsync(
            new TorrentHistoryRecord
            {
                TorrentId = torrent.TorrentId,
                Name = torrent.Name,
                MagnetUri = torrent.MagnetUri,
                InfoHash = torrent.InfoHash,
                CategoryKey = torrent.CategoryKey,
                DownloadRootPath = categorySelection.DownloadRootPath,
                SavePath = torrent.SavePath,
                LatestTorrentState = torrent.State.ToString(),
                LatestWaitReason = torrent.WaitReason?.ToString(),
                LatestErrorMessage = torrent.ErrorMessage,
                LatestProgressPercent = torrent.ProgressPercent,
                LatestDownloadedBytes = torrent.DownloadedBytes,
                LatestUploadedBytes = 0,
                LatestTotalBytes = torrent.TotalBytes,
                LatestDownloadRateBytesPerSecond = torrent.DownloadRateBytesPerSecond,
                LatestUploadRateBytesPerSecond = torrent.UploadRateBytesPerSecond,
                LatestTrackerCount = torrent.TrackerCount,
                LatestConnectedPeerCount = torrent.ConnectedPeerCount,
                SubmittedAtUtc = submittedAtUtc,
                MetadataResolvedAtUtc = torrent.InfoHash is not null && torrent.TotalBytes is not null ? submittedAtUtc : null,
                DownloadStartedAtUtc = null,
                DownloadCompletedAtUtc = torrent.CompletedAtUtc,
                SeedingStartedAtUtc = null,
                LastActivityAtUtc = torrent.LastActivityAtUtc,
                LastUpdatedAtUtc = submittedAtUtc,
                RemovedAtUtc = null,
                InvokeCompletionCallback = categorySelection.InvokeCompletionCallback,
                CompletionCallbackLabel = categorySelection.CompletionCallbackLabel,
                LatestCallbackStatus = torrent.CompletionCallbackState,
                CallbackStartedAtUtc = torrent.CompletionCallbackPendingSinceUtc,
                CallbackCompletedAtUtc = torrent.CompletionCallbackInvokedAtUtc,
                CallbackLastError = torrent.CompletionCallbackLastError,
                DataDeleted = false,
                RemovalReason = null,
                RemovedByCleanupPolicy = false,
                FinalPayloadPath = torrent.CompletionCallbackFinalPayloadPath,
                ServiceInstanceIdLastSeen = serviceInstanceContext.ServiceInstanceId,
            },
            cancellationToken);
    }
}
