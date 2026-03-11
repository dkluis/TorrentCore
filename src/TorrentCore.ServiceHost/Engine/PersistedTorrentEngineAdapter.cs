using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Application;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Engine;

public sealed class PersistedTorrentEngineAdapter(
    ITorrentStateStore torrentStateStore,
    IRuntimeSettingsService runtimeSettingsService) : ITorrentEngineAdapter
{
    public Task<int> GetTorrentCountAsync(CancellationToken cancellationToken) =>
        torrentStateStore.CountAsync(cancellationToken);

    public async Task<TorrentEngineRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var torrents = await torrentStateStore.ListAsync(cancellationToken);
        var changes = new List<TorrentRecoveryChange>();

        foreach (var torrent in torrents)
        {
            var previousState = torrent.State;
            var normalizedState = NormalizeStateForStartup(previousState);
            var requiresUpdate = false;

            if (previousState != normalizedState)
            {
                torrent.State = normalizedState;
                requiresUpdate = true;

                changes.Add(new TorrentRecoveryChange
                {
                    TorrentId = torrent.TorrentId,
                    Name = torrent.Name,
                    PreviousState = previousState,
                    CurrentState = normalizedState,
                });
            }

            if (torrent.DownloadRateBytesPerSecond != 0)
            {
                torrent.DownloadRateBytesPerSecond = 0;
                requiresUpdate = true;
            }

            if (torrent.UploadRateBytesPerSecond != 0)
            {
                torrent.UploadRateBytesPerSecond = 0;
                requiresUpdate = true;
            }

            if (torrent.ConnectedPeerCount != 0)
            {
                torrent.ConnectedPeerCount = 0;
                requiresUpdate = true;
            }

            if (requiresUpdate)
            {
                torrent.LastActivityAtUtc = now;
                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            }
        }

        return new TorrentEngineRecoveryResult
        {
            RecoveredTorrentCount = torrents.Count,
            NormalizedTorrentCount = changes.Count,
            CompletedAtUtc = now,
            Changes = changes,
        };
    }

    public Task SynchronizeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        var torrents = await torrentStateStore.ListAsync(cancellationToken);
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var diagnostics = TorrentQueueDiagnostics.Create(torrents, runtimeSettings);
        return torrents.Select(snapshot => MapSummary(snapshot, diagnostics[snapshot.TorrentId])).ToArray();
    }

    public async Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrents = await torrentStateStore.ListAsync(cancellationToken);
        var torrent = torrents.SingleOrDefault(snapshot => snapshot.TorrentId == torrentId);
        return torrent is null
            ? throw new ServiceOperationException(
                "torrent_not_found",
                $"Torrent '{torrentId}' was not found.",
                StatusCodes.Status404NotFound,
                nameof(torrentId))
            : MapDetail(
                torrent,
                TorrentQueueDiagnostics.Create(
                    torrents,
                    await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken))[torrent.TorrentId]);
    }

    public async Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, string downloadRootPath, CancellationToken cancellationToken)
    {
        var magnet = ParseMagnet(request.MagnetUri);

        if (await torrentStateStore.ExistsByInfoHashAsync(magnet.InfoHash, cancellationToken))
        {
            throw new ServiceOperationException(
                "duplicate_magnet",
                "A torrent with the same info hash already exists on this host.",
                StatusCodes.Status409Conflict,
                nameof(request.MagnetUri));
        }

        var now = DateTimeOffset.UtcNow;
        var torrent = new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = magnet.DisplayName,
            MagnetUri = request.MagnetUri.Trim(),
            InfoHash = magnet.InfoHash,
            DownloadRootPath = downloadRootPath,
            SavePath = Path.Combine(downloadRootPath, SanitizePathSegment(magnet.DisplayName)),
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
        return MapDetail(torrent, new TorrentQueueDiagnostic(null, null));
    }

    public async Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);

        if (!CanPause(torrent.State))
        {
            throw new ServiceOperationException(
                "invalid_state",
                $"Torrent '{torrent.Name}' cannot be paused while in state '{torrent.State}'.",
                StatusCodes.Status409Conflict,
                nameof(torrentId));
        }

        torrent.DesiredState = TorrentDesiredState.Paused;
        torrent.State = TorrentState.Paused;
        torrent.DownloadRateBytesPerSecond = 0;
        torrent.UploadRateBytesPerSecond = 0;
        torrent.LastActivityAtUtc = DateTimeOffset.UtcNow;

        await torrentStateStore.UpdateAsync(torrent, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId = torrent.TorrentId,
            Action = "pause",
            State = torrent.State,
            ProcessedAtUtc = torrent.LastActivityAtUtc.Value,
            DataDeleted = false,
        };
    }

    public async Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);

        if (!CanResume(torrent.State))
        {
            throw new ServiceOperationException(
                "invalid_state",
                $"Torrent '{torrent.Name}' cannot be resumed while in state '{torrent.State}'.",
                StatusCodes.Status409Conflict,
                nameof(torrentId));
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
            TorrentId = torrent.TorrentId,
            Action = "resume",
            State = torrent.State,
            ProcessedAtUtc = torrent.LastActivityAtUtc.Value,
            DataDeleted = false,
        };
    }

    public async Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request, CancellationToken cancellationToken)
    {
        var torrent = await GetRequiredSnapshotAsync(torrentId, cancellationToken);
        await torrentStateStore.DeleteAsync(torrentId, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId = torrent.TorrentId,
            Action = "remove",
            State = TorrentState.Removed,
            ProcessedAtUtc = DateTimeOffset.UtcNow,
            DataDeleted = request.DeleteData,
        };
    }

    private async Task<TorrentSnapshot> GetRequiredSnapshotAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await torrentStateStore.GetAsync(torrentId, cancellationToken);
        return torrent ?? throw new ServiceOperationException(
            "torrent_not_found",
            $"Torrent '{torrentId}' was not found.",
            StatusCodes.Status404NotFound,
            nameof(torrentId));
    }

    private static MagnetMetadata ParseMagnet(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
        {
            throw new ServiceOperationException(
                "invalid_magnet",
                "MagnetUri is required.",
                StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri));
        }

        if (!Uri.TryCreate(magnetUri.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase))
        {
            throw new ServiceOperationException(
                "invalid_magnet",
                "MagnetUri must be a valid magnet URI.",
                StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri));
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        var infoHash = ExtractInfoHash(query);
        var displayName = query.TryGetValue("dn", out var names) && !StringValues.IsNullOrEmpty(names)
            ? names[0]!
            : $"Magnet {infoHash[..8]}";

        return new MagnetMetadata(infoHash, displayName);
    }

    private static string ExtractInfoHash(Dictionary<string, StringValues> query)
    {
        if (!query.TryGetValue("xt", out var exactTopics) || StringValues.IsNullOrEmpty(exactTopics))
        {
            throw new ServiceOperationException(
                "invalid_magnet",
                "MagnetUri must include an exact topic info hash (xt=urn:btih:...).",
                StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri));
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
            "invalid_magnet",
            "MagnetUri must include a btih exact topic value.",
            StatusCodes.Status400BadRequest,
            nameof(AddMagnetRequest.MagnetUri));
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
            TorrentId = snapshot.TorrentId,
            Name = snapshot.Name,
            State = snapshot.State,
            ProgressPercent = snapshot.ProgressPercent,
            DownloadedBytes = snapshot.DownloadedBytes,
            TotalBytes = snapshot.TotalBytes,
            DownloadRateBytesPerSecond = snapshot.DownloadRateBytesPerSecond,
            UploadRateBytesPerSecond = snapshot.UploadRateBytesPerSecond,
            TrackerCount = snapshot.TrackerCount,
            ConnectedPeerCount = snapshot.ConnectedPeerCount,
            WaitReason = diagnostic.WaitReason,
            QueuePosition = diagnostic.QueuePosition,
            AddedAtUtc = snapshot.AddedAtUtc,
            CompletedAtUtc = snapshot.CompletedAtUtc,
            LastActivityAtUtc = snapshot.LastActivityAtUtc,
            ErrorMessage = snapshot.ErrorMessage,
            CanPause = CanPause(snapshot.State),
            CanResume = CanResume(snapshot.State),
            CanRemove = CanRemove(snapshot.State),
        };
    }

    private static TorrentDetailDto MapDetail(TorrentSnapshot snapshot, TorrentQueueDiagnostic diagnostic)
    {
        return new TorrentDetailDto
        {
            TorrentId = snapshot.TorrentId,
            Name = snapshot.Name,
            State = snapshot.State,
            MagnetUri = snapshot.MagnetUri,
            InfoHash = snapshot.InfoHash,
            SavePath = snapshot.SavePath,
            ProgressPercent = snapshot.ProgressPercent,
            DownloadedBytes = snapshot.DownloadedBytes,
            TotalBytes = snapshot.TotalBytes,
            DownloadRateBytesPerSecond = snapshot.DownloadRateBytesPerSecond,
            UploadRateBytesPerSecond = snapshot.UploadRateBytesPerSecond,
            TrackerCount = snapshot.TrackerCount,
            ConnectedPeerCount = snapshot.ConnectedPeerCount,
            WaitReason = diagnostic.WaitReason,
            QueuePosition = diagnostic.QueuePosition,
            AddedAtUtc = snapshot.AddedAtUtc,
            CompletedAtUtc = snapshot.CompletedAtUtc,
            LastActivityAtUtc = snapshot.LastActivityAtUtc,
            ErrorMessage = snapshot.ErrorMessage,
            CanPause = CanPause(snapshot.State),
            CanResume = CanResume(snapshot.State),
            CanRemove = CanRemove(snapshot.State),
        };
    }

    private static bool CanPause(TorrentState state) => state is TorrentState.Downloading or TorrentState.Seeding or TorrentState.Queued or TorrentState.ResolvingMetadata;

    private static bool CanResume(TorrentState state) => state is TorrentState.Paused or TorrentState.Error;

    private static bool CanRemove(TorrentState state) => state is not TorrentState.Removed;

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "torrent" : sanitized;
    }

    private sealed record MagnetMetadata(string InfoHash, string DisplayName);
}
