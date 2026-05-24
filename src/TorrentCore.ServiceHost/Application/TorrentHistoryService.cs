#region

using TorrentCore.Core.Torrents;
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
                FinalPayloadPath = null,
                ServiceInstanceIdLastSeen = serviceInstanceContext.ServiceInstanceId,
            },
            cancellationToken);
    }

    public async Task ObserveSnapshotAsync(TorrentSnapshot snapshot, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await torrentHistoryStore.GetAsync(snapshot.TorrentId, cancellationToken);
        if (existing is null)
        {
            var created = CreateFromSnapshot(snapshot, now);
            await torrentHistoryStore.InsertAsync(created, cancellationToken);
            return;
        }

        var updated = MergeSnapshot(existing, snapshot, now);
        if (!HasMeaningfulChanges(existing, updated))
        {
            return;
        }

        updated.LastUpdatedAtUtc = now;
        await torrentHistoryStore.UpdateAsync(updated, cancellationToken);
    }

    private TorrentHistoryRecord CreateFromSnapshot(TorrentSnapshot snapshot, DateTimeOffset now)
    {
        var latestState = snapshot.State.ToString();
        DateTimeOffset? metadataResolvedAtUtc =
                ShouldStampMetadataResolved(null, snapshot) ? snapshot.LastActivityAtUtc ?? now : null;
        DateTimeOffset? downloadStartedAtUtc =
                snapshot.State == Contracts.Torrents.TorrentState.Downloading ? snapshot.LastActivityAtUtc ?? now : null;

        return new TorrentHistoryRecord
        {
            TorrentId = snapshot.TorrentId,
            Name = snapshot.Name,
            MagnetUri = snapshot.MagnetUri,
            InfoHash = snapshot.InfoHash,
            CategoryKey = snapshot.CategoryKey,
            DownloadRootPath = snapshot.DownloadRootPath,
            SavePath = snapshot.SavePath,
            LatestTorrentState = latestState,
            LatestWaitReason = null,
            LatestErrorMessage = snapshot.ErrorMessage,
            LatestProgressPercent = snapshot.ProgressPercent,
            LatestDownloadedBytes = snapshot.DownloadedBytes,
            LatestUploadedBytes = snapshot.UploadedBytes,
            LatestTotalBytes = snapshot.TotalBytes,
            LatestDownloadRateBytesPerSecond = snapshot.DownloadRateBytesPerSecond,
            LatestUploadRateBytesPerSecond = snapshot.UploadRateBytesPerSecond,
            LatestTrackerCount = snapshot.TrackerCount,
            LatestConnectedPeerCount = snapshot.ConnectedPeerCount,
            SubmittedAtUtc = snapshot.AddedAtUtc,
            MetadataResolvedAtUtc = metadataResolvedAtUtc,
            DownloadStartedAtUtc = downloadStartedAtUtc,
            DownloadCompletedAtUtc = snapshot.CompletedAtUtc,
            SeedingStartedAtUtc = snapshot.SeedingStartedAtUtc,
            LastActivityAtUtc = snapshot.LastActivityAtUtc,
            LastUpdatedAtUtc = now,
            RemovedAtUtc = null,
            InvokeCompletionCallback = snapshot.InvokeCompletionCallback,
            CompletionCallbackLabel = snapshot.CompletionCallbackLabel,
            LatestCallbackStatus = snapshot.CompletionCallbackState?.ToString(),
            CallbackStartedAtUtc = snapshot.CompletionCallbackPendingSinceUtc,
            CallbackCompletedAtUtc = snapshot.CompletionCallbackInvokedAtUtc,
            CallbackLastError = snapshot.CompletionCallbackLastError,
            DataDeleted = false,
            RemovalReason = null,
            RemovedByCleanupPolicy = false,
            FinalPayloadPath = null,
            ServiceInstanceIdLastSeen = serviceInstanceContext.ServiceInstanceId,
        };
    }

    private TorrentHistoryRecord MergeSnapshot(TorrentHistoryRecord existing, TorrentSnapshot snapshot, DateTimeOffset now)
    {
        var updated = Clone(existing);
        var latestState = snapshot.State.ToString();

        updated.Name = snapshot.Name;
        updated.MagnetUri = snapshot.MagnetUri;
        updated.InfoHash = snapshot.InfoHash;
        updated.CategoryKey = snapshot.CategoryKey;
        updated.DownloadRootPath = snapshot.DownloadRootPath;
        updated.SavePath = snapshot.SavePath;
        updated.LatestTorrentState = latestState;
        updated.LatestErrorMessage = snapshot.ErrorMessage;
        updated.LatestProgressPercent = snapshot.ProgressPercent;
        updated.LatestDownloadedBytes = snapshot.DownloadedBytes;
        updated.LatestUploadedBytes = snapshot.UploadedBytes;
        updated.LatestTotalBytes = snapshot.TotalBytes;
        updated.LatestDownloadRateBytesPerSecond = snapshot.DownloadRateBytesPerSecond;
        updated.LatestUploadRateBytesPerSecond = snapshot.UploadRateBytesPerSecond;
        updated.LatestTrackerCount = snapshot.TrackerCount;
        updated.LatestConnectedPeerCount = snapshot.ConnectedPeerCount;
        updated.LastActivityAtUtc = snapshot.LastActivityAtUtc;
        updated.InvokeCompletionCallback = snapshot.InvokeCompletionCallback;
        updated.CompletionCallbackLabel = snapshot.CompletionCallbackLabel;
        updated.LatestCallbackStatus = snapshot.CompletionCallbackState?.ToString();
        updated.ServiceInstanceIdLastSeen = serviceInstanceContext.ServiceInstanceId;

        var snapshotPendingSinceUtc = snapshot.CompletionCallbackPendingSinceUtc;
        var snapshotInvokedAtUtc = snapshot.CompletionCallbackInvokedAtUtc;
        var snapshotCallbackStatus = snapshot.CompletionCallbackState;

        if (IsRetryTransition(existing, snapshot))
        {
            updated.CallbackStartedAtUtc = snapshotPendingSinceUtc;
            updated.CallbackCompletedAtUtc = null;
            updated.CallbackLastError = snapshot.CompletionCallbackLastError;
        }
        else
        {
            updated.CallbackStartedAtUtc ??= snapshotPendingSinceUtc;
            if (updated.CallbackStartedAtUtc is null &&
                snapshotCallbackStatus == TorrentCompletionCallbackState.PendingFinalization)
            {
                updated.CallbackStartedAtUtc = snapshot.LastActivityAtUtc ?? now;
            }

            updated.CallbackCompletedAtUtc ??= snapshotInvokedAtUtc;
            if (updated.CallbackCompletedAtUtc is null &&
                IsTerminalCallbackStatus(snapshotCallbackStatus))
            {
                updated.CallbackCompletedAtUtc = snapshot.LastActivityAtUtc ?? now;
            }

            updated.CallbackLastError = snapshot.CompletionCallbackLastError;
        }

        if (updated.MetadataResolvedAtUtc is null && ShouldStampMetadataResolved(existing, snapshot))
        {
            updated.MetadataResolvedAtUtc = snapshot.LastActivityAtUtc ?? now;
        }

        if (updated.DownloadStartedAtUtc is null && snapshot.State == Contracts.Torrents.TorrentState.Downloading)
        {
            updated.DownloadStartedAtUtc = snapshot.LastActivityAtUtc ?? now;
        }

        updated.DownloadCompletedAtUtc ??= snapshot.CompletedAtUtc;
        updated.SeedingStartedAtUtc ??= snapshot.SeedingStartedAtUtc;

        return updated;
    }

    private static bool ShouldStampMetadataResolved(TorrentHistoryRecord? existing, TorrentSnapshot snapshot)
    {
        if (snapshot.State == Contracts.Torrents.TorrentState.ResolvingMetadata)
        {
            return false;
        }

        if (existing is null)
        {
            return snapshot.TotalBytes is not null || !string.IsNullOrWhiteSpace(snapshot.InfoHash);
        }

        return string.Equals(existing.LatestTorrentState, Contracts.Torrents.TorrentState.ResolvingMetadata.ToString(), StringComparison.Ordinal) &&
               snapshot.State != Contracts.Torrents.TorrentState.ResolvingMetadata;
    }

    private static bool HasMeaningfulChanges(TorrentHistoryRecord existing, TorrentHistoryRecord updated)
    {
        return
            existing.Name != updated.Name ||
            existing.MagnetUri != updated.MagnetUri ||
            existing.InfoHash != updated.InfoHash ||
            existing.CategoryKey != updated.CategoryKey ||
            existing.DownloadRootPath != updated.DownloadRootPath ||
            existing.SavePath != updated.SavePath ||
            existing.LatestTorrentState != updated.LatestTorrentState ||
            existing.LatestWaitReason != updated.LatestWaitReason ||
            existing.LatestErrorMessage != updated.LatestErrorMessage ||
            existing.LatestProgressPercent != updated.LatestProgressPercent ||
            existing.LatestDownloadedBytes != updated.LatestDownloadedBytes ||
            existing.LatestUploadedBytes != updated.LatestUploadedBytes ||
            existing.LatestTotalBytes != updated.LatestTotalBytes ||
            existing.LatestDownloadRateBytesPerSecond != updated.LatestDownloadRateBytesPerSecond ||
            existing.LatestUploadRateBytesPerSecond != updated.LatestUploadRateBytesPerSecond ||
            existing.LatestTrackerCount != updated.LatestTrackerCount ||
            existing.LatestConnectedPeerCount != updated.LatestConnectedPeerCount ||
            existing.MetadataResolvedAtUtc != updated.MetadataResolvedAtUtc ||
            existing.DownloadStartedAtUtc != updated.DownloadStartedAtUtc ||
            existing.DownloadCompletedAtUtc != updated.DownloadCompletedAtUtc ||
            existing.SeedingStartedAtUtc != updated.SeedingStartedAtUtc ||
            existing.LastActivityAtUtc != updated.LastActivityAtUtc ||
            existing.InvokeCompletionCallback != updated.InvokeCompletionCallback ||
            existing.CompletionCallbackLabel != updated.CompletionCallbackLabel ||
            existing.LatestCallbackStatus != updated.LatestCallbackStatus ||
            existing.CallbackStartedAtUtc != updated.CallbackStartedAtUtc ||
            existing.CallbackCompletedAtUtc != updated.CallbackCompletedAtUtc ||
            existing.CallbackLastError != updated.CallbackLastError ||
            existing.ServiceInstanceIdLastSeen != updated.ServiceInstanceIdLastSeen;
    }

    private static bool IsRetryTransition(TorrentHistoryRecord existing, TorrentSnapshot snapshot)
    {
        return snapshot.CompletionCallbackState == TorrentCompletionCallbackState.PendingFinalization &&
               existing.LatestCallbackStatus is nameof(TorrentCompletionCallbackState.Failed) or nameof(TorrentCompletionCallbackState.TimedOut);
    }

    private static bool IsTerminalCallbackStatus(TorrentCompletionCallbackState? status)
    {
        return status is TorrentCompletionCallbackState.Invoked or TorrentCompletionCallbackState.Failed or TorrentCompletionCallbackState.TimedOut;
    }

    private static TorrentHistoryRecord Clone(TorrentHistoryRecord source)
    {
        return new TorrentHistoryRecord
        {
            TorrentId = source.TorrentId,
            Name = source.Name,
            MagnetUri = source.MagnetUri,
            InfoHash = source.InfoHash,
            CategoryKey = source.CategoryKey,
            DownloadRootPath = source.DownloadRootPath,
            SavePath = source.SavePath,
            LatestTorrentState = source.LatestTorrentState,
            LatestWaitReason = source.LatestWaitReason,
            LatestErrorMessage = source.LatestErrorMessage,
            LatestProgressPercent = source.LatestProgressPercent,
            LatestDownloadedBytes = source.LatestDownloadedBytes,
            LatestUploadedBytes = source.LatestUploadedBytes,
            LatestTotalBytes = source.LatestTotalBytes,
            LatestDownloadRateBytesPerSecond = source.LatestDownloadRateBytesPerSecond,
            LatestUploadRateBytesPerSecond = source.LatestUploadRateBytesPerSecond,
            LatestTrackerCount = source.LatestTrackerCount,
            LatestConnectedPeerCount = source.LatestConnectedPeerCount,
            SubmittedAtUtc = source.SubmittedAtUtc,
            MetadataResolvedAtUtc = source.MetadataResolvedAtUtc,
            DownloadStartedAtUtc = source.DownloadStartedAtUtc,
            DownloadCompletedAtUtc = source.DownloadCompletedAtUtc,
            SeedingStartedAtUtc = source.SeedingStartedAtUtc,
            LastActivityAtUtc = source.LastActivityAtUtc,
            LastUpdatedAtUtc = source.LastUpdatedAtUtc,
            RemovedAtUtc = source.RemovedAtUtc,
            InvokeCompletionCallback = source.InvokeCompletionCallback,
            CompletionCallbackLabel = source.CompletionCallbackLabel,
            LatestCallbackStatus = source.LatestCallbackStatus,
            CallbackStartedAtUtc = source.CallbackStartedAtUtc,
            CallbackCompletedAtUtc = source.CallbackCompletedAtUtc,
            CallbackLastError = source.CallbackLastError,
            DataDeleted = source.DataDeleted,
            RemovalReason = source.RemovalReason,
            RemovedByCleanupPolicy = source.RemovedByCleanupPolicy,
            FinalPayloadPath = source.FinalPayloadPath,
            ServiceInstanceIdLastSeen = source.ServiceInstanceIdLastSeen,
        };
    }
}
