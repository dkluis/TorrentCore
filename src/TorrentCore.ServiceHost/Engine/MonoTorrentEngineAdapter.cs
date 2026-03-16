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
using TorrentCore.Service.Callbacks;
using TorrentCore.Service.Configuration;
using ContractTorrentState = TorrentCore.Contracts.Torrents.TorrentState;

namespace TorrentCore.Service.Engine;

public sealed class MonoTorrentEngineAdapter(
    ITorrentStateStore torrentStateStore,
    IActivityLogService activityLogService,
    ITorrentCompletionCallbackInvoker completionCallbackInvoker,
    ResolvedTorrentCoreServicePaths servicePaths,
    IOptions<TorrentCoreServiceOptions> serviceOptions,
    IRuntimeSettingsService runtimeSettingsService,
    AppliedEngineSettingsState appliedEngineSettingsState,
    ServiceInstanceContext serviceInstanceContext,
    ILogger<MonoTorrentEngineAdapter> logger) : ITorrentEngineAdapter, IHostedService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _synchronizationGate = new(1, 1);
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
        _synchronizationGate.Dispose();
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

                    if (ShouldStartOnRecovery(snapshot))
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

        await SynchronizeAsync(cancellationToken);
        return recoveryResult!;
    }

    private async Task<IReadOnlyList<TorrentSnapshot>> GetProjectedSnapshotsAsync(CancellationToken cancellationToken)
    {
        var persistedSnapshots = await torrentStateStore.ListAsync(cancellationToken);
        if (persistedSnapshots.Count == 0)
        {
            return persistedSnapshots;
        }

        Dictionary<Guid, TorrentManager> managers;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            managers = _managers.ToDictionary();
        }
        finally
        {
            _gate.Release();
        }

        if (managers.Count == 0)
        {
            return persistedSnapshots;
        }

        return persistedSnapshots
            .Select(snapshot => managers.TryGetValue(snapshot.TorrentId, out var manager)
                ? CreateReadProjectedSnapshot(snapshot, manager)
                : snapshot)
            .ToArray();
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            await SynchronizeCoreAsync(cancellationToken);
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }

    public async Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        var torrents = await GetProjectedSnapshotsAsync(cancellationToken);
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var diagnostics = TorrentQueueDiagnostics.Create(torrents, runtimeSettings);
        return torrents.Select(snapshot => MapSummary(snapshot, diagnostics[snapshot.TorrentId])).ToArray();
    }

    public async Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrents = await GetProjectedSnapshotsAsync(cancellationToken);
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

    public async Task<TorrentDetailDto> AddMagnetAsync(
        AddMagnetRequest request,
        ResolvedTorrentCategorySelection categorySelection,
        CancellationToken cancellationToken)
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
        var manager = await _engine!.AddAsync(magnet, categorySelection.DownloadRootPath);
        var torrentId = Guid.NewGuid();
        RegisterManager(torrentId, manager);
        var persistedSavePath = MonoTorrentSavePathNormalizer.Normalize(manager.SavePath, string.IsNullOrWhiteSpace(magnet.Name) ? null : magnet.Name);

        var snapshot = new TorrentSnapshot
        {
            TorrentId = torrentId,
            Name = string.IsNullOrWhiteSpace(magnet.Name) ? $"Magnet {infoHash[..8]}" : magnet.Name,
            CategoryKey = categorySelection.CategoryKey,
            CompletionCallbackLabel = categorySelection.CompletionCallbackLabel,
            InvokeCompletionCallback = categorySelection.InvokeCompletionCallback,
            State = ContractTorrentState.Queued,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = request.MagnetUri.Trim(),
            InfoHash = infoHash,
            DownloadRootPath = categorySelection.DownloadRootPath,
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

        await SynchronizeAsync(cancellationToken);

        var persistedSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
        return MapDetail(persistedSnapshot, new TorrentQueueDiagnostic(null, null));
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

        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            var currentSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
            var now = DateTimeOffset.UtcNow;
            var updatedSnapshot = CreatePausedSnapshot(CreateUpdatedSnapshot(currentSnapshot, manager, now), now);
            await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);

            await SynchronizeCoreAsync(cancellationToken);

            return new TorrentActionResultDto
            {
                TorrentId = torrentId,
                Action = "pause",
                State = updatedSnapshot.State,
                ProcessedAtUtc = now,
                DataDeleted = false,
            };
        }
        finally
        {
            _synchronizationGate.Release();
        }
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

        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            var currentSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
            var now = DateTimeOffset.UtcNow;
            var queuedSnapshot = CreateQueuedSnapshot(CreateUpdatedSnapshot(currentSnapshot, manager, now), now);
            queuedSnapshot.ErrorMessage = null;
            await torrentStateStore.UpdateAsync(queuedSnapshot, cancellationToken);

            await SynchronizeCoreAsync(cancellationToken);

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
        finally
        {
            _synchronizationGate.Release();
        }
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

        await SynchronizeAsync(cancellationToken);

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
        await ReconcileSeedingQueueAsync(managedTorrents, now, cancellationToken);

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

    private async Task SynchronizeCoreAsync(CancellationToken cancellationToken)
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
        var callbackSnapshots = new List<TorrentSnapshot>();

        foreach (var entry in managers)
        {
            var snapshot = await torrentStateStore.GetAsync(entry.Key, cancellationToken);
            if (snapshot is null)
            {
                continue;
            }

            TorrentSnapshot updatedSnapshot;

            if (snapshot.DesiredState == TorrentDesiredState.Paused)
            {
                if (IsManagerRunning(entry.Value))
                {
                    await EnsureManagerStoppedAsync(entry.Value, cancellationToken);
                }

                updatedSnapshot = CreatePausedSnapshot(CreateUpdatedSnapshot(snapshot, entry.Value, now), now);
            }
            else
            {
                updatedSnapshot = CreateUpdatedSnapshot(snapshot, entry.Value, now);
                if (updatedSnapshot.State == ContractTorrentState.Seeding)
                {
                    updatedSnapshot = await ApplySeedingPolicyIfNeededAsync(updatedSnapshot, entry.Value, now, cancellationToken);
                }
            }

            var previousCompletedAtUtc = snapshot.CompletedAtUtc;
            await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);

            if (previousCompletedAtUtc is null && updatedSnapshot.CompletedAtUtc is not null)
            {
                callbackSnapshots.Add(updatedSnapshot);
            }
        }

        await ReconcileRuntimeQueueAsync(cancellationToken);

        foreach (var callbackSnapshot in callbackSnapshots)
        {
            await completionCallbackInvoker.InvokeIfTriggeredAsync(null, callbackSnapshot, cancellationToken);
        }
    }

    private async Task ReconcileMetadataResolutionQueueAsync(
        IReadOnlyList<(TorrentSnapshot Snapshot, TorrentManager Manager)> managedTorrents,
        int maxActiveMetadataResolutions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = managedTorrents
            .Where(entry => entry.Snapshot.DesiredState == TorrentDesiredState.Runnable)
            .Where(entry => entry.Snapshot.State is not ContractTorrentState.Completed and not ContractTorrentState.Error and not ContractTorrentState.Removed)
            .Where(entry => !entry.Manager.HasMetadata && !entry.Manager.Complete)
            .OrderBy(entry => entry.Snapshot.AddedAtUtc)
            .ThenBy(entry => entry.Snapshot.TorrentId)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var (snapshot, manager) = candidates[index];
            var currentSnapshot = await torrentStateStore.GetAsync(snapshot.TorrentId, cancellationToken);
            if (currentSnapshot is null ||
                currentSnapshot.DesiredState == TorrentDesiredState.Paused ||
                currentSnapshot.State is ContractTorrentState.Completed or ContractTorrentState.Error or ContractTorrentState.Removed)
            {
                continue;
            }

            if (index < maxActiveMetadataResolutions)
            {
                await EnsureManagerStartedAsync(manager, cancellationToken);

                var updatedSnapshot = CreateUpdatedSnapshot(currentSnapshot, manager, now);
                updatedSnapshot.State = ContractTorrentState.ResolvingMetadata;
                updatedSnapshot.ErrorMessage = null;
                updatedSnapshot.LastActivityAtUtc ??= now;
                await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
                continue;
            }

            if (IsManagerRunning(manager))
            {
                await EnsureManagerStoppedAsync(manager, cancellationToken);
            }

            await torrentStateStore.UpdateAsync(CreateQueuedSnapshot(CreateUpdatedSnapshot(currentSnapshot, manager, now), now), cancellationToken);
        }
    }

    private async Task ReconcileSeedingQueueAsync(
        IReadOnlyList<(TorrentSnapshot Snapshot, TorrentManager Manager)> managedTorrents,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = managedTorrents
            .Where(entry => entry.Snapshot.DesiredState == TorrentDesiredState.Runnable)
            .Where(entry => entry.Snapshot.State is not ContractTorrentState.Completed and not ContractTorrentState.Error and not ContractTorrentState.Removed)
            .Where(entry => entry.Manager.HasMetadata && entry.Manager.Complete)
            .OrderBy(entry => entry.Snapshot.AddedAtUtc)
            .ThenBy(entry => entry.Snapshot.TorrentId)
            .ToList();

        foreach (var (snapshot, manager) in candidates)
        {
            var currentSnapshot = await torrentStateStore.GetAsync(snapshot.TorrentId, cancellationToken);
            if (currentSnapshot is null ||
                currentSnapshot.DesiredState == TorrentDesiredState.Paused ||
                currentSnapshot.State is ContractTorrentState.Completed or ContractTorrentState.Error or ContractTorrentState.Removed)
            {
                continue;
            }

            await EnsureManagerStartedAsync(manager, cancellationToken);

            var updatedSnapshot = CreateUpdatedSnapshot(currentSnapshot, manager, now);
            updatedSnapshot.State = ContractTorrentState.Seeding;
            updatedSnapshot = await ApplySeedingPolicyIfNeededAsync(updatedSnapshot, manager, now, cancellationToken);
            await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
        }
    }

    private async Task ReconcileDownloadQueueAsync(
        IReadOnlyList<(TorrentSnapshot Snapshot, TorrentManager Manager)> managedTorrents,
        int maxActiveDownloads,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = managedTorrents
            .Where(entry => entry.Snapshot.DesiredState == TorrentDesiredState.Runnable)
            .Where(entry => entry.Snapshot.State is not ContractTorrentState.Completed and not ContractTorrentState.Error and not ContractTorrentState.Removed)
            .Where(entry => entry.Manager.HasMetadata && !entry.Manager.Complete)
            .OrderBy(entry => entry.Snapshot.AddedAtUtc)
            .ThenBy(entry => entry.Snapshot.TorrentId)
            .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var (snapshot, manager) = candidates[index];
            var currentSnapshot = await torrentStateStore.GetAsync(snapshot.TorrentId, cancellationToken);
            if (currentSnapshot is null ||
                currentSnapshot.DesiredState == TorrentDesiredState.Paused ||
                currentSnapshot.State is ContractTorrentState.Completed or ContractTorrentState.Error or ContractTorrentState.Removed)
            {
                continue;
            }

            if (index < maxActiveDownloads)
            {
                await EnsureManagerStartedAsync(manager, cancellationToken);

                var updatedSnapshot = CreateUpdatedSnapshot(currentSnapshot, manager, now);
                if (updatedSnapshot.State == ContractTorrentState.Queued && IsManagerRunning(manager))
                {
                    updatedSnapshot.ErrorMessage = null;
                }

                await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
                continue;
            }

            if (IsManagerRunning(manager))
            {
                await EnsureManagerStoppedAsync(manager, cancellationToken);
            }

            await torrentStateStore.UpdateAsync(CreateQueuedSnapshot(CreateUpdatedSnapshot(currentSnapshot, manager, now), now), cancellationToken);
        }
    }

    private TorrentSnapshot CreateUpdatedSnapshot(TorrentSnapshot existing, TorrentManager manager, DateTimeOffset now)
    {
        var state = MapState(manager, existing.State, existing.DesiredState);
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
            CategoryKey = existing.CategoryKey,
            CompletionCallbackLabel = existing.CompletionCallbackLabel,
            InvokeCompletionCallback = existing.InvokeCompletionCallback,
            State = state,
            DesiredState = existing.DesiredState,
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

    private static TorrentSnapshot CreateReadProjectedSnapshot(TorrentSnapshot existing, TorrentManager manager)
    {
        var state = MapState(manager, existing.State, existing.DesiredState);
        var totalBytes = manager.HasMetadata
            ? manager.Torrent?.Size ?? existing.TotalBytes
            : existing.TotalBytes ?? manager.MagnetLink?.Size;

        if (manager.HasMetadata && state == ContractTorrentState.ResolvingMetadata)
        {
            state = manager.Complete ? ContractTorrentState.Seeding : ContractTorrentState.Downloading;
        }

        var projectedSnapshot = new TorrentSnapshot
        {
            TorrentId = existing.TorrentId,
            Name = string.IsNullOrWhiteSpace(manager.Name) ? existing.Name : manager.Name,
            CategoryKey = existing.CategoryKey,
            CompletionCallbackLabel = existing.CompletionCallbackLabel,
            InvokeCompletionCallback = existing.InvokeCompletionCallback,
            State = state,
            DesiredState = existing.DesiredState,
            MagnetUri = existing.MagnetUri,
            InfoHash = manager.InfoHashes.V1OrV2.ToHex().ToUpperInvariant(),
            DownloadRootPath = existing.DownloadRootPath,
            SavePath = MonoTorrentSavePathNormalizer.Normalize(manager.SavePath, existing.Name),
            ProgressPercent = manager.Progress,
            DownloadedBytes = CalculateDownloadedBytes(totalBytes, manager.Progress, existing.DownloadedBytes),
            UploadedBytes = existing.UploadedBytes,
            TotalBytes = totalBytes,
            DownloadRateBytesPerSecond = manager.Monitor.DownloadRate,
            UploadRateBytesPerSecond = manager.Monitor.UploadRate,
            TrackerCount = CountTrackers(manager),
            ConnectedPeerCount = manager.OpenConnections,
            AddedAtUtc = existing.AddedAtUtc,
            CompletedAtUtc = existing.CompletedAtUtc,
            SeedingStartedAtUtc = existing.SeedingStartedAtUtc,
            LastActivityAtUtc = existing.LastActivityAtUtc,
            ErrorMessage = state == ContractTorrentState.Error
                ? manager.Error?.Reason.ToString() ?? existing.ErrorMessage
                : null,
        };

        if (state is ContractTorrentState.Paused or ContractTorrentState.Queued or ContractTorrentState.Completed or ContractTorrentState.Error)
        {
            projectedSnapshot.ConnectedPeerCount = 0;
            projectedSnapshot.DownloadRateBytesPerSecond = 0;
            projectedSnapshot.UploadRateBytesPerSecond = 0;
        }

        return projectedSnapshot;
    }

    private static TorrentSnapshot CreateQueuedSnapshot(TorrentSnapshot snapshot, DateTimeOffset now)
    {
        snapshot.DesiredState = TorrentDesiredState.Runnable;
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
                    ContractState = MapState(
                        eventArgs.TorrentManager,
                        snapshot?.State ?? ContractTorrentState.Queued,
                        snapshot?.DesiredState ?? TorrentDesiredState.Runnable).ToString(),
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
            CategoryKey = snapshot.CategoryKey,
            CompletionCallbackLabel = snapshot.CompletionCallbackLabel,
            InvokeCompletionCallback = snapshot.InvokeCompletionCallback,
            State = ContractTorrentState.Completed,
            DesiredState = snapshot.DesiredState,
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

    private static ContractTorrentState MapState(TorrentManager manager, ContractTorrentState existingState, TorrentDesiredState desiredState)
    {
        if (desiredState == TorrentDesiredState.Paused && manager.State is not MonoTorrent.Client.TorrentState.Error)
        {
            return ContractTorrentState.Paused;
        }

        return manager.State switch
        {
            MonoTorrent.Client.TorrentState.Metadata => ContractTorrentState.ResolvingMetadata,
            MonoTorrent.Client.TorrentState.Downloading => ContractTorrentState.Downloading,
            MonoTorrent.Client.TorrentState.Seeding => ContractTorrentState.Seeding,
            MonoTorrent.Client.TorrentState.Error => ContractTorrentState.Error,
            MonoTorrent.Client.TorrentState.Paused => desiredState == TorrentDesiredState.Paused ? ContractTorrentState.Paused : ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Hashing or MonoTorrent.Client.TorrentState.HashingPaused or MonoTorrent.Client.TorrentState.FetchingHashes => ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Starting => ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Stopping => desiredState == TorrentDesiredState.Paused ? ContractTorrentState.Paused : existingState == ContractTorrentState.Completed ? ContractTorrentState.Completed : ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Stopped => existingState switch
            {
                ContractTorrentState.Completed => ContractTorrentState.Completed,
                _ when manager.Complete => ContractTorrentState.Queued,
                _ => ContractTorrentState.Queued,
            },
            _ => existingState,
        };
    }

    private static bool ShouldStartOnRecovery(TorrentSnapshot snapshot) =>
        snapshot.DesiredState == TorrentDesiredState.Runnable &&
        snapshot.State is not ContractTorrentState.Completed and not ContractTorrentState.Error and not ContractTorrentState.Removed;

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
        if (manager.State is MonoTorrent.Client.TorrentState.Stopped or MonoTorrent.Client.TorrentState.Stopping)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await manager.StopAsync();
    }

    private static async Task EnsureManagerStartedAsync(TorrentManager manager, CancellationToken cancellationToken)
    {
        await WaitForManagerToBecomeRestartableAsync(manager, cancellationToken);

        if (!IsManagerRunning(manager))
        {
            await manager.StartAsync();
        }
    }

    private static async Task WaitForManagerToBecomeRestartableAsync(TorrentManager manager, CancellationToken cancellationToken)
    {
        if (manager.State != MonoTorrent.Client.TorrentState.Stopping)
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        while (manager.State == MonoTorrent.Client.TorrentState.Stopping)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow - startedAt > TimeSpan.FromSeconds(2))
            {
                throw new ServiceOperationException(
                    "torrent_resume_pending_stop",
                    "Torrent is still stopping. Try the resume request again in a moment.",
                    StatusCodes.Status409Conflict,
                    nameof(manager));
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private static TorrentSnapshot CreatePausedSnapshot(TorrentSnapshot snapshot, DateTimeOffset now)
    {
        snapshot.DesiredState = TorrentDesiredState.Paused;
        snapshot.State = ContractTorrentState.Paused;
        snapshot.ConnectedPeerCount = 0;
        snapshot.DownloadRateBytesPerSecond = 0;
        snapshot.UploadRateBytesPerSecond = 0;
        snapshot.LastActivityAtUtc = now;
        return snapshot;
    }

    private static TorrentSummaryDto MapSummary(TorrentSnapshot snapshot, TorrentQueueDiagnostic diagnostic)
    {
        return new TorrentSummaryDto
        {
            TorrentId = snapshot.TorrentId,
            Name = snapshot.Name,
            CategoryKey = snapshot.CategoryKey,
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
            CanRemove = snapshot.State is not ContractTorrentState.Removed,
        };
    }

    private static TorrentDetailDto MapDetail(TorrentSnapshot snapshot, TorrentQueueDiagnostic diagnostic)
    {
        return new TorrentDetailDto
        {
            TorrentId = snapshot.TorrentId,
            Name = snapshot.Name,
            CategoryKey = snapshot.CategoryKey,
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
            CanRemove = snapshot.State is not ContractTorrentState.Removed,
        };
    }
}
