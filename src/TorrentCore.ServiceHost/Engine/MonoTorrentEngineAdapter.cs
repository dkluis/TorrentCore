using System.Collections.Concurrent;
using MonoTorrent;
using MonoTorrent.Client;
using Microsoft.Extensions.Options;
using System.Net;
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
    IRuntimeSettingsService runtimeSettingsService,
    AppliedEngineSettingsState appliedEngineSettingsState,
    ServiceInstanceContext serviceInstanceContext,
    ILogger<MonoTorrentEngineAdapter> logger) : ITorrentEngineAdapter, IHostedService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, TorrentManager> _managers = new();
    private readonly HashSet<Guid> _observedTorrentIds = [];
    private readonly ConcurrentDictionary<Guid, long> _observedUploadedSessionBytes = new();
    private readonly TorrentCoreServiceOptions _serviceOptions = serviceOptions.Value;
    private readonly ConnectionFailureLogThrottle _connectionFailureLogThrottle = new();

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
            _observedUploadedSessionBytes.Clear();
            _connectionFailureLogThrottle.Clear();
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

        var snapshots = Array.Empty<TorrentSnapshot>();
        var changes = new List<TorrentRecoveryChange>();
        var now = DateTimeOffset.UtcNow;
        TorrentEngineRecoveryResult? recoveryResult = null;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_recovered && _lastRecoveryResult is not null)
            {
                return _lastRecoveryResult;
            }

            snapshots = (await torrentStateStore.ListAsync(cancellationToken)).ToArray();

            foreach (var snapshot in snapshots)
            {
                try
                {
                    var manager = await AddOrGetManagerAsync(snapshot, cancellationToken);
                    var updatedSnapshot = CreateUpdatedSnapshot(snapshot, manager, now);
                    var previousState = snapshot.State;

                    if (ShouldStartOnRecovery(snapshot.State))
                    {
                        if (updatedSnapshot.State != ContractTorrentState.Completed &&
                            updatedSnapshot.State != ContractTorrentState.Paused &&
                            updatedSnapshot.State != ContractTorrentState.Error)
                        {
                            updatedSnapshot = CreateQueuedSnapshot(updatedSnapshot, now);
                        }
                    }

                    if (updatedSnapshot.State == ContractTorrentState.Seeding)
                    {
                        updatedSnapshot = await ApplySeedingPolicyIfNeededAsync(updatedSnapshot, manager, now, cancellationToken);
                    }

                    await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);

                    if (previousState != updatedSnapshot.State)
                    {
                        changes.Add(new TorrentRecoveryChange
                        {
                            TorrentId = snapshot.TorrentId,
                            Name = snapshot.Name,
                            PreviousState = previousState,
                            CurrentState = updatedSnapshot.State,
                        });
                    }
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
            recoveryResult = new TorrentEngineRecoveryResult
            {
                RecoveredTorrentCount = snapshots.Length,
                NormalizedTorrentCount = changes.Count,
                CompletedAtUtc = now,
                Changes = changes,
            };
            _lastRecoveryResult = recoveryResult;
        }
        finally
        {
            _gate.Release();
        }

        await ReconcileRuntimeQueueAsync(cancellationToken);
        return recoveryResult!;
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
            if (updatedSnapshot.State == ContractTorrentState.Seeding)
            {
                updatedSnapshot = await ApplySeedingPolicyIfNeededAsync(updatedSnapshot, entry.Value, now, cancellationToken);
            }

            await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
        }

        await ReconcileRuntimeQueueAsync(cancellationToken);
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

        if (!_recovered)
        {
            await RecoverAsync(cancellationToken);
        }

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
        var persistedSavePath = MonoTorrentSavePathNormalizer.Normalize(manager.SavePath, string.IsNullOrWhiteSpace(magnet.Name) ? null : magnet.Name);

        var snapshot = new TorrentSnapshot
        {
            TorrentId = torrentId,
            Name = string.IsNullOrWhiteSpace(magnet.Name) ? $"Magnet {infoHash[..8]}" : magnet.Name,
            State = ContractTorrentState.Queued,
            MagnetUri = request.MagnetUri.Trim(),
            InfoHash = infoHash,
            DownloadRootPath = downloadRootPath,
            SavePath = persistedSavePath,
            ProgressPercent = 0,
            DownloadedBytes = 0,
            UploadedBytes = 0,
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

        await ReconcileRuntimeQueueAsync(cancellationToken);

        var persistedSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
        return MapDetail(persistedSnapshot);
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

        await ReconcileRuntimeQueueAsync(cancellationToken);

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

        var now = DateTimeOffset.UtcNow;
        var queuedSnapshot = CreateQueuedSnapshot(CreateUpdatedSnapshot(snapshot, manager, now), now);
        queuedSnapshot.ErrorMessage = null;
        await torrentStateStore.UpdateAsync(queuedSnapshot, cancellationToken);

        await manager.StartAsync();

        await ReconcileRuntimeQueueAsync(cancellationToken);

        var updatedSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? queuedSnapshot;

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
        var (snapshot, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);
        var cleanupCandidateDirectories = request.DeleteData
            ? GetCleanupCandidateDirectories(snapshot, manager)
            : Array.Empty<string>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureManagerStoppedAsync(manager, cancellationToken);

            await _engine!.RemoveAsync(
                manager,
                request.DeleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);

            _managers.Remove(torrentId);
            _observedTorrentIds.Remove(torrentId);
            _observedUploadedSessionBytes.TryRemove(torrentId, out _);
        }
        finally
        {
            _gate.Release();
        }

        await torrentStateStore.DeleteAsync(torrentId, cancellationToken);

        if (request.DeleteData)
        {
            TorrentDataPathCleanup.DeleteEmptyDirectories(snapshot.DownloadRootPath ?? servicePaths.DownloadRootPath, cleanupCandidateDirectories);
        }

        await ReconcileRuntimeQueueAsync(cancellationToken);

        return new TorrentActionResultDto
        {
            TorrentId = torrentId,
            Action = "remove",
            State = ContractTorrentState.Removed,
            ProcessedAtUtc = DateTimeOffset.UtcNow,
            DataDeleted = request.DeleteData,
        };
    }

    private static IReadOnlyList<string> GetCleanupCandidateDirectories(TorrentSnapshot snapshot, TorrentManager manager)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(manager.ContainingDirectory))
        {
            directories.Add(Path.GetFullPath(manager.ContainingDirectory));
        }

        foreach (var file in manager.Files)
        {
            AddParentDirectory(directories, file.DownloadCompleteFullPath);
            AddParentDirectory(directories, file.DownloadIncompleteFullPath);
            AddParentDirectory(directories, file.FullPath);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SavePath))
        {
            directories.Add(Path.GetFullPath(snapshot.SavePath));
        }

        return directories.ToArray();
    }

    private static void AddParentDirectory(ISet<string> directories, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var parentDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            directories.Add(parentDirectory);
        }
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

            var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
            var cacheDirectory = Path.Combine(servicePaths.StorageRootPath, "monotorrent-cache");
            Directory.CreateDirectory(cacheDirectory);

            var engineSettingsBuilder = new EngineSettingsBuilder
            {
                AllowLocalPeerDiscovery = _serviceOptions.EngineAllowLocalPeerDiscovery,
                AllowPortForwarding = _serviceOptions.EngineAllowPortForwarding,
                CacheDirectory = cacheDirectory,
                AutoSaveLoadFastResume = true,
                AutoSaveLoadMagnetLinkMetadata = true,
                UsePartialFiles = _serviceOptions.UsePartialFiles,
                MaximumConnections = runtimeSettings.EngineMaximumConnections,
                MaximumHalfOpenConnections = runtimeSettings.EngineMaximumHalfOpenConnections,
                MaximumDownloadRate = runtimeSettings.EngineMaximumDownloadRateBytesPerSecond,
                MaximumUploadRate = runtimeSettings.EngineMaximumUploadRateBytesPerSecond,
                DhtEndPoint = new IPEndPoint(IPAddress.Any, _serviceOptions.EngineDhtPort),
                ListenEndPoints = new Dictionary<string, IPEndPoint>
                {
                    ["ipv4"] = new IPEndPoint(IPAddress.Any, _serviceOptions.EngineListenPort),
                },
            };

            _engine = new ClientEngine(engineSettingsBuilder.ToSettings());
            appliedEngineSettingsState.Set(
                runtimeSettings.EngineMaximumConnections,
                runtimeSettings.EngineMaximumHalfOpenConnections,
                runtimeSettings.EngineMaximumDownloadRateBytesPerSecond,
                runtimeSettings.EngineMaximumUploadRateBytesPerSecond);
            _initialized = true;

            logger.LogInformation("MonoTorrent engine initialized. CacheDirectory={CacheDirectory}", cacheDirectory);

            await activityLogService.WriteAsync(new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Information,
                Category = "engine",
                EventType = "engine.monotorrent.ready",
                Message = "MonoTorrent engine is initialized and ready.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    cacheDirectory,
                    _serviceOptions.EngineListenPort,
                    _serviceOptions.EngineDhtPort,
                    _serviceOptions.EngineAllowPortForwarding,
                    _serviceOptions.EngineAllowLocalPeerDiscovery,
                    runtimeSettings.EngineMaximumConnections,
                    runtimeSettings.EngineMaximumHalfOpenConnections,
                    runtimeSettings.EngineMaximumDownloadRateBytesPerSecond,
                    runtimeSettings.EngineMaximumUploadRateBytesPerSecond,
                    runtimeSettings.EngineConnectionFailureLogBurstLimit,
                    runtimeSettings.EngineConnectionFailureLogWindowSeconds,
                    _serviceOptions.UsePartialFiles,
                    PartialFileSuffix = _serviceOptions.UsePartialFiles ? ".!mt" : string.Empty,
                    SeedingStopMode = runtimeSettings.SeedingStopMode,
                    SeedingStopRatio = runtimeSettings.SeedingStopRatio,
                    SeedingStopMinutes = runtimeSettings.SeedingStopMinutes,
                }),
            }, cancellationToken);
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
        var recoveryDownloadRootPath = MonoTorrentRecoveryPathResolver.ResolveDownloadRootPath(snapshot, servicePaths.DownloadRootPath);
        var manager = await _engine!.AddAsync(magnet, recoveryDownloadRootPath);
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

    private async Task ReconcileRuntimeQueueAsync(CancellationToken cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var managedTorrents = await GetManagedTorrentsAsync(cancellationToken);
        await ReconcileMetadataResolutionQueueAsync(managedTorrents, runtimeSettings.MaxActiveMetadataResolutions, now, cancellationToken);

        managedTorrents = await GetManagedTorrentsAsync(cancellationToken);
        await ReconcileDownloadQueueAsync(managedTorrents, runtimeSettings.MaxActiveDownloads, now, cancellationToken);
    }

    private async Task<List<(TorrentSnapshot Snapshot, TorrentManager Manager)>> GetManagedTorrentsAsync(CancellationToken cancellationToken)
    {
        List<KeyValuePair<Guid, TorrentManager>> managers;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            managers = _managers.ToList();
        }
        finally
        {
            _gate.Release();
        }

        var managedTorrents = new List<(TorrentSnapshot Snapshot, TorrentManager Manager)>(managers.Count);
        foreach (var entry in managers)
        {
            var snapshot = await torrentStateStore.GetAsync(entry.Key, cancellationToken);
            if (snapshot is not null)
            {
                managedTorrents.Add((snapshot, entry.Value));
            }
        }

        return managedTorrents;
    }

    private async Task ReconcileMetadataResolutionQueueAsync(
        IReadOnlyList<(TorrentSnapshot Snapshot, TorrentManager Manager)> managedTorrents,
        int maxActiveMetadataResolutions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = managedTorrents
            .Where(entry => entry.Snapshot.State is not ContractTorrentState.Paused and not ContractTorrentState.Completed and not ContractTorrentState.Error and not ContractTorrentState.Removed)
            .Where(entry => !entry.Manager.HasMetadata && !entry.Manager.Complete)
            .OrderBy(entry => entry.Snapshot.AddedAtUtc)
            .ThenBy(entry => entry.Snapshot.TorrentId)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var (snapshot, manager) = candidates[index];
            if (index < maxActiveMetadataResolutions)
            {
                if (!IsManagerRunning(manager))
                {
                    await manager.StartAsync();
                }

                var updatedSnapshot = CreateUpdatedSnapshot(snapshot, manager, now);
                updatedSnapshot.State = ContractTorrentState.ResolvingMetadata;
                updatedSnapshot.ErrorMessage = null;
                updatedSnapshot.LastActivityAtUtc ??= now;
                await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
                continue;
            }

            if (IsManagerRunning(manager))
            {
                await manager.StopAsync(TimeSpan.FromSeconds(2));
            }

            await torrentStateStore.UpdateAsync(CreateQueuedSnapshot(CreateUpdatedSnapshot(snapshot, manager, now), now), cancellationToken);
        }
    }

    private async Task ReconcileDownloadQueueAsync(
        IReadOnlyList<(TorrentSnapshot Snapshot, TorrentManager Manager)> managedTorrents,
        int maxActiveDownloads,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = managedTorrents
            .Where(entry => entry.Snapshot.State is not ContractTorrentState.Paused and not ContractTorrentState.Completed and not ContractTorrentState.Error and not ContractTorrentState.Removed)
            .Where(entry => entry.Manager.HasMetadata && !entry.Manager.Complete)
            .OrderBy(entry => entry.Snapshot.AddedAtUtc)
            .ThenBy(entry => entry.Snapshot.TorrentId)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var (snapshot, manager) = candidates[index];
            if (index < maxActiveDownloads)
            {
                if (!IsManagerRunning(manager))
                {
                    await manager.StartAsync();
                }

                var updatedSnapshot = CreateUpdatedSnapshot(snapshot, manager, now);
                if (updatedSnapshot.State == ContractTorrentState.Queued && IsManagerRunning(manager))
                {
                    updatedSnapshot.ErrorMessage = null;
                }

                await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
                continue;
            }

            if (IsManagerRunning(manager))
            {
                await manager.StopAsync(TimeSpan.FromSeconds(2));
            }

            await torrentStateStore.UpdateAsync(CreateQueuedSnapshot(CreateUpdatedSnapshot(snapshot, manager, now), now), cancellationToken);
        }
    }

    private TorrentSnapshot CreateUpdatedSnapshot(TorrentSnapshot existing, TorrentManager manager, DateTimeOffset now)
    {
        var state = MapState(manager, existing.State);
        var totalBytes = manager.HasMetadata
            ? manager.Torrent?.Size ?? existing.TotalBytes
            : existing.TotalBytes ?? manager.MagnetLink?.Size;
        var savePath = MonoTorrentSavePathNormalizer.Normalize(manager.SavePath, existing.Name);
        var downloadedBytes = CalculateDownloadedBytes(totalBytes, manager.Progress, existing.DownloadedBytes);
        var uploadedBytes = CalculateUploadedBytes(existing.TorrentId, existing.UploadedBytes, manager.Monitor.DataBytesSent);

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
            DownloadRootPath = existing.DownloadRootPath,
            SavePath = savePath,
            ProgressPercent = manager.Progress,
            DownloadedBytes = downloadedBytes,
            UploadedBytes = uploadedBytes,
            TotalBytes = totalBytes,
            DownloadRateBytesPerSecond = manager.Monitor.DownloadRate,
            UploadRateBytesPerSecond = manager.Monitor.UploadRate,
            TrackerCount = CountTrackers(manager),
            ConnectedPeerCount = manager.OpenConnections,
            AddedAtUtc = existing.AddedAtUtc,
            CompletedAtUtc = ResolveCompletedAtUtc(existing.CompletedAtUtc, state, manager.Progress, manager.Complete, now),
            SeedingStartedAtUtc = ResolveSeedingStartedAtUtc(existing.SeedingStartedAtUtc, state, now),
            LastActivityAtUtc = now,
            ErrorMessage = manager.Error?.Reason.ToString() ?? existing.ErrorMessage,
        };
    }

    private static TorrentSnapshot CreateQueuedSnapshot(TorrentSnapshot snapshot, DateTimeOffset now)
    {
        snapshot.State = ContractTorrentState.Queued;
        snapshot.ConnectedPeerCount = 0;
        snapshot.DownloadRateBytesPerSecond = 0;
        snapshot.UploadRateBytesPerSecond = 0;
        snapshot.LastActivityAtUtc = now;
        return snapshot;
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
                var now = DateTimeOffset.UtcNow;
                var updatedSnapshot = CreateUpdatedSnapshot(snapshot, eventArgs.TorrentManager, now);
                if (updatedSnapshot.State == ContractTorrentState.Seeding)
                {
                    updatedSnapshot = await ApplySeedingPolicyIfNeededAsync(updatedSnapshot, eventArgs.TorrentManager, now, CancellationToken.None);
                }

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
            var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(CancellationToken.None);
            var decision = _connectionFailureLogThrottle.RegisterAttempt(
                $"{torrentId:N}:{eventArgs.Reason}",
                DateTimeOffset.UtcNow,
                runtimeSettings.EngineConnectionFailureLogBurstLimit,
                runtimeSettings.EngineConnectionFailureLogWindowSeconds);
            if (decision == ConnectionFailureLogDecision.Suppress)
            {
                return;
            }

            await activityLogService.WriteAsync(new ActivityLogWriteRequest
            {
                Level = decision == ConnectionFailureLogDecision.ThrottleNotice
                    ? ActivityLogLevel.Information
                    : ActivityLogLevel.Warning,
                Category = "engine",
                EventType = decision == ConnectionFailureLogDecision.ThrottleNotice
                    ? "torrent.engine.connection_failed.throttled"
                    : "torrent.engine.connection_failed",
                Message = decision == ConnectionFailureLogDecision.ThrottleNotice
                    ? $"Repeated MonoTorrent connection failures are being throttled for reason '{eventArgs.Reason}'."
                    : $"MonoTorrent connection attempt failed with reason '{eventArgs.Reason}'.",
                TorrentId = torrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    Reason = eventArgs.Reason.ToString(),
                    PeerUri = eventArgs.Peer.ConnectionUri?.ToString(),
                    WindowSeconds = runtimeSettings.EngineConnectionFailureLogWindowSeconds,
                    BurstLimit = runtimeSettings.EngineConnectionFailureLogBurstLimit,
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

    private long CalculateUploadedBytes(Guid torrentId, long existingUploadedBytes, long currentSessionUploadedBytes)
    {
        if (!_observedUploadedSessionBytes.TryGetValue(torrentId, out var previousSessionUploadedBytes))
        {
            _observedUploadedSessionBytes[torrentId] = currentSessionUploadedBytes;
            return existingUploadedBytes + Math.Max(0L, currentSessionUploadedBytes);
        }

        _observedUploadedSessionBytes[torrentId] = currentSessionUploadedBytes;
        var delta = currentSessionUploadedBytes >= previousSessionUploadedBytes
            ? currentSessionUploadedBytes - previousSessionUploadedBytes
            : currentSessionUploadedBytes;
        return existingUploadedBytes + Math.Max(0L, delta);
    }

    private static DateTimeOffset? ResolveCompletedAtUtc(
        DateTimeOffset? existingCompletedAtUtc,
        ContractTorrentState state,
        double progressPercent,
        bool isComplete,
        DateTimeOffset now)
    {
        var isCompletedState = state is ContractTorrentState.Completed or ContractTorrentState.Seeding;
        var reachedFullProgress = progressPercent >= 100d;

        return isComplete || isCompletedState || reachedFullProgress
            ? existingCompletedAtUtc ?? now
            : null;
    }

    private static DateTimeOffset? ResolveSeedingStartedAtUtc(
        DateTimeOffset? existingSeedingStartedAtUtc,
        ContractTorrentState state,
        DateTimeOffset now)
    {
        return state == ContractTorrentState.Seeding
            ? existingSeedingStartedAtUtc ?? now
            : existingSeedingStartedAtUtc;
    }

    private async Task<SeedingPolicyDecision> ShouldStopSeedingAsync(TorrentSnapshot snapshot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);

        return SeedingPolicyEvaluator.Evaluate(
            runtimeSettings.SeedingStopMode,
            runtimeSettings.SeedingStopRatio,
            runtimeSettings.SeedingStopMinutes,
            snapshot.UploadedBytes,
            snapshot.TotalBytes,
            snapshot.SeedingStartedAtUtc,
            now);
    }

    private async Task<TorrentSnapshot> ApplySeedingPolicyIfNeededAsync(
        TorrentSnapshot snapshot,
        TorrentManager manager,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var seedingDecision = SeedingPolicyEvaluator.Evaluate(
            runtimeSettings.SeedingStopMode,
            runtimeSettings.SeedingStopRatio,
            runtimeSettings.SeedingStopMinutes,
            snapshot.UploadedBytes,
            snapshot.TotalBytes,
            snapshot.SeedingStartedAtUtc,
            now);
        if (!seedingDecision.ShouldStop)
        {
            return snapshot;
        }

        if (manager.State is not MonoTorrent.Client.TorrentState.Stopped and not MonoTorrent.Client.TorrentState.Paused)
        {
            await manager.StopAsync(TimeSpan.FromSeconds(2));
        }

        var completedSnapshot = new TorrentSnapshot
        {
            TorrentId = snapshot.TorrentId,
            Name = snapshot.Name,
            State = ContractTorrentState.Completed,
            MagnetUri = snapshot.MagnetUri,
            InfoHash = snapshot.InfoHash,
            DownloadRootPath = snapshot.DownloadRootPath,
            SavePath = snapshot.SavePath,
            ProgressPercent = snapshot.ProgressPercent,
            DownloadedBytes = snapshot.DownloadedBytes,
            UploadedBytes = snapshot.UploadedBytes,
            TotalBytes = snapshot.TotalBytes,
            ConnectedPeerCount = 0,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = snapshot.TrackerCount,
            AddedAtUtc = snapshot.AddedAtUtc,
            CompletedAtUtc = snapshot.CompletedAtUtc,
            SeedingStartedAtUtc = snapshot.SeedingStartedAtUtc,
            LastActivityAtUtc = now,
            ErrorMessage = snapshot.ErrorMessage,
        };

        await activityLogService.WriteAsync(new ActivityLogWriteRequest
        {
            Level = ActivityLogLevel.Information,
            Category = "torrent",
            EventType = "torrent.seeding.stopped_policy",
            Message = $"Stopped seeding for torrent '{snapshot.Name}' because the '{seedingDecision.Reason}' policy was reached.",
            TorrentId = snapshot.TorrentId,
            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                seedingDecision.Reason,
                seedingDecision.CurrentRatio,
                seedingDecision.CurrentSeedingMinutes,
                SeedingStopMode = runtimeSettings.SeedingStopMode,
                SeedingStopRatio = runtimeSettings.SeedingStopRatio,
                SeedingStopMinutes = runtimeSettings.SeedingStopMinutes,
            }),
        }, cancellationToken);

        return completedSnapshot;
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

    private static bool IsManagerRunning(TorrentManager manager) =>
        manager.State is not MonoTorrent.Client.TorrentState.Stopped
            and not MonoTorrent.Client.TorrentState.Paused
            and not MonoTorrent.Client.TorrentState.Error;

    private static async Task EnsureManagerStoppedAsync(TorrentManager manager, CancellationToken cancellationToken)
    {
        if (manager.State == MonoTorrent.Client.TorrentState.Stopped)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await manager.StopAsync();
    }

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
