using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentQueuePolicyTests
{
    [Fact]
    public void MixedPriorityQueue_IsGlobal_AndRetainsOrdinaryLanePositions()
    {
        var activeDownload = Item("active", TorrentQueueWorkKind.Download, 1, isActive: true);
        var ordinaryMetadata = Item("ordinary-metadata", TorrentQueueWorkKind.Metadata, 2, isActive: false);
        var priorityMetadata = Item("priority-metadata", TorrentQueueWorkKind.Metadata, 3, isActive: false,
            priorityOrder: 1);
        var ordinaryDownload = Item("ordinary-download", TorrentQueueWorkKind.Download, 4, isActive: false);
        var priorityDownload = Item("priority-download", TorrentQueueWorkKind.Download, 5, isActive: false,
            priorityOrder: 2);

        var result = TorrentQueuePolicy.Evaluate(
            [activeDownload, ordinaryMetadata, priorityMetadata, ordinaryDownload, priorityDownload],
            maxActiveMetadataResolutions: 2,
            maxActiveDownloads: 3
        );

        Assert.Equal(
            [priorityMetadata.Snapshot.TorrentId, priorityDownload.Snapshot.TorrentId],
            result.AdmissionOrder
        );
        Assert.Equal(1, result.Diagnostics[priorityMetadata.Snapshot.TorrentId].PriorityQueuePosition);
        Assert.Equal(2, result.Diagnostics[priorityMetadata.Snapshot.TorrentId].QueuePosition);
        Assert.Equal(2, result.Diagnostics[priorityDownload.Snapshot.TorrentId].PriorityQueuePosition);
        Assert.Equal(2, result.Diagnostics[priorityDownload.Snapshot.TorrentId].QueuePosition);
        Assert.Equal(1, result.Diagnostics[ordinaryMetadata.Snapshot.TorrentId].QueuePosition);
        Assert.Equal(1, result.Diagnostics[ordinaryDownload.Snapshot.TorrentId].QueuePosition);
    }

    [Fact]
    public void FullDownloadSet_DoesNotDisplaceDownloadForPriority()
    {
        var activeDownloads = Enumerable.Range(1, 6)
                                        .Select(index => Item(
                                             $"download-{index}", TorrentQueueWorkKind.Download, index, isActive: true))
                                        .ToArray();
        var priority = Item("priority", TorrentQueueWorkKind.Metadata, 7, isActive: false, priorityOrder: 1);

        var result = TorrentQueuePolicy.Evaluate(
            [..activeDownloads, priority],
            maxActiveMetadataResolutions: 2,
            maxActiveDownloads: 6
        );

        Assert.Empty(result.AdmissionOrder);
        Assert.Empty(result.StopActiveTorrentIds);
        Assert.Null(result.PriorityMetadataDisplacementTorrentId);
        Assert.Equal(TorrentWaitReason.WaitingForMetadataSlot,
            result.Diagnostics[priority.Snapshot.TorrentId].WaitReason);
    }

    [Fact]
    public void PriorityDownload_DisplacesResolverClosestToSliceExpiration()
    {
        var oldestResolver = Item("oldest", TorrentQueueWorkKind.Metadata, 1, isActive: true,
            attemptStartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-9));
        var newerResolver = Item("newer", TorrentQueueWorkKind.Metadata, 2, isActive: true,
            attemptStartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-3));
        var priority = Item("priority", TorrentQueueWorkKind.Download, 3, isActive: false, priorityOrder: 1);

        var result = TorrentQueuePolicy.Evaluate(
            [oldestResolver, newerResolver, priority],
            maxActiveMetadataResolutions: 2,
            maxActiveDownloads: 2
        );

        Assert.Equal(oldestResolver.Snapshot.TorrentId, result.PriorityMetadataDisplacementTorrentId);
        Assert.Contains(oldestResolver.Snapshot.TorrentId, result.StopActiveTorrentIds);
        Assert.Equal([priority.Snapshot.TorrentId], result.AdmissionOrder);
    }

    [Fact]
    public void ActivePriorityResolver_IsProtectedUntilItsAttemptYields()
    {
        var protectedResolver = Item("protected", TorrentQueueWorkKind.Metadata, 1, isActive: true,
            priorityOrder: 1, attemptStartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        var nextPriority = Item("next-priority", TorrentQueueWorkKind.Metadata, 2, isActive: false,
            priorityOrder: 2);
        var ordinaryDownload = Item("ordinary-download", TorrentQueueWorkKind.Download, 3, isActive: false);

        var result = TorrentQueuePolicy.Evaluate(
            [protectedResolver, nextPriority, ordinaryDownload],
            maxActiveMetadataResolutions: 1,
            maxActiveDownloads: 1
        );

        Assert.Empty(result.AdmissionOrder);
        Assert.Empty(result.StopActiveTorrentIds);
        Assert.Equal(1, result.Diagnostics[protectedResolver.Snapshot.TorrentId].PriorityQueuePosition);
        Assert.Equal(2, result.Diagnostics[nextPriority.Snapshot.TorrentId].PriorityQueuePosition);
    }

    [Fact]
    public void HeldWork_ReleasesOnlyAfterNoNonHeldWorkRemainsQueued()
    {
        var ordinary = Item("ordinary", TorrentQueueWorkKind.Download, 1, isActive: false);
        var heldOne = Item("held-one", TorrentQueueWorkKind.Metadata, 2, isActive: false, isHeld: true);
        var heldTwo = Item("held-two", TorrentQueueWorkKind.Download, 3, isActive: false, isHeld: true);

        var blockedRelease = TorrentQueuePolicy.Evaluate(
            [ordinary, heldOne, heldTwo],
            maxActiveMetadataResolutions: 1,
            maxActiveDownloads: 1
        );

        Assert.Empty(blockedRelease.HeldReleaseOrder);
        Assert.Equal(1, blockedRelease.Diagnostics[heldOne.Snapshot.TorrentId].HeldQueuePosition);
        Assert.Equal(2, blockedRelease.Diagnostics[heldTwo.Snapshot.TorrentId].HeldQueuePosition);
        Assert.Equal(TorrentWaitReason.HeldByOperator,
            blockedRelease.Diagnostics[heldOne.Snapshot.TorrentId].WaitReason);

        var releasable = TorrentQueuePolicy.Evaluate(
            [heldOne, heldTwo],
            maxActiveMetadataResolutions: 1,
            maxActiveDownloads: 1
        );

        Assert.Equal(
            [heldOne.Snapshot.TorrentId, heldTwo.Snapshot.TorrentId],
            releasable.HeldReleaseOrder
        );
    }

    [Fact]
    public void OrdinaryAdmission_PreservesExistingDownloadReservationBehavior()
    {
        var downloads = new[]
        {
            Item("download-one", TorrentQueueWorkKind.Download, 1, isActive: false),
            Item("download-two", TorrentQueueWorkKind.Download, 2, isActive: false),
        };
        var metadata = new[]
        {
            Item("metadata-one", TorrentQueueWorkKind.Metadata, 3, isActive: false),
            Item("metadata-two", TorrentQueueWorkKind.Metadata, 4, isActive: false),
            Item("metadata-three", TorrentQueueWorkKind.Metadata, 5, isActive: false),
        };

        var result = TorrentQueuePolicy.Evaluate(
            [..downloads, ..metadata],
            maxActiveMetadataResolutions: 4,
            maxActiveDownloads: 4
        );

        Assert.Equal(
            [
                downloads[0].Snapshot.TorrentId,
                downloads[1].Snapshot.TorrentId,
                metadata[0].Snapshot.TorrentId,
                metadata[1].Snapshot.TorrentId,
            ],
            result.AdmissionOrder
        );
        Assert.Equal(TorrentWaitReason.WaitingForMetadataSlot,
            result.Diagnostics[metadata[2].Snapshot.TorrentId].WaitReason);
    }

    private static TorrentQueuePolicyItem Item(string name, TorrentQueueWorkKind kind, long ordinaryOrder,
        bool isActive, long? priorityOrder = null, bool isHeld = false,
        DateTimeOffset? attemptStartedAtUtc = null)
    {
        var state = isActive
            ? kind == TorrentQueueWorkKind.Metadata ? TorrentState.ResolvingMetadata : TorrentState.Downloading
            : TorrentState.Queued;
        var snapshot = new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = name,
            State = state,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = $"magnet:?xt=urn:btih:{Guid.NewGuid():N}00000000",
            SavePath = $"/tmp/{name}",
            ProgressPercent = 0,
            DownloadedBytes = 0,
            UploadedBytes = 0,
            TotalBytes = kind == TorrentQueueWorkKind.Download ? 1_024 : null,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = DateTimeOffset.UtcNow.AddMinutes(ordinaryOrder),
            OrdinaryQueueOrder = ordinaryOrder,
            PriorityQueueOrder = priorityOrder,
            IsQueueHeld = isHeld,
            MetadataResolutionAttemptStartedAtUtc = attemptStartedAtUtc,
        };
        return new TorrentQueuePolicyItem(snapshot, kind, isActive);
    }
}
