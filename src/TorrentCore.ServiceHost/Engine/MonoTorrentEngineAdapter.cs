using MonoTorrent;
using MonoTorrent.Client;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Application;
using TorrentCore.Service.Configuration;
using ContractTorrentState = TorrentCore.Contracts.Torrents.TorrentState;

namespace TorrentCore.Service.Engine;

public sealed class MonoTorrentEngineAdapter(
    ITorrentStateStore torrentStateStore,
    IActivityLogService activityLogService,
    ResolvedTorrentCoreServicePaths servicePaths,
    IOptions<TorrentCoreServiceOptions> serviceOptions,
    ServiceInstanceContext serviceInstanceContext,
    ILogger<MonoTorrentEngineAdapter> logger) : ITorrentEngineAdapter, IHostedService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, TorrentManager> _managers = new();
    private readonly HashSet<Guid> _observedTorrentIds = [];
    private readonly TorrentCoreServiceOptions _serviceOptions = serviceOptions.Value;

    private ClientEngine? _engine;
    private bool _initialized;
    private bool _recovered;
    private TorrentEngineRecoveryResult? _lastRecoveryResult;
    private int _disposeState;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_serviceOptions.EngineMode != TorrentEngineMode.MonoTorrent)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_serviceOptions.EngineMode != TorrentEngineMode.MonoTorrent)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_engine is null)
            {
                return;
            }

            foreach (var manager in _managers.Values)
            {
                try
                {
                    if (manager.State is not MonoTorrent.Client.TorrentState.Stopped and not MonoTorrent.Client.TorrentState.Paused)
                    {
                        await manager.StopAsync(TimeSpan.FromSeconds(2));
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed stopping MonoTorrent manager {ManagerName}", manager.Name);
                }
            }

            await _engine.StopAllAsync(TimeSpan.FromSeconds(2));
            _managers.Clear();
            _observedTorrentIds.Clear();
            _recovered = false;
            _lastRecoveryResult = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1)
        {
            return;
        }

        if (_engine is not null)
        {
            await StopAsync(CancellationToken.None);
        }

        _gate.Dispose();
    }

    public Task<int> GetTorrentCountAsync(CancellationToken cancellationToken) =>
        torrentStateStore.CountAsync(cancellationToken);

    public async Task<TorrentEngineRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_recovered && _lastRecoveryResult is not null)
            {
                return _lastRecoveryResult;
            }

            var now = DateTimeOffset.UtcNow;
            var snapshots = await torrentStateStore.ListAsync(cancellationToken);
            var changes = new List<TorrentRecoveryChange>();

            foreach (var snapshot in snapshots)
            {
                try
                {
                    var manager = await AddOrGetManagerAsync(snapshot, cancellationToken);

                    if (ShouldStartOnRecovery(snapshot.State))
                    {
                        await manager.StartAsync();
                    }

                    var updatedSnapshot = CreateUpdatedSnapshot(snapshot, manager, now);
                    await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to recover torrent {TorrentId} ({TorrentName})", snapshot.TorrentId, snapshot.Name);

                    var previousState = snapshot.State;
                    snapshot.State = ContractTorrentState.Error;
                    snapshot.ErrorMessage = exception.Message;
                    snapshot.LastActivityAtUtc = now;
                    await torrentStateStore.UpdateAsync(snapshot, cancellationToken);

                    changes.Add(new TorrentRecoveryChange
                    {
                        TorrentId = snapshot.TorrentId,
                        Name = snapshot.Name,
                        PreviousState = previousState,
                        CurrentState = ContractTorrentState.Error,
                    });
                }
            }

            _recovered = true;
            _lastRecoveryResult = new TorrentEngineRecoveryResult
            {
                RecoveredTorrentCount = snapshots.Count,
                NormalizedTorrentCount = changes.Count,
                CompletedAtUtc = now,
                Changes = changes,
            };

            return _lastRecoveryResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        List<KeyValuePair<Guid, TorrentManager>> managers;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recovered)
            {
                return;
            }

            managers = _managers.ToList();
        }
        finally
        {
            _gate.Release();
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var entry in managers)
        {
            var snapshot = await torrentStateStore.GetAsync(entry.Key, cancellationToken);
            if (snapshot is null)
            {
                continue;
            }

            var updatedSnapshot = CreateUpdatedSnapshot(snapshot, entry.Value, now);
            await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        await SynchronizeAsync(cancellationToken);
        var torrents = await torrentStateStore.ListAsync(cancellationToken);
        return torrents.Select(MapSummary).ToArray();
    }

    public async Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        await SynchronizeAsync(cancellationToken);
        var torrent = await torrentStateStore.GetAsync(torrentId, cancellationToken);
        return torrent is null
            ? throw new ServiceOperationException(
                "torrent_not_found",
                $"Torrent '{torrentId}' was not found.",
                StatusCodes.Status404NotFound,
                nameof(torrentId))
            : MapDetail(torrent);
    }

    public async Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, string downloadRootPath, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        MagnetLink magnet;
        try
        {
            magnet = MagnetLink.Parse(request.MagnetUri.Trim());
        }
        catch (Exception)
        {
            throw new ServiceOperationException(
                "invalid_magnet",
                "MagnetUri must be a valid magnet URI.",
                StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri));
        }

        var infoHash = magnet.InfoHashes.V1OrV2.ToHex().ToUpperInvariant();
        if (await torrentStateStore.ExistsByInfoHashAsync(infoHash, cancellationToken))
        {
            throw new ServiceOperationException(
                "duplicate_magnet",
                "A torrent with the same info hash already exists on this host.",
                StatusCodes.Status409Conflict,
                nameof(request.MagnetUri));
        }

        var now = DateTimeOffset.UtcNow;
        var manager = await _engine!.AddAsync(magnet, downloadRootPath);
        var torrentId = Guid.NewGuid();
        RegisterManager(torrentId, manager);
        await manager.StartAsync();

        var snapshot = new TorrentSnapshot
        {
            TorrentId = torrentId,
            Name = string.IsNullOrWhiteSpace(magnet.Name) ? $"Magnet {infoHash[..8]}" : magnet.Name,
            State = ContractTorrentState.ResolvingMetadata,
            MagnetUri = request.MagnetUri.Trim(),
            InfoHash = infoHash,
            SavePath = downloadRootPath,
            ProgressPercent = 0,
            DownloadedBytes = 0,
            TotalBytes = magnet.Size,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = CountTrackers(manager),
            ConnectedPeerCount = manager.OpenConnections,
            AddedAtUtc = now,
            LastActivityAtUtc = now,
        };

        snapshot = CreateUpdatedSnapshot(snapshot, manager, now);
        await torrentStateStore.InsertAsync(snapshot, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _managers[snapshot.TorrentId] = manager;
        }
        finally
        {
            _gate.Release();
        }

        return MapDetail(snapshot);
    }

    public async Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var (snapshot, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);

        if (!CanPause(snapshot.State))
        {
            throw new ServiceOperationException(
                "invalid_state",
                $"Torrent '{snapshot.Name}' cannot be paused while in state '{snapshot.State}'.",
                StatusCodes.Status409Conflict,
                nameof(torrentId));
        }

        await manager.PauseAsync();

        var now = DateTimeOffset.UtcNow;
        var updatedSnapshot = CreateUpdatedSnapshot(snapshot, manager, now);
        updatedSnapshot.State = ContractTorrentState.Paused;
        updatedSnapshot.DownloadRateBytesPerSecond = 0;
        updatedSnapshot.UploadRateBytesPerSecond = 0;
        updatedSnapshot.ConnectedPeerCount = 0;
        updatedSnapshot.LastActivityAtUtc = now;
        await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId = torrentId,
            Action = "pause",
            State = updatedSnapshot.State,
            ProcessedAtUtc = now,
            DataDeleted = false,
        };
    }

    public async Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var (snapshot, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);

        if (!CanResume(snapshot.State))
        {
            throw new ServiceOperationException(
                "invalid_state",
                $"Torrent '{snapshot.Name}' cannot be resumed while in state '{snapshot.State}'.",
                StatusCodes.Status409Conflict,
                nameof(torrentId));
        }

        await manager.StartAsync();

        var now = DateTimeOffset.UtcNow;
        var updatedSnapshot = CreateUpdatedSnapshot(snapshot, manager, now);
        await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId = torrentId,
            Action = "resume",
            State = updatedSnapshot.State,
            ProcessedAtUtc = now,
            DataDeleted = false,
        };
    }

    public async Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request, CancellationToken cancellationToken)
    {
        var (_, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);

        await _engine!.RemoveAsync(
            manager,
            request.DeleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _managers.Remove(torrentId);
            _observedTorrentIds.Remove(torrentId);
        }
        finally
        {
            _gate.Release();
        }

        await torrentStateStore.DeleteAsync(torrentId, cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId = torrentId,
            Action = "remove",
            State = ContractTorrentState.Removed,
            ProcessedAtUtc = DateTimeOffset.UtcNow,
            DataDeleted = request.DeleteData,
        };
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_serviceOptions.EngineMode != TorrentEngineMode.MonoTorrent)
        {
            throw new InvalidOperationException("MonoTorrent engine adapter cannot initialize when EngineMode is not MonoTorrent.");
        }

        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
            {
                return;
            }

            var cacheDirectory = Path.Combine(servicePaths.StorageRootPath, "monotorrent-cache");
            Directory.CreateDirectory(cacheDirectory);

            var engineSettingsBuilder = new EngineSettingsBuilder
            {
                CacheDirectory = cacheDirectory,
                AutoSaveLoadFastResume = true,
                AutoSaveLoadMagnetLinkMetadata = true,
            };

            _engine = new ClientEngine(engineSettingsBuilder.ToSettings());
            _initialized = true;

            logger.LogInformation("MonoTorrent engine initialized. CacheDirectory={CacheDirectory}", cacheDirectory);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TorrentManager> AddOrGetManagerAsync(TorrentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_managers.TryGetValue(snapshot.TorrentId, out var existingManager))
        {
            return existingManager;
        }

        var magnet = MagnetLink.Parse(snapshot.MagnetUri);
        var manager = await _engine!.AddAsync(magnet, snapshot.SavePath);
        RegisterManager(snapshot.TorrentId, manager);
        _managers[snapshot.TorrentId] = manager;
        return manager;
    }

    private async Task<(TorrentSnapshot Snapshot, TorrentManager Manager)> GetRequiredManagedTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_recovered)
        {
            await RecoverAsync(cancellationToken);
        }

        TorrentManager? manager;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _managers.TryGetValue(torrentId, out manager);
        }
        finally
        {
            _gate.Release();
        }

        var snapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken);
        if (snapshot is null || manager is null)
        {
            throw new ServiceOperationException(
                "torrent_not_found",
                $"Torrent '{torrentId}' was not found.",
                StatusCodes.Status404NotFound,
                nameof(torrentId));
        }

        return (snapshot, manager);
    }

    private TorrentSnapshot CreateUpdatedSnapshot(TorrentSnapshot existing, TorrentManager manager, DateTimeOffset now)
    {
        var state = MapState(manager, existing.State);
        var totalBytes = manager.HasMetadata
            ? manager.Torrent?.Size ?? existing.TotalBytes
            : existing.TotalBytes ?? manager.MagnetLink?.Size;
        var savePath = manager.HasMetadata && !string.IsNullOrWhiteSpace(manager.ContainingDirectory)
            ? manager.ContainingDirectory
            : manager.SavePath;
        var downloadedBytes = CalculateDownloadedBytes(totalBytes, manager.Progress, existing.DownloadedBytes);

        if (manager.HasMetadata && state == ContractTorrentState.ResolvingMetadata)
        {
            state = manager.Complete ? ContractTorrentState.Seeding : ContractTorrentState.Downloading;
        }

        return new TorrentSnapshot
        {
            TorrentId = existing.TorrentId,
            Name = string.IsNullOrWhiteSpace(manager.Name) ? existing.Name : manager.Name,
            State = state,
            MagnetUri = existing.MagnetUri,
            InfoHash = manager.InfoHashes.V1OrV2.ToHex().ToUpperInvariant(),
            SavePath = savePath,
            ProgressPercent = manager.Progress,
            DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes,
            DownloadRateBytesPerSecond = manager.Monitor.DownloadRate,
            UploadRateBytesPerSecond = manager.Monitor.UploadRate,
            TrackerCount = CountTrackers(manager),
            ConnectedPeerCount = manager.OpenConnections,
            AddedAtUtc = existing.AddedAtUtc,
            CompletedAtUtc = manager.Complete
                ? existing.CompletedAtUtc ?? now
                : existing.CompletedAtUtc,
            LastActivityAtUtc = now,
            ErrorMessage = manager.Error?.Reason.ToString() ?? existing.ErrorMessage,
        };
    }

    private void RegisterManager(Guid torrentId, TorrentManager manager)
    {
        if (!_observedTorrentIds.Add(torrentId))
        {
            return;
        }

        manager.TorrentStateChanged += (_, eventArgs) => _ = HandleTorrentStateChangedAsync(torrentId, eventArgs);
        manager.PeersFound += (_, eventArgs) => _ = HandlePeersFoundAsync(torrentId, eventArgs);
        manager.ConnectionAttemptFailed += (_, eventArgs) => _ = HandleConnectionAttemptFailedAsync(torrentId, eventArgs);
    }

    private async Task HandleTorrentStateChangedAsync(Guid torrentId, TorrentStateChangedEventArgs eventArgs)
    {
        try
        {
            var snapshot = await torrentStateStore.GetAsync(torrentId, CancellationToken.None);
            if (snapshot is not null)
            {
                var updatedSnapshot = CreateUpdatedSnapshot(snapshot, eventArgs.TorrentManager, DateTimeOffset.UtcNow);
                await torrentStateStore.UpdateAsync(updatedSnapshot, CancellationToken.None);
            }

            await activityLogService.WriteAsync(new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Information,
                Category = "engine",
                EventType = "torrent.engine.state_changed",
                Message = $"Torrent engine state changed from '{eventArgs.OldState}' to '{eventArgs.NewState}'.",
                TorrentId = torrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    OldState = eventArgs.OldState.ToString(),
                    NewState = eventArgs.NewState.ToString(),
                    ContractState = MapState(eventArgs.TorrentManager, snapshot?.State ?? ContractTorrentState.Queued).ToString(),
                    HasMetadata = eventArgs.TorrentManager.HasMetadata,
                    ProgressPercent = eventArgs.TorrentManager.Progress,
                }),
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed handling MonoTorrent state change event for torrent {TorrentId}", torrentId);
        }
    }

    private async Task HandlePeersFoundAsync(Guid torrentId, PeersAddedEventArgs eventArgs)
    {
        try
        {
            await activityLogService.WriteAsync(new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Information,
                Category = "engine",
                EventType = "torrent.engine.peers_found",
                Message = $"MonoTorrent discovered {eventArgs.NewPeers} new peer(s).",
                TorrentId = torrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    eventArgs.NewPeers,
                    eventArgs.ExistingPeers,
                    OpenConnections = eventArgs.TorrentManager.OpenConnections,
                }),
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed handling MonoTorrent peers-found event for torrent {TorrentId}", torrentId);
        }
    }

    private async Task HandleConnectionAttemptFailedAsync(Guid torrentId, ConnectionAttemptFailedEventArgs eventArgs)
    {
        try
        {
            await activityLogService.WriteAsync(new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Warning,
                Category = "engine",
                EventType = "torrent.engine.connection_failed",
                Message = $"MonoTorrent connection attempt failed with reason '{eventArgs.Reason}'.",
                TorrentId = torrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    Reason = eventArgs.Reason.ToString(),
                    PeerUri = eventArgs.Peer.ConnectionUri?.ToString(),
                }),
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed handling MonoTorrent connection-failed event for torrent {TorrentId}", torrentId);
        }
    }

    private static long CalculateDownloadedBytes(long? totalBytes, double progressPercent, long existingDownloadedBytes)
    {
        if (totalBytes is null)
        {
            return existingDownloadedBytes;
        }

        var boundedProgress = Math.Clamp(progressPercent, 0, 100);
        return (long)Math.Round(totalBytes.Value * (boundedProgress / 100d), MidpointRounding.AwayFromZero);
    }

    private static ContractTorrentState MapState(TorrentManager manager, ContractTorrentState existingState)
    {
        return manager.State switch
        {
            MonoTorrent.Client.TorrentState.Metadata => ContractTorrentState.ResolvingMetadata,
            MonoTorrent.Client.TorrentState.Downloading => ContractTorrentState.Downloading,
            MonoTorrent.Client.TorrentState.Seeding => ContractTorrentState.Seeding,
            MonoTorrent.Client.TorrentState.Error => ContractTorrentState.Error,
            MonoTorrent.Client.TorrentState.Paused => ContractTorrentState.Paused,
            MonoTorrent.Client.TorrentState.Hashing or MonoTorrent.Client.TorrentState.HashingPaused or MonoTorrent.Client.TorrentState.FetchingHashes => ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Starting => ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Stopping => existingState,
            MonoTorrent.Client.TorrentState.Stopped => existingState switch
            {
                ContractTorrentState.Paused => ContractTorrentState.Paused,
                ContractTorrentState.Completed => ContractTorrentState.Completed,
                ContractTorrentState.Seeding => ContractTorrentState.Seeding,
                _ when manager.Complete => ContractTorrentState.Completed,
                _ => ContractTorrentState.Queued,
            },
            _ => existingState,
        };
    }

    private static bool ShouldStartOnRecovery(ContractTorrentState state) =>
        state is not ContractTorrentState.Paused and not ContractTorrentState.Completed and not ContractTorrentState.Error and not ContractTorrentState.Removed;

    private static int CountTrackers(TorrentManager manager) =>
        manager.TrackerManager?.Tiers.Sum(tier => tier.Trackers.Count) ?? manager.MagnetLink?.AnnounceUrls?.Count ?? 0;

    private static bool CanPause(ContractTorrentState state) => state is ContractTorrentState.Downloading or ContractTorrentState.Seeding or ContractTorrentState.Queued or ContractTorrentState.ResolvingMetadata;

    private static bool CanResume(ContractTorrentState state) => state is ContractTorrentState.Paused or ContractTorrentState.Error;

    private static TorrentSummaryDto MapSummary(TorrentSnapshot snapshot)
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
            AddedAtUtc = snapshot.AddedAtUtc,
            CompletedAtUtc = snapshot.CompletedAtUtc,
            LastActivityAtUtc = snapshot.LastActivityAtUtc,
            ErrorMessage = snapshot.ErrorMessage,
            CanPause = CanPause(snapshot.State),
            CanResume = CanResume(snapshot.State),
            CanRemove = snapshot.State is not ContractTorrentState.Removed,
        };
    }

    private static TorrentDetailDto MapDetail(TorrentSnapshot snapshot)
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
            AddedAtUtc = snapshot.AddedAtUtc,
            CompletedAtUtc = snapshot.CompletedAtUtc,
            LastActivityAtUtc = snapshot.LastActivityAtUtc,
            ErrorMessage = snapshot.ErrorMessage,
            CanPause = CanPause(snapshot.State),
            CanResume = CanResume(snapshot.State),
            CanRemove = snapshot.State is not ContractTorrentState.Removed,
        };
    }
}
