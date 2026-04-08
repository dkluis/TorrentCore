#region

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Application;
using TorrentCore.Service.Callbacks;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Engine;

public sealed class PersistedTorrentEngineAdapter(ITorrentStateStore torrentStateStore,
    IRuntimeSettingsService runtimeSettingsService, ITorrentCompletionFinalizationChecker finalizationChecker,
    ResolvedTorrentCoreServicePaths servicePaths) : ITorrentEngineAdapter
{
    public Task<int> GetTorrentCountAsync(CancellationToken cancellationToken)
    {
        return torrentStateStore.CountAsync(cancellationToken);
    }

    public async Task<TorrentEngineRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        var now      = DateTimeOffset.UtcNow;
        var torrents = await torrentStateStore.ListAsync(cancellationToken);
        var changes  = new List<TorrentRecoveryChange>();

        foreach (var torrent in torrents)
        {
            var previousState   = torrent.State;
            var normalizedState = NormalizeStateForStartup(previousState);
            var requiresUpdate  = false;

            if (previousState != normalizedState)
            {
                torrent.State  = normalizedState;
                requiresUpdate = true;

                changes.Add(
                    new TorrentRecoveryChange
                    {
                        TorrentId     = torrent.TorrentId,
                        Name          = torrent.Name,
                        PreviousState = previousState,
                        CurrentState  = normalizedState,
                    }
                );
            }

            if (torrent.DownloadRateBytesPerSecond != 0)
            {
                torrent.DownloadRateBytesPerSecond = 0;
                requiresUpdate                     = true;
            }

            if (torrent.UploadRateBytesPerSecond != 0)
            {
                torrent.UploadRateBytesPerSecond = 0;
                requiresUpdate                   = true;
            }

            if (torrent.ConnectedPeerCount != 0)
            {
                torrent.ConnectedPeerCount = 0;
                requiresUpdate             = true;
            }

            if (requiresUpdate)
            {
                torrent.LastActivityAtUtc = now;
                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            }
        }

        return new TorrentEngineRecoveryResult
        {
            RecoveredTorrentCount  = torrents.Count,
            NormalizedTorrentCount = changes.Count,
            CompletedAtUtc         = now,
            Changes                = changes,
        };
    }

    public Task SynchronizeAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }

    public async Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        var torrents        = await torrentStateStore.ListAsync(cancellationToken);
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var diagnostics     = TorrentQueueDiagnostics.Create(torrents, runtimeSettings);
        return torrents.Select(snapshot => MapSummary(snapshot, diagnostics[snapshot.TorrentId])).ToArray();
    }

    public async Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrents = await torrentStateStore.ListAsync(cancellationToken);
        var torrent  = torrents.SingleOrDefault(snapshot => snapshot.TorrentId == torrentId);
        return torrent is null ?
                throw new ServiceOperationException(
                    "torrent_not_found", $"Torrent '{torrentId}' was not found.", StatusCodes.Status404NotFound,
                    nameof(torrentId)
                ) : MapDetail(
                    torrent,
                    TorrentQueueDiagnostics.Create(
                        torrents, await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken)
                    )[torrent.TorrentId], await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken)
                );
    }

    public async Task<IReadOnlyList<TorrentPeerDto>> GetTorrentPeersAsync(Guid torrentId,
        CancellationToken                                                      cancellationToken)
    {
        _ = await GetRequiredSnapshotAsync(torrentId, cancellationToken);
        return Array.Empty<TorrentPeerDto>();
    }

    public async Task<IReadOnlyList<TorrentTrackerDto>> GetTorrentTrackersAsync(Guid torrentId,
        CancellationToken                                                         cancellationToken)
    {
        _ = await GetRequiredSnapshotAsync(torrentId, cancellationToken);
        return Array.Empty<TorrentTrackerDto>();
    }

    public async Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request,
        ResolvedTorrentCategorySelection categorySelection, CancellationToken cancellationToken)
    {
        var magnet = ParseMagnet(request.MagnetUri);

        if (await torrentStateStore.ExistsByInfoHashAsync(magnet.InfoHash, cancellationToken))
        {
            throw new ServiceOperationException(
                "duplicate_magnet", "A torrent with the same info hash already exists on this host.",
                StatusCodes.Status409Conflict, nameof(request.MagnetUri)
            );
        }

        var now = DateTimeOffset.UtcNow;
        var torrent = new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = magnet.DisplayName,
            CategoryKey = categorySelection.CategoryKey,
            CompletionCallbackLabel = categorySelection.CompletionCallbackLabel,
            InvokeCompletionCallback = categorySelection.InvokeCompletionCallback,
            CompletionCallbackState = null,
            CompletionCallbackPendingSinceUtc = null,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            MagnetUri = request.MagnetUri.Trim(),
            InfoHash = magnet.InfoHash,
            DownloadRootPath = categorySelection.DownloadRootPath,
            SavePath = Path.Combine(categorySelection.DownloadRootPath, SanitizePathSegment(magnet.DisplayName)),
            State = TorrentState.ResolvingMetadata,
            DesiredState = TorrentDesiredState.Runnable,
            ProgressPercent = 0,
            DownloadedBytes = 0,
            UploadedBytes = 0,
            TotalBytes = null,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = now,
            LastActivityAtUtc = now,
        };

        await torrentStateStore.InsertAsync(torrent, cancellationToken);
        return MapDetail(torrent, new TorrentQueueDiagnostic(null, null), null);
    }

    public async Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);

        if (!CanPause(torrent.State))
        {
            throw new ServiceOperationException(
                "invalid_state", $"Torrent '{torrent.Name}' cannot be paused while in state '{torrent.State}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        torrent.DesiredState               = TorrentDesiredState.Paused;
        torrent.State                      = TorrentState.Paused;
        torrent.DownloadRateBytesPerSecond = 0;
        torrent.UploadRateBytesPerSecond   = 0;
        torrent.LastActivityAtUtc          = DateTimeOffset.UtcNow;

        await torrentStateStore.UpdateAsync(torrent, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId      = torrent.TorrentId,
            Action         = "pause",
            State          = torrent.State,
            ProcessedAtUtc = torrent.LastActivityAtUtc.Value,
            DataDeleted    = false,
        };
    }

    public async Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);

        if (!CanResume(torrent.State))
        {
            throw new ServiceOperationException(
                "invalid_state", $"Torrent '{torrent.Name}' cannot be resumed while in state '{torrent.State}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        torrent.DesiredState = TorrentDesiredState.Runnable;
        torrent.State = torrent.ProgressPercent >= 100 ? TorrentState.Completed : TorrentState.Queued;
        torrent.DownloadRateBytesPerSecond = 0;
        torrent.UploadRateBytesPerSecond = 0;
        torrent.ConnectedPeerCount = 0;
        torrent.LastActivityAtUtc = DateTimeOffset.UtcNow;

        await torrentStateStore.UpdateAsync(torrent, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId      = torrent.TorrentId,
            Action         = "resume",
            State          = torrent.State,
            ProcessedAtUtc = torrent.LastActivityAtUtc.Value,
            DataDeleted    = false,
        };
    }

    public async Task<TorrentActionResultDto> RefreshMetadataAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);

        if (!CanRefreshMetadata(torrent.State))
        {
            throw new ServiceOperationException(
                "invalid_state", $"Torrent '{torrent.Name}' cannot refresh metadata while in state '{torrent.State}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        torrent.LastActivityAtUtc = DateTimeOffset.UtcNow;
        torrent.ErrorMessage      = null;

        await torrentStateStore.UpdateAsync(torrent, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId      = torrent.TorrentId,
            Action         = "refresh_metadata",
            State          = torrent.State,
            ProcessedAtUtc = torrent.LastActivityAtUtc.Value,
            DataDeleted    = false,
        };
    }

    public async Task<TorrentActionResultDto> ResetMetadataSessionAsync(Guid torrentId,
        CancellationToken                                                    cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);

        if (!CanRefreshMetadata(torrent.State))
        {
            throw new ServiceOperationException(
                "invalid_state", $"Torrent '{torrent.Name}' cannot reset metadata while in state '{torrent.State}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        torrent.LastActivityAtUtc = DateTimeOffset.UtcNow;
        torrent.ErrorMessage      = null;

        await torrentStateStore.UpdateAsync(torrent, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId      = torrent.TorrentId,
            Action         = "reset_metadata_session",
            State          = torrent.State,
            ProcessedAtUtc = torrent.LastActivityAtUtc.Value,
            DataDeleted    = false,
        };
    }

    public async Task<TorrentActionResultDto> RetryCompletionCallbackAsync(Guid torrentId,
        CancellationToken                                                       cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);

        if (!CanRetryCompletionCallback(torrent.CompletionCallbackState))
        {
            throw new ServiceOperationException(
                "invalid_callback_state",
                $"Completion callback for torrent '{torrent.Name}' cannot be retried while in state '{torrent.CompletionCallbackState?.ToString() ?? "None"}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        var now = DateTimeOffset.UtcNow;
        torrent.CompletionCallbackState           = TorrentCompletionCallbackState.PendingFinalization;
        torrent.CompletionCallbackPendingSinceUtc = now;
        torrent.CompletionCallbackInvokedAtUtc    = null;
        torrent.CompletionCallbackLastError       = null;
        torrent.LastActivityAtUtc                 = now;

        await torrentStateStore.UpdateAsync(torrent, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId      = torrent.TorrentId,
            Action         = "retry_completion_callback",
            State          = torrent.State,
            ProcessedAtUtc = now,
            DataDeleted    = false,
        };
    }

    public async Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request,
        CancellationToken                                      cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);
        await torrentStateStore.DeleteAsync(torrentId, cancellationToken);

        if (request.DeleteData)
        {
            var downloadRootPath = torrent.DownloadRootPath ?? servicePaths.DownloadRootPath;
            TorrentDataPathCleanup.DeletePayloadArtifacts(downloadRootPath, [torrent.SavePath]);
            TorrentDataPathCleanup.DeleteEmptyDirectories(downloadRootPath, [torrent.SavePath]);
        }

        return new TorrentActionResultDto
        {
            TorrentId      = torrent.TorrentId,
            Action         = "remove",
            State          = TorrentState.Removed,
            ProcessedAtUtc = DateTimeOffset.UtcNow,
            DataDeleted    = request.DeleteData,
        };
    }

    private async Task<TorrentSnapshot> GetRequiredSnapshotAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await torrentStateStore.GetAsync(torrentId, cancellationToken);
        return torrent ?? throw new ServiceOperationException(
            "torrent_not_found", $"Torrent '{torrentId}' was not found.", StatusCodes.Status404NotFound,
            nameof(torrentId)
        );
    }

    private static MagnetMetadata ParseMagnet(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
        {
            throw new ServiceOperationException(
                "invalid_magnet", "MagnetUri is required.", StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri)
            );
        }

        if (!Uri.TryCreate(magnetUri.Trim(), UriKind.Absolute, out var uri) || !string.Equals(
                    uri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase
                ))
        {
            throw new ServiceOperationException(
                "invalid_magnet", "MagnetUri must be a valid magnet URI.", StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri)
            );
        }

        var query    = QueryHelpers.ParseQuery(uri.Query);
        var infoHash = ExtractInfoHash(query);
        var displayName = query.TryGetValue("dn", out var names) && !StringValues.IsNullOrEmpty(names) ? names[0]! :
                $"Magnet {infoHash[..8]}";

        return new MagnetMetadata(infoHash, displayName);
    }

    private static string ExtractInfoHash(Dictionary<string, StringValues> query)
    {
        if (!query.TryGetValue("xt", out var exactTopics) || StringValues.IsNullOrEmpty(exactTopics))
        {
            throw new ServiceOperationException(
                "invalid_magnet", "MagnetUri must include an exact topic info hash (xt=urn:btih:...).",
                StatusCodes.Status400BadRequest, nameof(AddMagnetRequest.MagnetUri)
            );
        }

        foreach (var exactTopic in exactTopics)
        {
            const string prefix = "urn:btih:";

            if (exactTopic is not null && exactTopic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var infoHash = exactTopic[prefix.Length..].Trim();

                if (infoHash.Length is 32 or 40)
                {
                    return infoHash.ToUpperInvariant();
                }
            }
        }

        throw new ServiceOperationException(
            "invalid_magnet", "MagnetUri must include a btih exact topic value.", StatusCodes.Status400BadRequest,
            nameof(AddMagnetRequest.MagnetUri)
        );
    }

    private static TorrentState NormalizeStateForStartup(TorrentState state)
    {
        return state switch
        {
            TorrentState.ResolvingMetadata => TorrentState.Queued,
            TorrentState.Downloading => TorrentState.Queued,
            TorrentState.Seeding => TorrentState.Queued,
            _ => state,
        };
    }

    private static TorrentSummaryDto MapSummary(TorrentSnapshot snapshot, TorrentQueueDiagnostic diagnostic)
    {
        return new TorrentSummaryDto
        {
            TorrentId                         = snapshot.TorrentId,
            Name                              = snapshot.Name,
            CategoryKey                       = snapshot.CategoryKey,
            State                             = snapshot.State,
            ProgressPercent                   = snapshot.ProgressPercent,
            DownloadedBytes                   = snapshot.DownloadedBytes,
            TotalBytes                        = snapshot.TotalBytes,
            DownloadRateBytesPerSecond        = snapshot.DownloadRateBytesPerSecond,
            UploadRateBytesPerSecond          = snapshot.UploadRateBytesPerSecond,
            TrackerCount                      = snapshot.TrackerCount,
            ConnectedPeerCount                = snapshot.ConnectedPeerCount,
            WaitReason                        = diagnostic.WaitReason,
            QueuePosition                     = diagnostic.QueuePosition,
            AddedAtUtc                        = snapshot.AddedAtUtc,
            CompletedAtUtc                    = snapshot.CompletedAtUtc,
            LastActivityAtUtc                 = snapshot.LastActivityAtUtc,
            CompletionCallbackState           = snapshot.CompletionCallbackState?.ToString(),
            CompletionCallbackPendingSinceUtc = snapshot.CompletionCallbackPendingSinceUtc,
            CompletionCallbackInvokedAtUtc    = snapshot.CompletionCallbackInvokedAtUtc,
            CompletionCallbackLastError       = snapshot.CompletionCallbackLastError,
            ErrorMessage                      = snapshot.ErrorMessage,
            CanRefreshMetadata                = CanRefreshMetadata(snapshot.State),
            CanRetryCompletionCallback        = CanRetryCompletionCallback(snapshot.CompletionCallbackState),
            CanPause                          = CanPause(snapshot.State),
            CanResume                         = CanResume(snapshot.State),
            CanRemove                         = CanRemove(snapshot.State),
        };
    }

    private TorrentDetailDto MapDetail(TorrentSnapshot snapshot, TorrentQueueDiagnostic diagnostic,
        RuntimeSettingsSnapshot?                       runtimeSettings)
    {
        var callbackFinalPayloadPath = Path.Combine(
            snapshot.DownloadRootPath ?? servicePaths.DownloadRootPath, snapshot.Name
        );
        string? callbackPendingReason = null;
        if (runtimeSettings is not null &&
            snapshot.CompletionCallbackState is TorrentCompletionCallbackState.PendingFinalization or
                    TorrentCompletionCallbackState.TimedOut)
        {
            var finalizationResult = finalizationChecker.Check(snapshot, runtimeSettings);
            callbackFinalPayloadPath = finalizationResult.FinalPayloadPath;
            callbackPendingReason    = finalizationResult.IsReady ? null : finalizationResult.PendingReason;
        }

        return new TorrentDetailDto
        {
            TorrentId                          = snapshot.TorrentId,
            Name                               = snapshot.Name,
            CategoryKey                        = snapshot.CategoryKey,
            State                              = snapshot.State,
            MagnetUri                          = snapshot.MagnetUri,
            InfoHash                           = snapshot.InfoHash,
            SavePath                           = snapshot.SavePath,
            ProgressPercent                    = snapshot.ProgressPercent,
            DownloadedBytes                    = snapshot.DownloadedBytes,
            TotalBytes                         = snapshot.TotalBytes,
            DownloadRateBytesPerSecond         = snapshot.DownloadRateBytesPerSecond,
            UploadRateBytesPerSecond           = snapshot.UploadRateBytesPerSecond,
            TrackerCount                       = snapshot.TrackerCount,
            ConnectedPeerCount                 = snapshot.ConnectedPeerCount,
            WaitReason                         = diagnostic.WaitReason,
            QueuePosition                      = diagnostic.QueuePosition,
            AddedAtUtc                         = snapshot.AddedAtUtc,
            CompletedAtUtc                     = snapshot.CompletedAtUtc,
            LastActivityAtUtc                  = snapshot.LastActivityAtUtc,
            CompletionCallbackState            = snapshot.CompletionCallbackState?.ToString(),
            CompletionCallbackPendingSinceUtc  = snapshot.CompletionCallbackPendingSinceUtc,
            CompletionCallbackInvokedAtUtc     = snapshot.CompletionCallbackInvokedAtUtc,
            CompletionCallbackFinalPayloadPath = callbackFinalPayloadPath,
            CompletionCallbackPendingReason    = callbackPendingReason,
            CompletionCallbackLastError        = snapshot.CompletionCallbackLastError,
            ErrorMessage                       = snapshot.ErrorMessage,
            CanRefreshMetadata                 = CanRefreshMetadata(snapshot.State),
            CanRetryCompletionCallback         = CanRetryCompletionCallback(snapshot.CompletionCallbackState),
            CanPause                           = CanPause(snapshot.State),
            CanResume                          = CanResume(snapshot.State),
            CanRemove                          = CanRemove(snapshot.State),
        };
    }

    private static bool CanRefreshMetadata(TorrentState state) { return state == TorrentState.ResolvingMetadata; }

    private static bool CanPause(TorrentState state)
    {
        return state is TorrentState.Downloading or TorrentState.Seeding or TorrentState.Queued or
                TorrentState.ResolvingMetadata;
    }

    private static bool CanResume(TorrentState state) { return state is TorrentState.Paused or TorrentState.Error; }

    private static bool CanRetryCompletionCallback(TorrentCompletionCallbackState? callbackState)
    {
        return callbackState is TorrentCompletionCallbackState.Failed or TorrentCompletionCallbackState.TimedOut;
    }

    private static bool CanRemove(TorrentState state) { return state is not TorrentState.Removed; }

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()
        ).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "torrent" : sanitized;
    }

    private sealed record MagnetMetadata(string InfoHash, string DisplayName);
}
