#region

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Application;
using TorrentCore.Service.Callbacks;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;
using ContractTorrentState = TorrentCore.Contracts.Torrents.TorrentState;

#endregion

namespace TorrentCore.Service.Engine;

public sealed class MonoTorrentEngineAdapter(ITorrentStateStore torrentStateStore,
    IActivityLogService activityLogService, ITorrentCompletionCallbackProcessor completionCallbackProcessor,
    ITorrentHistoryService torrentHistoryService,
    ITorrentCompletionFinalizationChecker finalizationChecker, ResolvedTorrentCoreServicePaths servicePaths,
    TorrentCompletionFinalizationProbeCoordinator finalizationProbeCoordinator,
    TorrentManagerStopCoordinator managerStopCoordinator,
    IOptions<TorrentCoreServiceOptions> serviceOptions, IRuntimeSettingsService runtimeSettingsService,
    AppliedEngineSettingsState appliedEngineSettingsState, ServiceInstanceContext serviceInstanceContext,
    ITorrentRemovalCleanupScheduler torrentRemovalCleanupScheduler,
    RuntimeOperationDurationDiagnostics durationDiagnostics,
    ILogger<MonoTorrentEngineAdapter> logger) : ITorrentEngineAdapter, IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan ConnectionActivitySummaryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RecoveryTrackerAnnounceTimeout = TimeSpan.FromSeconds(10);
    private readonly CancellationTokenSource                                  _backgroundOperationCts = new();
    private readonly ConnectionActivitySummaryTracker                          _connectionActivitySummaries = new();
    private readonly ConcurrentDictionary<Guid, int>                          _downloadRecoveryAttemptCounts = new();
    private readonly ConcurrentDictionary<Guid, TorrentDownloadRecoveryState> _downloadRecoveryStates = new();
    private readonly SemaphoreSlim                                            _gate = new(1, 1);
    private readonly Dictionary<Guid, TorrentManager>                         _managers = new();
    private readonly ConcurrentDictionary<Guid, int>                          _metadataRecoveryAttemptCounts = new();
    private readonly ConcurrentDictionary<Guid, TorrentMetadataRecoveryState> _metadataRecoveryStates = new();
    private readonly ConcurrentDictionary<Guid, Task>                         _peerDiscoveryAnnounceTasks = new();
    private readonly HashSet<Guid>                                            _observedTorrentIds = [];
    private readonly ConcurrentDictionary<Guid, long>                         _observedUploadedSessionBytes = new();
    private readonly ConcurrentDictionary<TorrentManager, Guid>               _torrentIdsByManager = new();
    private readonly TorrentCoreServiceOptions                                _serviceOptions = serviceOptions.Value;
    private readonly SemaphoreSlim                                            _synchronizationGate = new(1, 1);
    private          int                                                      _disposeState;
    private          ClientEngine?                                            _engine;
    private          bool                                                     _initialized;
    private          TorrentEngineRecoveryResult?                             _lastRecoveryResult;
    private          bool                                                     _recovered;

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
        _backgroundOperationCts.Dispose();
    }

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

        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_engine is null)
            {
                return;
            }

            await StopBackgroundOperationsAsync();

            await FlushManagedSnapshotsForShutdownAsync(cancellationToken);

            foreach (var manager in _managers.Values)
            {
                try
                {
                    if (manager.State is not MonoTorrent.Client.TorrentState.Stopped and
                        not MonoTorrent.Client.TorrentState.Paused)
                    {
                        await EnsureManagerStoppedAsync(manager, cancellationToken);
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed stopping MonoTorrent manager {ManagerName}", manager.Name);
                }
            }

            await _engine.StopAllAsync(TimeSpan.FromSeconds(2));
            foreach (var torrentId in _managers.Keys)
            {
                finalizationProbeCoordinator.Remove(torrentId);
                managerStopCoordinator.Remove(torrentId);
            }
            _managers.Clear();
            _observedTorrentIds.Clear();
            _observedUploadedSessionBytes.Clear();
            _torrentIdsByManager.Clear();
            _downloadRecoveryStates.Clear();
            _downloadRecoveryAttemptCounts.Clear();
            _metadataRecoveryStates.Clear();
            _metadataRecoveryAttemptCounts.Clear();
            _connectionActivitySummaries.Clear();
            _peerDiscoveryAnnounceTasks.Clear();
            _recovered          = false;
            _lastRecoveryResult = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<int> GetTorrentCountAsync(CancellationToken cancellationToken)
    {
        return torrentStateStore.CountAsync(cancellationToken);
    }

    public async Task<TorrentEngineRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var                          snapshots      = Array.Empty<TorrentSnapshot>();
        var                          changes        = new List<TorrentRecoveryChange>();
        var                          now            = DateTimeOffset.UtcNow;
        var                          runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
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
                    var manager         = await AddOrGetManagerAsync(snapshot, cancellationToken);
                    var updatedSnapshot = CreateUpdatedSnapshot(snapshot, manager, now);
                    var previousState   = snapshot.State;
                    TorrentCompletionFinalizationCheckResult? finalizationResult = null;
                    var observedFiles = GetObservedFilePaths(manager);
                    if (manager.HasMetadata && LooksTransferComplete(updatedSnapshot))
                    {
                        finalizationProbeCoordinator.TryTakeCompletedOrSchedule(
                            updatedSnapshot,
                            runtimeSettings,
                            observedFiles,
                            out finalizationResult
                        );
                    }

                    if (ShouldPreservePersistedCompletion(snapshot, manager))
                    {
                        updatedSnapshot = CreateRecoveredCompletedSnapshot(snapshot, updatedSnapshot, now);
                    }
                    else
                    {
                        updatedSnapshot = NormalizeCompletedErrorIfPayloadVisible(
                            updatedSnapshot,
                            finalizationResult,
                            now
                        );
                    }

                    if (ShouldStartOnRecovery(snapshot))
                    {
                        if (updatedSnapshot.State != ContractTorrentState.Completed &&
                            updatedSnapshot.State != ContractTorrentState.Paused    &&
                            updatedSnapshot.State != ContractTorrentState.Error)
                        {
                            updatedSnapshot = CreateQueuedSnapshot(updatedSnapshot, now);
                        }
                    }

                    if (updatedSnapshot.State == ContractTorrentState.Seeding)
                    {
                        var seedingPolicyResult = await ApplySeedingPolicyIfNeededAsync(
                            updatedSnapshot,
                            manager,
                            now,
                            cancellationToken,
                            finalizationResult
                        );
                        updatedSnapshot = seedingPolicyResult.Snapshot;
                    }

                    await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
                    await torrentHistoryService.ObserveSnapshotAsync(updatedSnapshot, cancellationToken);

                    if (previousState != updatedSnapshot.State)
                    {
                        changes.Add(
                            new TorrentRecoveryChange
                            {
                                TorrentId     = snapshot.TorrentId,
                                Name          = snapshot.Name,
                                PreviousState = previousState,
                                CurrentState  = updatedSnapshot.State,
                            }
                        );
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception, "Failed to recover torrent {TorrentId} ({TorrentName})", snapshot.TorrentId,
                        snapshot.Name
                    );

                    var previousState = snapshot.State;
                    snapshot.State             = ContractTorrentState.Error;
                    snapshot.ErrorMessage      = exception.Message;
                    snapshot.LastActivityAtUtc = now;
                    await torrentStateStore.UpdateAsync(snapshot, cancellationToken);
                    await torrentHistoryService.ObserveSnapshotAsync(snapshot, cancellationToken);

                    changes.Add(
                        new TorrentRecoveryChange
                        {
                            TorrentId     = snapshot.TorrentId,
                            Name          = snapshot.Name,
                            PreviousState = previousState,
                            CurrentState  = ContractTorrentState.Error,
                        }
                    );
                }
            }

            _recovered = true;
            recoveryResult = new TorrentEngineRecoveryResult
            {
                RecoveredTorrentCount  = snapshots.Length,
                NormalizedTorrentCount = changes.Count,
                CompletedAtUtc         = now,
                Changes                = changes,
            };
            _lastRecoveryResult = recoveryResult;
        }
        finally
        {
            _gate.Release();
        }

        await SynchronizeWithoutAutomaticRecoveryAsync(cancellationToken);
        return recoveryResult!;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await SynchronizeInternalAsync(cancellationToken, includeAutomaticRecovery: true);
    }

    private async Task SynchronizeWithoutAutomaticRecoveryAsync(CancellationToken cancellationToken)
    {
        await SynchronizeInternalAsync(cancellationToken, includeAutomaticRecovery: false);
    }

    private async Task SynchronizeInternalAsync(CancellationToken cancellationToken, bool includeAutomaticRecovery)
    {
        List<PendingCallbackWork> pendingCallbackWork;

        var gateWaitStopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _synchronizationGate.WaitAsync(cancellationToken);
        gateWaitStopwatch.Stop();
        await durationDiagnostics.RecordIfSlowAsync(
            "engine",
            "synchronization_gate_wait",
            gateWaitStopwatch.Elapsed,
            RuntimeOperationDurationDiagnostics.GateWaitSlowThreshold,
            "acquired"
        );

        var synchronizationStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var synchronizationOutcome = "succeeded";
        try
        {
            pendingCallbackWork =
                    await SynchronizeCoreAsync(cancellationToken, includeAutomaticRecovery);
        }
        catch
        {
            synchronizationOutcome = "failed";
            throw;
        }
        finally
        {
            _synchronizationGate.Release();
            synchronizationStopwatch.Stop();
            await durationDiagnostics.RecordIfSlowAsync(
                "engine",
                "serialized_synchronization",
                synchronizationStopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.SynchronizationSlowThreshold,
                synchronizationOutcome
            );
        }

        await ProcessPendingCallbacksAsync(pendingCallbackWork, cancellationToken);
    }

    public async Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        var torrents        = await GetProjectedSnapshotsAsync(cancellationToken);
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var diagnostics     = TorrentQueueDiagnostics.Create(torrents, runtimeSettings);
        return torrents.Select(snapshot => MapSummary(snapshot, diagnostics[snapshot.TorrentId])).ToArray();
    }

    public async Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrents         = await GetProjectedSnapshotsAsync(cancellationToken);
        var torrent          = torrents.SingleOrDefault(snapshot => snapshot.TorrentId == torrentId);
        var runtimeSettings  = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var queueDiagnostics = TorrentQueueDiagnostics.Create(torrents, runtimeSettings);
        var manager          = await TryGetManagerAsync(torrentId, cancellationToken);

        return torrent is null ?
                throw new ServiceOperationException(
                    "torrent_not_found", $"Torrent '{torrentId}' was not found.", StatusCodes.Status404NotFound,
                    nameof(torrentId)
                ) : MapDetail(
                    torrent, queueDiagnostics[torrent.TorrentId], runtimeSettings, manager
                );
    }

    public async Task<IReadOnlyList<TorrentPeerDto>> GetTorrentPeersAsync(Guid torrentId,
        CancellationToken                                                      cancellationToken)
    {
        var (_, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);
        var peers = await manager.GetPeersAsync();

        return peers
              .Select(
                   peer => new TorrentPeerDto
                   {
                       Endpoint = peer.Uri.ToString(),
                       Client = peer.ClientApp.ToString(),
                       Direction = peer.ConnectionDirection.ToString(),
                       IsConnected = peer.IsConnected,
                       IsSeeder = peer.IsSeeder,
                       DownloadRateBytesPerSecond = peer.Monitor.DownloadRate,
                       UploadRateBytesPerSecond = peer.Monitor.UploadRate,
                       DownloadedBytes = peer.Monitor.DataBytesReceived,
                       UploadedBytes = peer.Monitor.DataBytesSent,
                       Encryption = peer.EncryptionType.ToString(),
                   }
               )
              .OrderByDescending(peer => peer.DownloadRateBytesPerSecond)
              .ThenByDescending(peer => peer.UploadRateBytesPerSecond)
              .ThenBy(peer => peer.Endpoint, StringComparer.OrdinalIgnoreCase)
              .ToArray();
    }

    public async Task<IReadOnlyList<TorrentTrackerDto>> GetTorrentTrackersAsync(Guid torrentId,
        CancellationToken                                                         cancellationToken)
    {
        var (_, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);
        var tiers = manager.TrackerManager?.Tiers;
        if (tiers is null || tiers.Count == 0)
        {
            return Array.Empty<TorrentTrackerDto>();
        }

        var trackers = new List<TorrentTrackerDto>();

        for (var tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
        {
            var tier = tiers[tierIndex];

            for (var trackerIndex = 0; trackerIndex < tier.Trackers.Count; trackerIndex++)
            {
                var tracker = tier.Trackers[trackerIndex];
                var concreteTracker = tracker as MonoTorrent.Trackers.Tracker;

                trackers.Add(
                    new TorrentTrackerDto
                    {
                        TierNumber = tierIndex + 1,
                        TrackerNumber = trackerIndex + 1,
                        IsActive = ReferenceEquals(tier.ActiveTracker, tracker),
                        Status = tracker.Status.ToString(),
                        CanAnnounce = concreteTracker?.CanAnnounce,
                        CanScrape = tracker.CanScrape,
                        TimeSinceLastAnnounceSeconds = (long) Math.Max(0, tier.TimeSinceLastAnnounce.TotalSeconds),
                        LastAnnounceSucceeded = tier.LastAnnounceSucceeded,
                        TimeSinceLastScrapeSeconds = (long) Math.Max(0, tier.TimeSinceLastScrape.TotalSeconds),
                        LastScrapeSucceeded = tier.LastScrapeSucceeded,
                        FailureMessage = string.IsNullOrWhiteSpace(tracker.FailureMessage) ? null : tracker.FailureMessage,
                        WarningMessage = string.IsNullOrWhiteSpace(tracker.WarningMessage) ? null : tracker.WarningMessage,
                    }
                );
            }
        }

        return trackers
              .OrderBy(row => row.TierNumber)
              .ThenBy(row => row.TrackerNumber)
              .ToArray();
    }

    public async Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request,
        ResolvedTorrentCategorySelection categorySelection, CancellationToken cancellationToken)
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
                "invalid_magnet", "MagnetUri must be a valid magnet URI.", StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri)
            );
        }

        var infoHash = magnet.InfoHashes.V1OrV2.ToHex().ToUpperInvariant();
        if (await torrentStateStore.ExistsByInfoHashAsync(infoHash, cancellationToken))
        {
            throw new ServiceOperationException(
                "duplicate_magnet", "A torrent with the same info hash already exists on this host.",
                StatusCodes.Status409Conflict, nameof(request.MagnetUri)
            );
        }

        var now       = DateTimeOffset.UtcNow;
        var manager = await MeasureMonoTorrentOperationAsync(
            "engine_add_magnet",
            null,
            string.IsNullOrWhiteSpace(magnet.Name) ? null : magnet.Name,
            async () => await _engine!.AddAsync(magnet, categorySelection.DownloadRootPath)
        );
        var torrentId = Guid.NewGuid();
        RegisterManager(torrentId, manager);
        var persistedSavePath = MonoTorrentSavePathNormalizer.Normalize(
            manager.SavePath, string.IsNullOrWhiteSpace(magnet.Name) ? null : magnet.Name
        );

        var snapshot = new TorrentSnapshot
        {
            TorrentId = torrentId,
            Name = string.IsNullOrWhiteSpace(magnet.Name) ? $"Magnet {infoHash[..8]}" : magnet.Name,
            CategoryKey = categorySelection.CategoryKey,
            CompletionCallbackLabel = categorySelection.CompletionCallbackLabel,
            InvokeCompletionCallback = categorySelection.InvokeCompletionCallback,
            CompletionCallbackState = null,
            CompletionCallbackPendingSinceUtc = null,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            CompletionCallbackFeedbackReceivedAtUtc = null,
            CompletionCallbackFeedbackJson = null,
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

        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
            var snapshots       = await torrentStateStore.ListAsync(cancellationToken);
            var activeMetadataResolutions = snapshots.Count(existing =>
                existing.TorrentId != snapshot.TorrentId &&
                existing.DesiredState == TorrentDesiredState.Runnable &&
                existing.State == ContractTorrentState.ResolvingMetadata
            );

            if (!manager.HasMetadata && !manager.Complete &&
                activeMetadataResolutions < runtimeSettings.MaxActiveMetadataResolutions)
            {
                await EnsureManagerStartedAsync(manager, cancellationToken);

                var dispatchedSnapshot = CreateUpdatedSnapshot(snapshot, manager, DateTimeOffset.UtcNow);
                dispatchedSnapshot.State             = ContractTorrentState.ResolvingMetadata;
                dispatchedSnapshot.ErrorMessage      = null;
                dispatchedSnapshot.LastActivityAtUtc ??= DateTimeOffset.UtcNow;
                await torrentStateStore.UpdateAsync(dispatchedSnapshot, cancellationToken);
            }
        }
        finally
        {
            _synchronizationGate.Release();
        }

        var persistedSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
        return MapDetail(persistedSnapshot, new TorrentQueueDiagnostic(null, null), null);
    }

    public async Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var (snapshot, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);

        if (!CanPause(snapshot.State))
        {
            throw new ServiceOperationException(
                "invalid_state", $"Torrent '{snapshot.Name}' cannot be paused while in state '{snapshot.State}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            finalizationProbeCoordinator.Remove(torrentId);
            var currentSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
            var now             = DateTimeOffset.UtcNow;
            var updatedSnapshot = CreatePausedSnapshot(CreateUpdatedSnapshot(currentSnapshot, manager, now), now);
            await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(updatedSnapshot, cancellationToken);

            await SynchronizeCoreAsync(cancellationToken, includeAutomaticRecovery: false);

            return new TorrentActionResultDto
            {
                TorrentId      = torrentId,
                Action         = "pause",
                State          = updatedSnapshot.State,
                ProcessedAtUtc = now,
                DataDeleted    = false,
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
                "invalid_state", $"Torrent '{snapshot.Name}' cannot be resumed while in state '{snapshot.State}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            var currentSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
            var now             = DateTimeOffset.UtcNow;
            var queuedSnapshot  = CreateQueuedSnapshot(CreateUpdatedSnapshot(currentSnapshot, manager, now), now);
            queuedSnapshot.ErrorMessage = null;
            await torrentStateStore.UpdateAsync(queuedSnapshot, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(queuedSnapshot, cancellationToken);

            await SynchronizeCoreAsync(cancellationToken, includeAutomaticRecovery: false);

            var updatedSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? queuedSnapshot;

            return new TorrentActionResultDto
            {
                TorrentId      = torrentId,
                Action         = "resume",
                State          = updatedSnapshot.State,
                ProcessedAtUtc = now,
                DataDeleted    = false,
            };
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }

    public async Task<TorrentActionResultDto> RefreshMetadataAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        var (snapshot, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);

        if (!CanRefreshMetadata(snapshot.State))
        {
            throw new ServiceOperationException(
                "invalid_state",
                $"Torrent '{snapshot.Name}' cannot refresh metadata while in state '{snapshot.State}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            var currentSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
            var now             = DateTimeOffset.UtcNow;
            await RequestMetadataDiscoveryRefreshAsync(currentSnapshot, manager, now, "manual", cancellationToken);
            var persistedSnapshot = CreateUpdatedSnapshot(currentSnapshot, manager, now);
            await torrentStateStore.UpdateAsync(persistedSnapshot, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(persistedSnapshot, cancellationToken);

            return new TorrentActionResultDto
            {
                TorrentId      = torrentId,
                Action         = "refresh_metadata",
                State          = persistedSnapshot.State,
                ProcessedAtUtc = now,
                DataDeleted    = false,
            };
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }

    public async Task<TorrentActionResultDto> ResetMetadataSessionAsync(Guid torrentId,
        CancellationToken                                                    cancellationToken)
    {
        var (snapshot, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);

        if (!CanRefreshMetadata(snapshot.State))
        {
            throw new ServiceOperationException(
                "invalid_state", $"Torrent '{snapshot.Name}' cannot reset metadata while in state '{snapshot.State}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            var currentSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
            var now             = DateTimeOffset.UtcNow;
            var recreatedManager = await ResetMetadataSessionCoreAsync(
                currentSnapshot, manager, now, "manual", cancellationToken
            );
            await RequestMetadataDiscoveryRefreshAsync(
                currentSnapshot, recreatedManager, now, "manual_reset", cancellationToken
            );
            var persistedSnapshot = CreateUpdatedSnapshot(currentSnapshot, recreatedManager, now);
            await torrentStateStore.UpdateAsync(persistedSnapshot, cancellationToken);

            return new TorrentActionResultDto
            {
                TorrentId      = torrentId,
                Action         = "reset_metadata_session",
                State          = persistedSnapshot.State,
                ProcessedAtUtc = now,
                DataDeleted    = false,
            };
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }

    public async Task<TorrentActionResultDto> RetryCompletionCallbackAsync(Guid torrentId,
        CancellationToken                                                       cancellationToken)
    {
        var (snapshot, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);

        if (!CanRetryCompletionCallback(snapshot.CompletionCallbackState))
        {
            throw new ServiceOperationException(
                "invalid_callback_state",
                $"Completion callback for torrent '{snapshot.Name}' cannot be retried while in state '{snapshot.CompletionCallbackState?.ToString() ?? "None"}'.",
                StatusCodes.Status409Conflict, nameof(torrentId)
            );
        }

        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            finalizationProbeCoordinator.Remove(torrentId);
            var currentSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? snapshot;
            var now             = DateTimeOffset.UtcNow;
            var updatedSnapshot = CreateUpdatedSnapshot(currentSnapshot, manager, now);
            updatedSnapshot.CompletionCallbackState           = TorrentCompletionCallbackState.PendingFinalization;
            updatedSnapshot.CompletionCallbackPendingSinceUtc = now;
            updatedSnapshot.CompletionCallbackInvokedAtUtc    = null;
            updatedSnapshot.CompletionCallbackLastError       = null;
            await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(updatedSnapshot, cancellationToken);

            await SynchronizeCoreAsync(cancellationToken, includeAutomaticRecovery: false);

            var persistedSnapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken) ?? updatedSnapshot;

            return new TorrentActionResultDto
            {
                TorrentId      = torrentId,
                Action         = "retry_completion_callback",
                State          = persistedSnapshot.State,
                ProcessedAtUtc = now,
                DataDeleted    = false,
            };
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }

    public async Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request,
        CancellationToken                                      cancellationToken)
    {
        var (snapshot, manager) = await GetRequiredManagedTorrentAsync(torrentId, cancellationToken);
        var removedAtUtc = DateTimeOffset.UtcNow;
        var cleanupCandidatePaths = request.DeleteData ?
                GetCleanupCandidatePaths(manager)
                    .Concat(string.IsNullOrWhiteSpace(snapshot.SavePath) ? [] : [Path.GetFullPath(snapshot.SavePath)])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() :
                Array.Empty<string>();
        var downloadRootPath = snapshot.DownloadRootPath ?? servicePaths.DownloadRootPath;

        await RemoveManagedTorrentAsync(torrentId, manager, request.DeleteData, cancellationToken);
        await torrentStateStore.DeleteAsync(torrentId, cancellationToken);
        await torrentHistoryService.MarkRemovedAsync(
            snapshot,
            dataDeleted: request.DeleteData,
            removalReason: request.DeleteData ? "manual_remove_delete_data" : "manual_remove",
            removedByCleanupPolicy: false,
            removedAtUtc,
            cancellationToken);

        if (request.DeleteData)
        {
            torrentRemovalCleanupScheduler.ScheduleDeleteDataCleanup(
                torrentId,
                downloadRootPath,
                cleanupCandidatePaths
            );
        }

        return new TorrentActionResultDto
        {
            TorrentId      = torrentId,
            Action         = "remove",
            State          = ContractTorrentState.Removed,
            ProcessedAtUtc = removedAtUtc,
            DataDeleted    = request.DeleteData,
        };
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

        return persistedSnapshots.Select(snapshot
                                          => managers.TryGetValue(snapshot.TorrentId, out var manager) ?
                                                  CreateReadProjectedSnapshot(snapshot, manager) : snapshot
                                  )
                                 .ToArray();
    }

    private static IReadOnlyList<string> GetCleanupCandidatePaths(TorrentManager manager)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in manager.Files)
        {
            AddPath(paths, file.DownloadCompleteFullPath);
            AddPath(paths, file.DownloadIncompleteFullPath);
            AddPath(paths, file.FullPath);
        }

        return paths.ToArray();
    }

    private static void AddPath(ISet<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        paths.Add(Path.GetFullPath(path));
    }

    private async Task RemoveManagedTorrentAsync(Guid torrentId, TorrentManager manager, bool deleteData,
        CancellationToken                              cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await managerStopCoordinator.WaitForPendingAsync(torrentId, cancellationToken);
            await EnsureManagerStoppedAsync(manager, cancellationToken);

            await MeasureMonoTorrentOperationAsync(
                "engine_remove_manager",
                torrentId,
                manager.Name,
                async () => await _engine!.RemoveAsync(
                    manager, deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly
                )
            );

            _managers.Remove(torrentId);
            _torrentIdsByManager.TryRemove(manager, out _);
            _observedTorrentIds.Remove(torrentId);
            _observedUploadedSessionBytes.TryRemove(torrentId, out _);
            finalizationProbeCoordinator.Remove(torrentId);
            managerStopCoordinator.Remove(torrentId);
            _downloadRecoveryStates.TryRemove(torrentId, out _);
            _downloadRecoveryAttemptCounts.TryRemove(torrentId, out _);
            _metadataRecoveryStates.TryRemove(torrentId, out _);
            _metadataRecoveryAttemptCounts.TryRemove(torrentId, out _);
            _connectionActivitySummaries.Remove(torrentId);
            finalizationProbeCoordinator.Remove(torrentId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_serviceOptions.EngineMode != TorrentEngineMode.MonoTorrent)
        {
            throw new InvalidOperationException(
                "MonoTorrent engine adapter cannot initialize when EngineMode is not MonoTorrent."
            );
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
            var cacheDirectory  = Path.Combine(servicePaths.StorageRootPath, "monotorrent-cache");
            Directory.CreateDirectory(cacheDirectory);

            var engineSettingsBuilder = new EngineSettingsBuilder
            {
                AllowedEncryption            = MonoTorrentConnectionPolicy.CreateAllowedEncryption(runtimeSettings.EngineEncryptionMode),
                AllowLocalPeerDiscovery        = _serviceOptions.EngineAllowLocalPeerDiscovery,
                AllowPortForwarding            = _serviceOptions.EngineAllowPortForwarding,
                CacheDirectory                 = cacheDirectory,
                AutoSaveLoadFastResume         = true,
                AutoSaveLoadMagnetLinkMetadata = true,
                UsePartialFiles                = false,
                MaximumConnections             = runtimeSettings.EngineMaximumConnections,
                MaximumHalfOpenConnections     = runtimeSettings.EngineMaximumHalfOpenConnections,
                MaximumDownloadRate            = runtimeSettings.EngineMaximumDownloadRateBytesPerSecond,
                MaximumUploadRate              = runtimeSettings.EngineMaximumUploadRateBytesPerSecond,
                DhtEndPoint                    = new IPEndPoint(IPAddress.Any, _serviceOptions.EngineDhtPort),
                ListenEndPoints                = MonoTorrentConnectionPolicy.CreateListenEndPoints(_serviceOptions.EngineListenPort),
            };

            _engine = new ClientEngine(engineSettingsBuilder.ToSettings());
            appliedEngineSettingsState.Set(
                runtimeSettings.EngineEncryptionMode, runtimeSettings.EngineMaximumConnections,
                runtimeSettings.EngineMaximumHalfOpenConnections,
                runtimeSettings.EngineMaximumDownloadRateBytesPerSecond,
                runtimeSettings.EngineMaximumUploadRateBytesPerSecond
            );
            _initialized = true;

            logger.LogInformation("MonoTorrent engine initialized. CacheDirectory={CacheDirectory}", cacheDirectory);

            await WriteCacheAuditAsync(cacheDirectory, cancellationToken);

            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level             = ActivityLogLevel.Information,
                    Category          = "engine",
                    EventType         = "engine.monotorrent.ready",
                    Message           = "MonoTorrent engine is initialized and ready.",
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            cacheDirectory,
                            _serviceOptions.EngineListenPort,
                            _serviceOptions.EngineDhtPort,
                            _serviceOptions.EngineAllowPortForwarding,
                            _serviceOptions.EngineAllowLocalPeerDiscovery,
                            runtimeSettings.EngineEncryptionMode,
                            AllowedEncryption = engineSettingsBuilder.AllowedEncryption.Select(item => item.ToString()).ToArray(),
                            ListenEndPoints = engineSettingsBuilder.ListenEndPoints.ToDictionary(
                                item => item.Key, item => item.Value.ToString()
                            ),
                            runtimeSettings.EngineMaximumConnections,
                            runtimeSettings.EngineMaximumHalfOpenConnections,
                            runtimeSettings.EngineMaximumDownloadRateBytesPerSecond,
                            runtimeSettings.EngineMaximumUploadRateBytesPerSecond,
                            runtimeSettings.EngineConnectionFailureLogBurstLimit,
                            runtimeSettings.EngineConnectionFailureLogWindowSeconds,
                            UsePartialFiles = false,
                            PartialFileSuffix = string.Empty,
                            runtimeSettings.SeedingStopMode, runtimeSettings.SeedingStopRatio,
                            runtimeSettings.SeedingStopMinutes,
                        }
                    ),
                }, cancellationToken
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TorrentManager> AddOrGetManagerAsync(TorrentSnapshot snapshot,
        CancellationToken                                                   cancellationToken)
    {
        if (_managers.TryGetValue(snapshot.TorrentId, out var existingManager))
        {
            return existingManager;
        }

        var magnet = MagnetLink.Parse(snapshot.MagnetUri);
        var recoveryDownloadRootPath =
                MonoTorrentRecoveryPathResolver.ResolveDownloadRootPath(snapshot, servicePaths.DownloadRootPath);
        var manager = await MeasureMonoTorrentOperationAsync(
            "engine_add_recovered_magnet",
            snapshot.TorrentId,
            snapshot.Name,
            async () => await _engine!.AddAsync(magnet, recoveryDownloadRootPath)
        );
        RegisterManager(snapshot.TorrentId, manager);
        _managers[snapshot.TorrentId] = manager;
        return manager;
    }

    private async Task<(TorrentSnapshot Snapshot, TorrentManager Manager)> GetRequiredManagedTorrentAsync(
        Guid torrentId, CancellationToken cancellationToken)
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
                "torrent_not_found", $"Torrent '{torrentId}' was not found.", StatusCodes.Status404NotFound,
                nameof(torrentId)
            );
        }

        return (snapshot, manager);
    }

    private async Task ReconcileRuntimeQueueAsync(CancellationToken cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var now             = DateTimeOffset.UtcNow;

        var managedTorrents = await GetManagedTorrentsAsync(cancellationToken);
        await ReconcileMetadataResolutionQueueAsync(
            managedTorrents, runtimeSettings.MaxActiveMetadataResolutions, now, cancellationToken
        );

        managedTorrents = await GetManagedTorrentsAsync(cancellationToken);
        await ReconcileSeedingQueueAsync(managedTorrents, runtimeSettings, now, cancellationToken);

        managedTorrents = await GetManagedTorrentsAsync(cancellationToken);
        await ReconcileDownloadQueueAsync(managedTorrents, runtimeSettings.MaxActiveDownloads, now, cancellationToken);
    }

    private async Task FlushManagedSnapshotsForShutdownAsync(CancellationToken cancellationToken)
    {
        var now             = DateTimeOffset.UtcNow;
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);

        foreach (var (torrentId, manager) in _managers)
        {
            try
            {
                var snapshot = await torrentStateStore.GetAsync(torrentId, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                var updatedSnapshot = CreateUpdatedSnapshot(snapshot, manager, now);
                if (ShouldPreservePersistedCompletion(snapshot, manager))
                {
                    updatedSnapshot = CreateRecoveredCompletedSnapshot(snapshot, updatedSnapshot, now);
                } else
                {
                    updatedSnapshot = NormalizeCompletedErrorIfPayloadVisible(
                        updatedSnapshot,
                        finalizationResult: null,
                        now
                    );
                }

                if (manager.Complete && updatedSnapshot.ProgressPercent >= 100d &&
                    updatedSnapshot.State == ContractTorrentState.Queued)
                {
                    updatedSnapshot = CreateRecoveredCompletedSnapshot(updatedSnapshot, updatedSnapshot, now);
                }

                await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed flushing MonoTorrent manager {TorrentId} before shutdown", torrentId);
            }
        }
    }

    private async Task<List<(TorrentSnapshot Snapshot, TorrentManager Manager)>> GetManagedTorrentsAsync(
        CancellationToken cancellationToken)
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

    private async Task<List<PendingCallbackWork>>
            SynchronizeCoreAsync(CancellationToken cancellationToken, bool includeAutomaticRecovery = true)
    {
        await EnsureInitializedAsync(cancellationToken);

        List<KeyValuePair<Guid, TorrentManager>> managers;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recovered)
            {
                return [];
            }

            managers = _managers.ToList();
        }
        finally
        {
            _gate.Release();
        }

        var now                      = DateTimeOffset.UtcNow;
        var runtimeSettings          = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var pendingCallbackWork = new List<PendingCallbackWork>();
        var snapshotPersistenceStopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var entry in managers)
        {
            var snapshot = await MeasureStorageOperationAsync(
                "torrent_snapshot_read",
                entry.Key,
                async () => await torrentStateStore.GetAsync(entry.Key, cancellationToken)
            );
            if (snapshot is null)
            {
                continue;
            }

            var observedFiles = GetObservedFilePaths(entry.Value);
            if (snapshot.State == ContractTorrentState.Completed && IsManagerStoppedForCompletion(entry.Value) &&
                snapshot.CompletionCallbackState == TorrentCompletionCallbackState.PendingFinalization)
            {
                managerStopCoordinator.Remove(snapshot.TorrentId);
                pendingCallbackWork.Add(
                    new PendingCallbackWork(snapshot, observedFiles, FinalizationResult: null, EngineStopReady: true)
                );
                continue;
            }

            if (CanSkipCompletedSynchronization(snapshot, entry.Value))
            {
                finalizationProbeCoordinator.Remove(snapshot.TorrentId);
                managerStopCoordinator.Remove(snapshot.TorrentId);
                continue;
            }

            TorrentSnapshot updatedSnapshot;
            TorrentCompletionFinalizationCheckResult? finalizationResult = null;
            var engineStopReady = true;
            var projectionStopwatch = System.Diagnostics.Stopwatch.StartNew();

            if (snapshot.DesiredState == TorrentDesiredState.Paused)
            {
                if (IsManagerRunning(entry.Value))
                {
                    await EnsureManagerPausedAsync(entry.Value, cancellationToken);
                }

                updatedSnapshot = CreatePausedSnapshot(CreateUpdatedSnapshot(snapshot, entry.Value, now), now);
            } else
            {
                updatedSnapshot = CreateUpdatedSnapshot(snapshot, entry.Value, now);
                if (entry.Value.HasMetadata && LooksTransferComplete(updatedSnapshot))
                {
                    finalizationProbeCoordinator.TryTakeCompletedOrSchedule(
                        updatedSnapshot,
                        runtimeSettings,
                        observedFiles,
                        out finalizationResult
                    );
                }

                updatedSnapshot = ApplyFileCompletionVisibilityIfNeeded(
                    updatedSnapshot,
                    entry.Value,
                    finalizationResult
                );
                updatedSnapshot = NormalizeCompletedErrorIfPayloadVisible(
                    updatedSnapshot,
                    finalizationResult,
                    now
                );
                if (updatedSnapshot.State is ContractTorrentState.Seeding or ContractTorrentState.Completed)
                {
                    var seedingPolicyResult = await ApplySeedingPolicyIfNeededAsync(
                        updatedSnapshot,
                        entry.Value,
                        now,
                        cancellationToken,
                        finalizationResult
                    );
                    updatedSnapshot = seedingPolicyResult.Snapshot;
                    engineStopReady = seedingPolicyResult.EngineStopReady;
                }
            }
            projectionStopwatch.Stop();
            await durationDiagnostics.RecordIfSlowAsync(
                "engine",
                "torrent_snapshot_projection",
                projectionStopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.StorageSlowThreshold,
                "succeeded",
                entry.Key,
                new { TorrentName = snapshot.Name, snapshot.State }
            );

            var previousCompletedAtUtc = snapshot.CompletedAtUtc;
            if (ShouldEvaluateCompletionFinalization(previousCompletedAtUtc, updatedSnapshot) ||
                ShouldEvaluateTimedOutFinalization(updatedSnapshot))
            {
                finalizationResult ??= TorrentCompletionFinalizationProbeCoordinator.CreateDeferredResult(
                    updatedSnapshot,
                    observedFiles,
                    defaultDownloadRootPath: servicePaths.DownloadRootPath
                );
            }

            await completionCallbackProcessor.MarkPendingIfTriggeredAsync(
                previousCompletedAtUtc,
                updatedSnapshot,
                runtimeSettings,
                now,
                cancellationToken,
                finalizationResult
            );

            if (ShouldAutoRequeueFinalizationTimeout(updatedSnapshot, finalizationResult))
            {
                updatedSnapshot.CompletionCallbackState           = TorrentCompletionCallbackState.PendingFinalization;
                updatedSnapshot.CompletionCallbackPendingSinceUtc = now;
                updatedSnapshot.CompletionCallbackInvokedAtUtc    = null;
                updatedSnapshot.CompletionCallbackLastError       = null;
            }

            updatedSnapshot = await MeasureStorageOperationAsync(
                "torrent_callback_progress_read",
                entry.Key,
                async () => await PreserveLatestPersistedCallbackProgressAsync(updatedSnapshot, cancellationToken)
            );
            await MeasureStorageOperationAsync(
                "torrent_snapshot_write",
                entry.Key,
                async () => await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken)
            );
            await MeasureStorageOperationAsync(
                "torrent_history_write",
                entry.Key,
                async () => await torrentHistoryService.ObserveSnapshotAsync(updatedSnapshot, cancellationToken)
            );

            if (updatedSnapshot.CompletionCallbackState is TorrentCompletionCallbackState.PendingFinalization or
                TorrentCompletionCallbackState.WaitingForFeedback)
            {
                pendingCallbackWork.Add(
                    new PendingCallbackWork(updatedSnapshot, observedFiles, finalizationResult, engineStopReady)
                );
            }
        }

        snapshotPersistenceStopwatch.Stop();
        await durationDiagnostics.RecordIfSlowAsync(
            "storage",
            "torrent_snapshot_persistence_phase",
            snapshotPersistenceStopwatch.Elapsed,
            RuntimeOperationDurationDiagnostics.StorageSlowThreshold,
            "succeeded",
            details: new { ManagerCount = managers.Count }
        );

        var queueReconciliationStopwatch = System.Diagnostics.Stopwatch.StartNew();
        await ReconcileRuntimeQueueAsync(cancellationToken);
        queueReconciliationStopwatch.Stop();
        await durationDiagnostics.RecordIfSlowAsync(
            "engine",
            "queue_reconciliation_phase",
            queueReconciliationStopwatch.Elapsed,
            RuntimeOperationDurationDiagnostics.SynchronizationSlowThreshold,
            "succeeded",
            details: new { ManagerCount = managers.Count }
        );
        if (includeAutomaticRecovery)
        {
            var recoveryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await ProcessMetadataRecoveryAsync(runtimeSettings, now, cancellationToken);
            await ProcessDownloadRecoveryAsync(runtimeSettings, now, cancellationToken);
            recoveryStopwatch.Stop();
            await durationDiagnostics.RecordIfSlowAsync(
                "monotorrent",
                "automatic_recovery_phase",
                recoveryStopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.MonoTorrentSlowThreshold,
                "succeeded",
                details: new { ManagerCount = managers.Count }
            );
        }

        await WriteConnectionActivitySummariesAsync(now, managers, cancellationToken);

        return pendingCallbackWork;
    }

    private async Task ProcessPendingCallbacksAsync(
        IReadOnlyList<PendingCallbackWork> pendingCallbackWork,
        CancellationToken cancellationToken)
    {
        if (pendingCallbackWork.Count == 0)
        {
            return;
        }

        var now             = DateTimeOffset.UtcNow;
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);

        foreach (var work in pendingCallbackWork)
        {
            if (!work.EngineStopReady)
            {
                continue;
            }

            var currentSnapshot =
                    await PreserveLatestPersistedCallbackProgressAsync(work.Snapshot, cancellationToken);
            TorrentCompletionFinalizationCheckResult? finalizationResult = null;
            if (currentSnapshot.CompletionCallbackState == TorrentCompletionCallbackState.PendingFinalization)
            {
                finalizationResult = work.FinalizationResult;
                var probeCompleted = finalizationResult is not null ||
                                     finalizationProbeCoordinator.TryTakeCompletedOrSchedule(
                                         currentSnapshot,
                                         runtimeSettings,
                                         work.ObservedFiles,
                                         out finalizationResult
                                     );
                if (!probeCompleted)
                {
                    continue;
                }

                finalizationResult ??= TorrentCompletionFinalizationProbeCoordinator.CreateDeferredResult(
                    currentSnapshot,
                    work.ObservedFiles,
                    defaultDownloadRootPath: servicePaths.DownloadRootPath
                );
            }
            if (!await completionCallbackProcessor.ProcessPendingAsync(
                        currentSnapshot, runtimeSettings, now, cancellationToken, finalizationResult
                    ))
            {
                continue;
            }

            currentSnapshot = await PreserveLatestPersistedCallbackProgressAsync(currentSnapshot, cancellationToken);
            await torrentStateStore.UpdateAsync(currentSnapshot, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(currentSnapshot, cancellationToken);
            if (currentSnapshot.CompletionCallbackState != TorrentCompletionCallbackState.PendingFinalization)
            {
                finalizationProbeCoordinator.Remove(currentSnapshot.TorrentId);
            }
        }
    }

    private async Task ReconcileMetadataResolutionQueueAsync(
        IReadOnlyList<(TorrentSnapshot Snapshot, TorrentManager Manager)> managedTorrents,
        int maxActiveMetadataResolutions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var candidates = managedTorrents.Where(entry => entry.Snapshot.DesiredState == TorrentDesiredState.Runnable)
                                        .Where(entry => entry.Snapshot.State is not ContractTorrentState.Completed and
                                                 not ContractTorrentState.Error and not ContractTorrentState.Removed
                                         )
                                        .Where(entry => !entry.Manager.HasMetadata && !entry.Manager.Complete)
                                        .OrderBy(entry => entry.Snapshot.AddedAtUtc)
                                        .ThenBy(entry => entry.Snapshot.TorrentId)
                                        .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var (snapshot, manager) = candidates[index];
            var currentSnapshot = await torrentStateStore.GetAsync(snapshot.TorrentId, cancellationToken);
            if (currentSnapshot is null || currentSnapshot.DesiredState == TorrentDesiredState.Paused ||
                currentSnapshot.State is ContractTorrentState.Completed or ContractTorrentState.Error or
                        ContractTorrentState.Removed)
            {
                continue;
            }

            if (index < maxActiveMetadataResolutions)
            {
                await EnsureManagerStartedAsync(manager, cancellationToken);

                var updatedSnapshot = CreateUpdatedSnapshot(currentSnapshot, manager, now);
                updatedSnapshot.State             =   ContractTorrentState.ResolvingMetadata;
                updatedSnapshot.ErrorMessage      =   null;
                updatedSnapshot.LastActivityAtUtc ??= now;
                await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
                await torrentHistoryService.ObserveSnapshotAsync(updatedSnapshot, cancellationToken);
                continue;
            }

            if (IsManagerRunning(manager))
            {
                await EnsureManagerStoppedAsync(manager, cancellationToken);
            }

            var queuedSnapshot = CreateQueuedSnapshot(CreateUpdatedSnapshot(currentSnapshot, manager, now), now);
            await torrentStateStore.UpdateAsync(queuedSnapshot, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(queuedSnapshot, cancellationToken);
        }
    }

    private async Task ReconcileSeedingQueueAsync(
        IReadOnlyList<(TorrentSnapshot Snapshot, TorrentManager Manager)> managedTorrents,
        RuntimeSettingsSnapshot                                            runtimeSettings,
        DateTimeOffset                                                     now,
        CancellationToken                                                  cancellationToken)
    {
        var candidates = managedTorrents.Where(entry => entry.Snapshot.DesiredState == TorrentDesiredState.Runnable)
                                        .Where(entry => entry.Snapshot.State is not ContractTorrentState.Completed and
                                                 not ContractTorrentState.Error and not ContractTorrentState.Removed
                                         )
                                        .Where(entry => entry.Snapshot.CompletionCallbackState is not
                                             TorrentCompletionCallbackState.PendingFinalization and not
                                             TorrentCompletionCallbackState.WaitingForFeedback)
                                        .Where(entry => entry.Manager.HasMetadata && entry.Manager.Complete)
                                        .OrderBy(entry => entry.Snapshot.AddedAtUtc)
                                        .ThenBy(entry => entry.Snapshot.TorrentId)
                                        .ToList();

        foreach (var (snapshot, manager) in candidates)
        {
            var currentSnapshot = await torrentStateStore.GetAsync(snapshot.TorrentId, cancellationToken);
            if (currentSnapshot is null || currentSnapshot.DesiredState == TorrentDesiredState.Paused ||
                currentSnapshot.State is ContractTorrentState.Completed or ContractTorrentState.Error or
                        ContractTorrentState.Removed)
            {
                continue;
            }

            await EnsureManagerStartedAsync(manager, cancellationToken);

            var previousCompletedAtUtc = currentSnapshot.CompletedAtUtc;
            var updatedSnapshot = CreateUpdatedSnapshot(currentSnapshot, manager, now);
            updatedSnapshot.State = ContractTorrentState.Seeding;
            var observedFiles = GetObservedFilePaths(manager);
            finalizationProbeCoordinator.TryTakeCompletedOrSchedule(
                updatedSnapshot,
                runtimeSettings,
                observedFiles,
                out var finalizationResult
            );
            updatedSnapshot = ApplyFileCompletionVisibilityIfNeeded(updatedSnapshot, manager, finalizationResult);
            if (updatedSnapshot.State == ContractTorrentState.Seeding)
            {
                var seedingPolicyResult = await ApplySeedingPolicyIfNeededAsync(
                    updatedSnapshot,
                    manager,
                    now,
                    cancellationToken,
                    finalizationResult
                );
                updatedSnapshot = seedingPolicyResult.Snapshot;
            }

            var callbackFinalizationResult = ShouldEvaluateCompletionFinalization(
                previousCompletedAtUtc,
                updatedSnapshot
            )
                ? finalizationResult ?? TorrentCompletionFinalizationProbeCoordinator.CreateDeferredResult(
                    updatedSnapshot,
                    observedFiles,
                    defaultDownloadRootPath: servicePaths.DownloadRootPath
                )
                : null;
            await completionCallbackProcessor.MarkPendingIfTriggeredAsync(
                previousCompletedAtUtc,
                updatedSnapshot,
                runtimeSettings,
                now,
                cancellationToken,
                callbackFinalizationResult
            );

            updatedSnapshot = await PreserveLatestPersistedCallbackProgressAsync(updatedSnapshot, cancellationToken);
            await torrentStateStore.UpdateAsync(updatedSnapshot, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(updatedSnapshot, cancellationToken);
        }
    }

    private async Task ReconcileDownloadQueueAsync(
        IReadOnlyList<(TorrentSnapshot Snapshot, TorrentManager Manager)> managedTorrents, int maxActiveDownloads,
        DateTimeOffset                                                    now, CancellationToken cancellationToken)
    {
        var candidates = managedTorrents.Where(entry => entry.Snapshot.DesiredState == TorrentDesiredState.Runnable)
                                        .Where(entry => entry.Snapshot.State is not ContractTorrentState.Completed and
                                                 not ContractTorrentState.Error and not ContractTorrentState.Removed
                                         )
                                        .Where(entry => entry.Manager.HasMetadata && !entry.Manager.Complete)
                                        .OrderBy(entry => entry.Snapshot.AddedAtUtc)
                                        .ThenBy(entry => entry.Snapshot.TorrentId)
                                        .ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var (snapshot, manager) = candidates[index];
            var currentSnapshot = await torrentStateStore.GetAsync(snapshot.TorrentId, cancellationToken);
            if (currentSnapshot is null || currentSnapshot.DesiredState == TorrentDesiredState.Paused ||
                currentSnapshot.State is ContractTorrentState.Completed or ContractTorrentState.Error or
                        ContractTorrentState.Removed)
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
                await torrentHistoryService.ObserveSnapshotAsync(updatedSnapshot, cancellationToken);
                continue;
            }

            if (IsManagerRunning(manager))
            {
                await EnsureManagerStoppedAsync(manager, cancellationToken);
            }

            var queuedSnapshot = CreateQueuedSnapshot(CreateUpdatedSnapshot(currentSnapshot, manager, now), now);
            await torrentStateStore.UpdateAsync(queuedSnapshot, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(queuedSnapshot, cancellationToken);
        }
    }

    private TorrentSnapshot CreateUpdatedSnapshot(TorrentSnapshot existing, TorrentManager manager, DateTimeOffset now)
    {
        var state = MapState(manager, existing.State, existing.DesiredState);
        var totalBytes = manager.HasMetadata ? manager.Torrent?.Size ?? existing.TotalBytes :
                existing.TotalBytes                                  ?? manager.MagnetLink?.Size;
        var savePath        = MonoTorrentSavePathNormalizer.Normalize(manager.SavePath, existing.Name);
        var downloadedBytes = CalculateDownloadedBytes(totalBytes, manager.Progress, existing.DownloadedBytes);
        var uploadedBytes = CalculateUploadedBytes(
            existing.TorrentId, existing.UploadedBytes, manager.Monitor.DataBytesSent
        );

        if (manager.HasMetadata && state == ContractTorrentState.ResolvingMetadata)
        {
            state = manager.Complete ? ContractTorrentState.Seeding : ContractTorrentState.Downloading;
        }

        return new TorrentSnapshot
        {
            TorrentId                         = existing.TorrentId,
            Name                              = string.IsNullOrWhiteSpace(manager.Name) ? existing.Name : manager.Name,
            CategoryKey                       = existing.CategoryKey,
            CompletionCallbackLabel           = existing.CompletionCallbackLabel,
            InvokeCompletionCallback          = existing.InvokeCompletionCallback,
            CompletionCallbackState           = existing.CompletionCallbackState,
            CompletionCallbackPendingSinceUtc = existing.CompletionCallbackPendingSinceUtc,
            CompletionCallbackInvokedAtUtc    = existing.CompletionCallbackInvokedAtUtc,
            CompletionCallbackLastError       = existing.CompletionCallbackLastError,
            CompletionCallbackFeedbackReceivedAtUtc = existing.CompletionCallbackFeedbackReceivedAtUtc,
            CompletionCallbackFeedbackJson = existing.CompletionCallbackFeedbackJson,
            State                             = state,
            DesiredState                      = existing.DesiredState,
            MagnetUri                         = existing.MagnetUri,
            InfoHash                          = manager.InfoHashes.V1OrV2.ToHex().ToUpperInvariant(),
            DownloadRootPath                  = existing.DownloadRootPath,
            SavePath                          = savePath,
            ProgressPercent                   = manager.Progress,
            DownloadedBytes                   = downloadedBytes,
            UploadedBytes                     = uploadedBytes,
            TotalBytes                        = totalBytes,
            DownloadRateBytesPerSecond        = manager.Monitor.DownloadRate,
            UploadRateBytesPerSecond          = manager.Monitor.UploadRate,
            TrackerCount                      = CountTrackers(manager),
            ConnectedPeerCount                = manager.OpenConnections,
            AddedAtUtc                        = existing.AddedAtUtc,
            CompletedAtUtc = ResolveCompletedAtUtc(
                existing.CompletedAtUtc, state, now
            ),
            SeedingStartedAtUtc = ResolveSeedingStartedAtUtc(existing.SeedingStartedAtUtc, state, now),
            DownloadColdSinceUtc = existing.DownloadColdSinceUtc,
            LastActivityAtUtc   = now,
            ErrorMessage        = manager.Error?.Reason.ToString() ?? existing.ErrorMessage,
        };
    }

    private static TorrentSnapshot CreateRecoveredCompletedSnapshot(TorrentSnapshot existing,
        TorrentSnapshot updated, DateTimeOffset now)
    {
        updated.State                             = ContractTorrentState.Completed;
        updated.ProgressPercent                   = Math.Max(100d, existing.ProgressPercent);
        updated.DownloadedBytes                   = CalculateRecoveredCompletedDownloadedBytes(existing);
        updated.TotalBytes                        = existing.TotalBytes ?? updated.TotalBytes;
        updated.ConnectedPeerCount                = 0;
        updated.DownloadRateBytesPerSecond        = 0;
        updated.UploadRateBytesPerSecond          = 0;
        updated.CompletedAtUtc                    = existing.CompletedAtUtc ?? existing.SeedingStartedAtUtc ?? now;
        updated.SeedingStartedAtUtc               = existing.SeedingStartedAtUtc;
        updated.LastActivityAtUtc                 = now;
        updated.ErrorMessage                      = null;
        return updated;
    }

    private static TorrentSnapshot CreateReadProjectedSnapshot(TorrentSnapshot existing, TorrentManager manager)
    {
        var state = MapState(manager, existing.State, existing.DesiredState);
        var totalBytes = manager.HasMetadata ? manager.Torrent?.Size ?? existing.TotalBytes :
                existing.TotalBytes                                  ?? manager.MagnetLink?.Size;

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
            CompletionCallbackState = existing.CompletionCallbackState,
            CompletionCallbackPendingSinceUtc = existing.CompletionCallbackPendingSinceUtc,
            CompletionCallbackInvokedAtUtc = existing.CompletionCallbackInvokedAtUtc,
            CompletionCallbackLastError = existing.CompletionCallbackLastError,
            CompletionCallbackFeedbackReceivedAtUtc = existing.CompletionCallbackFeedbackReceivedAtUtc,
            CompletionCallbackFeedbackJson = existing.CompletionCallbackFeedbackJson,
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
            DownloadColdSinceUtc = existing.DownloadColdSinceUtc,
            LastActivityAtUtc = existing.LastActivityAtUtc,
            ErrorMessage = state == ContractTorrentState.Error ?
                    manager.Error?.Reason.ToString() ?? existing.ErrorMessage : null,
        };

        if (ShouldPreservePersistedCompletion(existing, manager))
        {
            projectedSnapshot = CreateRecoveredCompletedSnapshot(
                existing, projectedSnapshot,
                existing.LastActivityAtUtc ?? existing.CompletedAtUtc ?? DateTimeOffset.UtcNow
            );
        }
        if (state is ContractTorrentState.Paused or ContractTorrentState.Queued or ContractTorrentState.Completed or
            ContractTorrentState.Error)
        {
            projectedSnapshot.ConnectedPeerCount         = 0;
            projectedSnapshot.DownloadRateBytesPerSecond = 0;
            projectedSnapshot.UploadRateBytesPerSecond   = 0;
        }

        return existing.State == ContractTorrentState.WaitingForFileCompletion
            ? ApplyFileCompletionVisibilityIfNeeded(projectedSnapshot, manager, finalizationResult: null)
            : projectedSnapshot;
    }

    private static TorrentSnapshot CreateQueuedSnapshot(TorrentSnapshot snapshot, DateTimeOffset now)
    {
        snapshot.DesiredState               = TorrentDesiredState.Runnable;
        snapshot.State                      = ContractTorrentState.Queued;
        snapshot.ConnectedPeerCount         = 0;
        snapshot.DownloadRateBytesPerSecond = 0;
        snapshot.UploadRateBytesPerSecond   = 0;
        snapshot.LastActivityAtUtc          = now;
        return snapshot;
    }

    private void RegisterManager(Guid torrentId, TorrentManager manager)
    {
        _torrentIdsByManager[manager] = torrentId;
        if (!_observedTorrentIds.Add(torrentId))
        {
            return;
        }

        manager.TorrentStateChanged += (_, eventArgs) => _ = HandleTorrentStateChangedAsync(torrentId, eventArgs);
        manager.PeersFound          += (_, eventArgs) => _ = HandlePeersFoundAsync(torrentId, eventArgs);
        manager.PeerConnected       += (_, eventArgs) => _ = HandlePeerConnectedAsync(torrentId, eventArgs);
        manager.PeerDisconnected    += (_, eventArgs) => _ = HandlePeerDisconnectedAsync(torrentId, eventArgs);
        manager.ConnectionAttemptFailed +=
                (_, eventArgs) => _ = HandleConnectionAttemptFailedAsync(torrentId, eventArgs);
    }

    private async Task HandleTorrentStateChangedAsync(Guid torrentId, TorrentStateChangedEventArgs eventArgs)
    {
        try
        {
            var snapshot = await torrentStateStore.GetAsync(torrentId, CancellationToken.None);

            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Information,
                    Category = "engine",
                    EventType = "torrent.engine.state_changed",
                    Message = $"Torrent engine state changed from '{eventArgs.OldState}' to '{eventArgs.NewState}'.",
                    TorrentId = torrentId,
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            OldState = eventArgs.OldState.ToString(),
                            NewState = eventArgs.NewState.ToString(),
                            ContractState = MapState(
                                        eventArgs.TorrentManager, snapshot?.State ?? ContractTorrentState.Queued,
                                        snapshot?.DesiredState                    ?? TorrentDesiredState.Runnable
                                    )
                                   .ToString(),
                            eventArgs.TorrentManager.HasMetadata,
                            ProgressPercent = eventArgs.TorrentManager.Progress,
                        }
                    ),
                }, CancellationToken.None
            );
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception, "Failed handling MonoTorrent state change event for torrent {TorrentId}", torrentId
            );
        }
    }

    private Task HandlePeersFoundAsync(Guid torrentId, PeersAddedEventArgs eventArgs)
    {
        var now = DateTimeOffset.UtcNow;
        _connectionActivitySummaries.RegisterPeersFound(torrentId, now, eventArgs.NewPeers);
        return Task.CompletedTask;
    }

    private Task HandlePeerConnectedAsync(Guid torrentId, PeerConnectedEventArgs eventArgs)
    {
        var now = DateTimeOffset.UtcNow;
        _connectionActivitySummaries.RegisterPeerConnected(torrentId, now);
        NoteMetadataDiscoveryActivity(torrentId, now);
        return Task.CompletedTask;
    }

    private Task HandlePeerDisconnectedAsync(Guid torrentId, PeerDisconnectedEventArgs eventArgs)
    {
        _connectionActivitySummaries.RegisterPeerDisconnected(torrentId, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    private async Task WriteCacheAuditAsync(string cacheDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var audit = MonoTorrentCacheInspector.Inspect(cacheDirectory, DateTimeOffset.UtcNow);
            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Information,
                    Category = "runtime",
                    EventType = "runtime.monotorrent.cache_audit",
                    Message = "MonoTorrent cache inventory completed.",
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(audit),
                }, cancellationToken
            );
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed inspecting MonoTorrent cache directory {CacheDirectory}", cacheDirectory);
        }
    }

    private async Task ProcessMetadataRecoveryAsync(RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now,
        CancellationToken                                                   cancellationToken)
    {
        var managedTorrents = await GetManagedTorrentsAsync(cancellationToken);

        foreach (var (snapshot, manager) in managedTorrents)
        {
            var currentSnapshot = await torrentStateStore.GetAsync(snapshot.TorrentId, cancellationToken);
            if (currentSnapshot is null)
            {
                continue;
            }

            if (currentSnapshot.DesiredState != TorrentDesiredState.Runnable)
            {
                ResetMetadataRecoveryState(currentSnapshot.TorrentId);
                _metadataRecoveryAttemptCounts.TryRemove(currentSnapshot.TorrentId, out _);
                continue;
            }

            var isResolvingMetadata = currentSnapshot.State == ContractTorrentState.ResolvingMetadata &&
                    !manager.HasMetadata && !manager.Complete;
            var recoveryState = _metadataRecoveryStates.GetOrAdd(
                currentSnapshot.TorrentId, _ => new TorrentMetadataRecoveryState()
            );
            recoveryState.Observe(now, isResolvingMetadata, manager.HasMetadata || manager.Complete);

            if (!isResolvingMetadata)
            {
                ResetMetadataRecoveryState(currentSnapshot.TorrentId);
                _metadataRecoveryAttemptCounts.TryRemove(currentSnapshot.TorrentId, out _);
                continue;
            }

            var decision = recoveryState.Evaluate(
                now, runtimeSettings.MetadataRefreshStaleSeconds, runtimeSettings.MetadataRefreshRestartDelaySeconds
            );

            switch (decision.Action)
            {
                case MetadataRecoveryAction.Refresh:
                    await ExecuteRecoveryActionAsync(
                        "metadata", decision.Action.ToString(), currentSnapshot, _metadataRecoveryAttemptCounts,
                        async () => await RequestMetadataDiscoveryRefreshAsync(
                            currentSnapshot, manager, now, "automatic_stale_metadata", cancellationToken,
                            decision
                        ),
                        decision
                    );
                break;
                case MetadataRecoveryAction.Restart:
                    await ExecuteRecoveryActionAsync(
                        "metadata", decision.Action.ToString(), currentSnapshot, _metadataRecoveryAttemptCounts,
                        async () => await RestartMetadataResolutionAsync(
                            currentSnapshot, manager, now, runtimeSettings, cancellationToken,
                            decision
                        ),
                        decision
                    );
                break;
                case MetadataRecoveryAction.Reset:
                    await ExecuteRecoveryActionAsync(
                        "metadata", decision.Action.ToString(), currentSnapshot, _metadataRecoveryAttemptCounts,
                        async () => await ResetMetadataResolutionAsync(
                            currentSnapshot, manager, now, runtimeSettings, cancellationToken,
                            decision
                        ),
                        decision
                    );
                break;
            }
        }
    }

    private async Task ProcessDownloadRecoveryAsync(RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now,
        CancellationToken                                                   cancellationToken)
    {
        var managedTorrents = await GetManagedTorrentsAsync(cancellationToken);

        foreach (var (snapshot, manager) in managedTorrents)
        {
            var currentSnapshot = await torrentStateStore.GetAsync(snapshot.TorrentId, cancellationToken);
            if (currentSnapshot is null)
            {
                continue;
            }

            if (currentSnapshot.DesiredState != TorrentDesiredState.Runnable)
            {
                ResetDownloadRecoveryState(currentSnapshot.TorrentId);
                _downloadRecoveryAttemptCounts.TryRemove(currentSnapshot.TorrentId, out _);
                if (currentSnapshot.DownloadColdSinceUtc is not null)
                {
                    currentSnapshot.DownloadColdSinceUtc = null;
                    await torrentStateStore.UpdateAsync(currentSnapshot, cancellationToken);
                }
                continue;
            }

            var isTrackedDownload = currentSnapshot.State == ContractTorrentState.Downloading &&
                    manager.HasMetadata && !manager.Complete;
            var recoveryState = _downloadRecoveryStates.GetOrAdd(
                currentSnapshot.TorrentId,
                _ => new TorrentDownloadRecoveryState(
                    currentSnapshot.DownloadColdSinceUtc,
                    currentSnapshot.DownloadedBytes
                )
            );

            if (!isTrackedDownload)
            {
                if (currentSnapshot.State == ContractTorrentState.Queued && manager.HasMetadata && !manager.Complete)
                {
                    recoveryState.Suspend(now);
                }
                else
                {
                    ResetDownloadRecoveryState(currentSnapshot.TorrentId);
                    _downloadRecoveryAttemptCounts.TryRemove(currentSnapshot.TorrentId, out _);
                    if (currentSnapshot.DownloadColdSinceUtc is not null)
                    {
                        currentSnapshot.DownloadColdSinceUtc = null;
                        await torrentStateStore.UpdateAsync(currentSnapshot, cancellationToken);
                    }
                }

                continue;
            }

            recoveryState.Observe(
                now, true, currentSnapshot.DownloadedBytes,
                currentSnapshot.DownloadRateBytesPerSecond, manager.OpenConnections
            );

            var coldSinceUtc = recoveryState.GetColdSinceUtc();
            if (currentSnapshot.DownloadColdSinceUtc != coldSinceUtc)
            {
                currentSnapshot.DownloadColdSinceUtc = coldSinceUtc;
                await torrentStateStore.UpdateAsync(currentSnapshot, cancellationToken);
            }

            var decision = recoveryState.Evaluate(
                now, runtimeSettings.MetadataRefreshStaleSeconds, runtimeSettings.MetadataRefreshRestartDelaySeconds,
                runtimeSettings.ColdDownloadRecoveryThresholdMinutes,
                runtimeSettings.ColdDownloadRecoveryIntervalMinutes
            );

            switch (decision.Action)
            {
                case DownloadRecoveryAction.Refresh:
                    await ExecuteRecoveryActionAsync(
                        "download", decision.Action.ToString(), currentSnapshot, _downloadRecoveryAttemptCounts,
                        async () => await RequestDownloadPeerRefreshAsync(
                            currentSnapshot, manager, now, "automatic_stale_download", cancellationToken,
                            decision
                        ),
                        decision
                    );
                break;
                case DownloadRecoveryAction.Restart:
                    await ExecuteRecoveryActionAsync(
                        "download", decision.Action.ToString(), currentSnapshot, _downloadRecoveryAttemptCounts,
                        async () => await RestartDownloadPeerRecoveryAsync(
                            currentSnapshot, manager, now, runtimeSettings, cancellationToken,
                            decision
                        ),
                        decision
                    );
                break;
            }
        }
    }

    private async Task<bool> QueuePeerDiscoveryAnnounceAsync(
        TorrentManager manager,
        CancellationToken cancellationToken)
    {
        await EnsureManagerStartedAsync(manager, cancellationToken);
        var torrentId = GetTorrentId(manager) ?? throw new InvalidOperationException(
            $"MonoTorrent manager '{manager.Name}' is not registered."
        );
        var usedTrackerAnnounce = manager.TrackerManager is not null && CountTrackers(manager) > 0;
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_peerDiscoveryAnnounceTasks.TryAdd(torrentId, completionSource.Task))
        {
            return usedTrackerAnnounce;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await RunPeerDiscoveryAnnounceInBackgroundAsync(
                        torrentId,
                        manager,
                        usedTrackerAnnounce,
                        _backgroundOperationCts.Token
                    );
                }
                finally
                {
                    completionSource.TrySetResult();
                    _peerDiscoveryAnnounceTasks.TryRemove(torrentId, out _);
                }
            },
            CancellationToken.None
        );

        return usedTrackerAnnounce;
    }

    private async Task RunPeerDiscoveryAnnounceInBackgroundAsync(
        Guid torrentId,
        TorrentManager manager,
        bool usedTrackerAnnounce,
        CancellationToken cancellationToken)
    {
        try
        {
            await MeasureMonoTorrentOperationAsync(
                "manager_dht_announce",
                torrentId,
                manager.Name,
                async () => await manager.DhtAnnounceAsync()
            );

            if (!usedTrackerAnnounce)
            {
                return;
            }

            using var timeoutCts = new CancellationTokenSource(RecoveryTrackerAnnounceTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token
            );
            try
            {
                await MeasureMonoTorrentOperationAsync(
                    "manager_tracker_announce",
                    torrentId,
                    manager.Name,
                    async () => await manager.TrackerManager!.AnnounceAsync(TorrentEvent.Started, linkedCts.Token)
                );
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested &&
                                                      !cancellationToken.IsCancellationRequested)
            {
                await activityLogService.TryWriteActivityLogAsync(
                    new ActivityLogWriteRequest
                    {
                        Level = ActivityLogLevel.Warning,
                        Category = "runtime",
                        EventType = "runtime.recovery.announce_timed_out",
                        Message = $"Recovery tracker announce for torrent '{manager.Name}' exceeded the time limit.",
                        TorrentId = torrentId,
                        ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                        DetailsJson = JsonSerializer.Serialize(
                            new
                            {
                                TimeoutMilliseconds = RecoveryTrackerAnnounceTimeout.TotalMilliseconds,
                                TorrentId = torrentId,
                                TorrentName = manager.Name,
                            }
                        ),
                    }, CancellationToken.None
                );
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Background peer discovery announce failed for torrent {TorrentId}", torrentId);
            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Warning,
                    Category = "runtime",
                    EventType = "runtime.recovery.announce_failed",
                    Message = $"Background recovery announce failed for torrent '{manager.Name}'.",
                    TorrentId = torrentId,
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            TorrentId = torrentId,
                            TorrentName = manager.Name,
                            Error = exception.Message,
                        }
                    ),
                }, CancellationToken.None
            );
        }
    }

    private async Task StopBackgroundOperationsAsync()
    {
        _backgroundOperationCts.Cancel();
        if (!await managerStopCoordinator.DrainAsync(TimeSpan.FromSeconds(2)))
        {
            logger.LogDebug("Background completion manager stops did not all finish before shutdown continued");
        }
        var backgroundTasks = _peerDiscoveryAnnounceTasks.Values.ToArray();
        if (backgroundTasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(backgroundTasks).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            logger.LogDebug(exception, "Background recovery announces did not all stop before shutdown continued");
        }
    }

    private async Task RequestMetadataDiscoveryRefreshAsync(TorrentSnapshot snapshot, TorrentManager manager,
        DateTimeOffset now, string origin, CancellationToken cancellationToken,
        TorrentMetadataRecoveryDecision? decision = null)
    {
        var recoveryState = _metadataRecoveryStates.GetOrAdd(
            snapshot.TorrentId, _ => new TorrentMetadataRecoveryState()
        );
        var trackerCount        = CountTrackers(manager);
        var usedTrackerAnnounce = await QueuePeerDiscoveryAnnounceAsync(manager, cancellationToken);

        recoveryState.MarkRefresh(now);

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Information,
                Category          = "engine",
                EventType         = "torrent.metadata.refresh_requested",
                Message           = $"Requested metadata discovery refresh ({origin}).",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        Origin = origin,
                        snapshot.State,
                        manager.OpenConnections,
                        TrackerCount        = trackerCount,
                        UsedDhtAnnounce     = true,
                        UsedTrackerAnnounce = usedTrackerAnnounce,
                        runtimeSettings = decision is null ? null : new
                        {
                            decision.Value.ResolvingSinceUtc,
                            decision.Value.LastDiscoveryActivityAtUtc,
                            decision.Value.LastRefreshAtUtc,
                            decision.Value.LastRestartAtUtc,
                            decision.Value.LastResetAtUtc,
                            decision.Value.StaleSinceUtc,
                            decision.Value.RecoveryCycle,
                            decision.Value.BackoffMultiplier,
                            decision.Value.EffectiveStaleSeconds,
                            decision.Value.EffectiveRestartDelaySeconds,
                        },
                    }
                ),
            }, cancellationToken
        );
    }

    private async Task RestartMetadataResolutionAsync(TorrentSnapshot snapshot, TorrentManager manager,
        DateTimeOffset now, RuntimeSettingsSnapshot runtimeSettings, CancellationToken cancellationToken,
        TorrentMetadataRecoveryDecision decision)
    {
        var recoveryState = _metadataRecoveryStates.GetOrAdd(
            snapshot.TorrentId, _ => new TorrentMetadataRecoveryState()
        );
        recoveryState.MarkRestart(now);

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Warning,
                Category          = "engine",
                EventType         = "torrent.metadata.restart_requested",
                Message           = "Restarting metadata resolution after a stale discovery window.",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        snapshot.State,
                        manager.OpenConnections,
                        decision.ResolvingSinceUtc,
                        decision.LastDiscoveryActivityAtUtc,
                        decision.LastRefreshAtUtc,
                        decision.LastRestartAtUtc,
                        decision.LastResetAtUtc,
                        decision.StaleSinceUtc,
                        decision.RecoveryCycle,
                        decision.BackoffMultiplier,
                        decision.EffectiveStaleSeconds,
                        decision.EffectiveRestartDelaySeconds,
                        runtimeSettings.MetadataRefreshStaleSeconds,
                        runtimeSettings.MetadataRefreshRestartDelaySeconds,
                    }
                ),
            }, cancellationToken
        );

        await EnsureManagerStoppedAsync(manager, cancellationToken);
        await EnsureManagerStartedAsync(manager, cancellationToken);
        await RequestMetadataDiscoveryRefreshAsync(
            snapshot, manager, now, "automatic_stale_restart", cancellationToken,
            decision
        );
    }

    private async Task RequestDownloadPeerRefreshAsync(TorrentSnapshot snapshot, TorrentManager manager,
        DateTimeOffset now, string origin, CancellationToken cancellationToken,
        TorrentDownloadRecoveryDecision? decision = null)
    {
        var recoveryState = _downloadRecoveryStates.GetOrAdd(
            snapshot.TorrentId, _ => new TorrentDownloadRecoveryState()
        );
        var trackerCount        = CountTrackers(manager);
        var usedTrackerAnnounce = await QueuePeerDiscoveryAnnounceAsync(manager, cancellationToken);

        recoveryState.MarkRefresh(now);

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Information,
                Category          = "engine",
                EventType         = "torrent.download.refresh_requested",
                Message           = $"Requested download peer refresh ({origin}).",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        Origin = origin,
                        snapshot.State,
                        snapshot.DownloadedBytes,
                        snapshot.DownloadRateBytesPerSecond,
                        manager.OpenConnections,
                        TrackerCount        = trackerCount,
                        UsedDhtAnnounce     = true,
                        UsedTrackerAnnounce = usedTrackerAnnounce,
                        decision = decision is null ? null : new
                        {
                            decision.Value.DownloadingSinceUtc,
                            decision.Value.LastUsefulActivityAtUtc,
                            decision.Value.LastActionAtUtc,
                            LastRecoveryAction = decision.Value.LastRecoveryAction.ToString(),
                            decision.Value.StaleSinceUtc,
                            decision.Value.RecoveryCycle,
                            decision.Value.BackoffMultiplier,
                            decision.Value.EffectiveStaleSeconds,
                            decision.Value.EffectiveRestartDelaySeconds,
                            decision.Value.LongColdMode,
                            decision.Value.LongColdSinceUtc,
                            decision.Value.EffectiveRecoveryIntervalMinutes,
                        },
                    }
                ),
            }, cancellationToken
        );
    }

    private async Task RestartDownloadPeerRecoveryAsync(TorrentSnapshot snapshot, TorrentManager manager,
        DateTimeOffset now, RuntimeSettingsSnapshot runtimeSettings, CancellationToken cancellationToken,
        TorrentDownloadRecoveryDecision decision)
    {
        var recoveryState = _downloadRecoveryStates.GetOrAdd(
            snapshot.TorrentId, _ => new TorrentDownloadRecoveryState()
        );
        recoveryState.MarkRestart(now);

        await EnsureManagerStoppedAsync(manager, cancellationToken);
        await EnsureManagerStartedAsync(manager, cancellationToken);

        var trackerCount        = CountTrackers(manager);
        var usedTrackerAnnounce = await QueuePeerDiscoveryAnnounceAsync(manager, cancellationToken);

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Warning,
                Category          = "engine",
                EventType         = "torrent.download.restart_requested",
                Message           = "Restarting a stalled download after a zero-peer stale window.",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        snapshot.State,
                        snapshot.DownloadedBytes,
                        snapshot.DownloadRateBytesPerSecond,
                        manager.OpenConnections,
                        TrackerCount        = trackerCount,
                        UsedDhtAnnounce     = true,
                        UsedTrackerAnnounce = usedTrackerAnnounce,
                        decision.DownloadingSinceUtc,
                        decision.LastUsefulActivityAtUtc,
                        decision.LastActionAtUtc,
                        LastRecoveryAction = decision.LastRecoveryAction.ToString(),
                        decision.StaleSinceUtc,
                        decision.RecoveryCycle,
                        decision.BackoffMultiplier,
                        decision.EffectiveStaleSeconds,
                        decision.EffectiveRestartDelaySeconds,
                        decision.LongColdMode,
                        decision.LongColdSinceUtc,
                        decision.EffectiveRecoveryIntervalMinutes,
                        runtimeSettings.MetadataRefreshStaleSeconds,
                        runtimeSettings.MetadataRefreshRestartDelaySeconds,
                    }
                ),
            }, cancellationToken
        );
    }

    private async Task ResetMetadataResolutionAsync(TorrentSnapshot snapshot, TorrentManager manager,
        DateTimeOffset now, RuntimeSettingsSnapshot runtimeSettings, CancellationToken cancellationToken,
        TorrentMetadataRecoveryDecision decision)
    {
        var recreatedManager = await ResetMetadataSessionCoreAsync(
            snapshot, manager, now, "automatic_stale_reset", cancellationToken,
            decision
        );
        await RequestMetadataDiscoveryRefreshAsync(
            snapshot, recreatedManager, now, "automatic_stale_reset", cancellationToken,
            decision
        );

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Warning,
                Category          = "engine",
                EventType         = "torrent.metadata.reset_applied",
                Message           = "Recreated metadata discovery session after refresh and restart were not enough.",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        snapshot.State,
                        decision.ResolvingSinceUtc,
                        decision.LastDiscoveryActivityAtUtc,
                        decision.LastRefreshAtUtc,
                        decision.LastRestartAtUtc,
                        decision.LastResetAtUtc,
                        decision.StaleSinceUtc,
                        decision.RecoveryCycle,
                        decision.BackoffMultiplier,
                        decision.EffectiveStaleSeconds,
                        decision.EffectiveRestartDelaySeconds,
                        runtimeSettings.MetadataRefreshStaleSeconds,
                        runtimeSettings.MetadataRefreshRestartDelaySeconds,
                    }
                ),
            }, cancellationToken
        );
    }

    private async Task<TorrentManager> ResetMetadataSessionCoreAsync(TorrentSnapshot snapshot, TorrentManager manager,
        DateTimeOffset now, string origin, CancellationToken cancellationToken,
        TorrentMetadataRecoveryDecision? decision = null)
    {
        var recoveryState = _metadataRecoveryStates.GetOrAdd(
            snapshot.TorrentId, _ => new TorrentMetadataRecoveryState()
        );
        recoveryState.MarkReset(now);

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Warning,
                Category          = "engine",
                EventType         = "torrent.metadata.reset_requested",
                Message           = $"Recreating metadata discovery session ({origin}).",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        Origin = origin,
                        snapshot.State,
                        manager.OpenConnections,
                        TrackerCount = CountTrackers(manager),
                        decision = decision is null ? null : new
                        {
                            decision.Value.ResolvingSinceUtc,
                            decision.Value.LastDiscoveryActivityAtUtc,
                            decision.Value.LastRefreshAtUtc,
                            decision.Value.LastRestartAtUtc,
                            decision.Value.LastResetAtUtc,
                            decision.Value.StaleSinceUtc,
                            decision.Value.RecoveryCycle,
                            decision.Value.BackoffMultiplier,
                            decision.Value.EffectiveStaleSeconds,
                            decision.Value.EffectiveRestartDelaySeconds,
                        },
                    }
                ),
            }, cancellationToken
        );

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureManagerStoppedAsync(manager, cancellationToken);
            await MeasureMonoTorrentOperationAsync(
                "engine_remove_metadata_session",
                snapshot.TorrentId,
                snapshot.Name,
                async () => await _engine!.RemoveAsync(manager, RemoveMode.CacheDataOnly)
            );

            _managers.Remove(snapshot.TorrentId);
            _torrentIdsByManager.TryRemove(manager, out _);
            _observedTorrentIds.Remove(snapshot.TorrentId);
            _observedUploadedSessionBytes.TryRemove(snapshot.TorrentId, out _);

            var magnet = MagnetLink.Parse(snapshot.MagnetUri);
            var downloadRootPath =
                    MonoTorrentRecoveryPathResolver.ResolveDownloadRootPath(snapshot, servicePaths.DownloadRootPath);
            var recreatedManager = await MeasureMonoTorrentOperationAsync(
                "engine_recreate_metadata_session",
                snapshot.TorrentId,
                snapshot.Name,
                async () => await _engine!.AddAsync(magnet, downloadRootPath)
            );
            RegisterManager(snapshot.TorrentId, recreatedManager);
            _managers[snapshot.TorrentId] = recreatedManager;
            return recreatedManager;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void NoteMetadataDiscoveryActivity(Guid torrentId, DateTimeOffset now)
    {
        _metadataRecoveryStates.GetOrAdd(torrentId, _ => new TorrentMetadataRecoveryState())
                               .NoteDiscoveryActivity(now);
    }

    private void ResetMetadataRecoveryState(Guid torrentId)
    {
        if (_metadataRecoveryStates.TryGetValue(torrentId, out var state))
        {
            state.Reset();
        }
    }

    private void ResetDownloadRecoveryState(Guid torrentId)
    {
        if (_downloadRecoveryStates.TryGetValue(torrentId, out var state))
        {
            state.Reset();
        }
    }

    private Task HandleConnectionAttemptFailedAsync(Guid torrentId, ConnectionAttemptFailedEventArgs eventArgs)
    {
        _connectionActivitySummaries.RegisterConnectionFailure(
            torrentId,
            DateTimeOffset.UtcNow,
            eventArgs.Reason.ToString()
        );
        return Task.CompletedTask;
    }

    private async Task WriteConnectionActivitySummariesAsync(
        DateTimeOffset now,
        IReadOnlyList<KeyValuePair<Guid, TorrentManager>> managers,
        CancellationToken cancellationToken)
    {
        foreach (var summary in _connectionActivitySummaries.DrainReady(now, ConnectionActivitySummaryInterval))
        {
            var manager = managers.FirstOrDefault(entry => entry.Key == summary.TorrentId).Value;
            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Information,
                    Category = "runtime",
                    EventType = "runtime.connection.activity_summary",
                    Message = $"Connection activity summary for torrent '{manager?.Name ?? summary.TorrentId.ToString()}'.",
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            summary.TorrentId,
                            TorrentName = manager?.Name,
                            summary.WindowStartedAtUtc,
                            summary.WindowEndedAtUtc,
                            WindowSeconds = (summary.WindowEndedAtUtc - summary.WindowStartedAtUtc).TotalSeconds,
                            summary.PeersFoundEvents,
                            summary.NewPeersFound,
                            summary.PeerConnectedEvents,
                            summary.PeerDisconnectedEvents,
                            summary.ConnectionFailureEvents,
                            summary.ConnectionFailuresByReason,
                        }
                    ),
                },
                cancellationToken
            );
        }
    }

    private static long CalculateDownloadedBytes(long? totalBytes, double progressPercent, long existingDownloadedBytes)
    {
        if (totalBytes is null)
        {
            return existingDownloadedBytes;
        }

        var boundedProgress = Math.Clamp(progressPercent, 0, 100);
        return (long) Math.Round(totalBytes.Value * (boundedProgress / 100d), MidpointRounding.AwayFromZero);
    }

    private static long CalculateRecoveredCompletedDownloadedBytes(TorrentSnapshot snapshot)
    {
        return snapshot.TotalBytes is > 0 ? Math.Max(snapshot.DownloadedBytes, snapshot.TotalBytes.Value) :
                snapshot.DownloadedBytes;
    }

    private long CalculateUploadedBytes(Guid torrentId, long existingUploadedBytes, long currentSessionUploadedBytes)
    {
        if (!_observedUploadedSessionBytes.TryGetValue(torrentId, out var previousSessionUploadedBytes))
        {
            _observedUploadedSessionBytes[torrentId] = currentSessionUploadedBytes;
            return existingUploadedBytes + Math.Max(0L, currentSessionUploadedBytes);
        }

        _observedUploadedSessionBytes[torrentId] = currentSessionUploadedBytes;
        var delta = currentSessionUploadedBytes >= previousSessionUploadedBytes ?
                currentSessionUploadedBytes - previousSessionUploadedBytes : currentSessionUploadedBytes;
        return existingUploadedBytes + Math.Max(0L, delta);
    }

    internal static DateTimeOffset? ResolveCompletedAtUtc(
        DateTimeOffset? existingCompletedAtUtc,
        ContractTorrentState state,
        DateTimeOffset now)
    {
        return state is ContractTorrentState.Completed or ContractTorrentState.Seeding
            ? existingCompletedAtUtc ?? now
            : null;
    }

    private static DateTimeOffset? ResolveSeedingStartedAtUtc(DateTimeOffset? existingSeedingStartedAtUtc,
        ContractTorrentState                                                  state, DateTimeOffset now)
    {
        return state == ContractTorrentState.Seeding ? existingSeedingStartedAtUtc ?? now : existingSeedingStartedAtUtc;
    }

    private async Task<SeedingPolicyDecision> ShouldStopSeedingAsync(TorrentSnapshot snapshot, DateTimeOffset now,
        CancellationToken                                                            cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);

        return SeedingPolicyEvaluator.Evaluate(
            runtimeSettings.SeedingStopMode, runtimeSettings.SeedingStopRatio, runtimeSettings.SeedingStopMinutes,
            snapshot.UploadedBytes, snapshot.TotalBytes, snapshot.SeedingStartedAtUtc, now
        );
    }

    private async Task<SeedingPolicyApplicationResult> ApplySeedingPolicyIfNeededAsync(
        TorrentSnapshot snapshot,
        TorrentManager manager,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        TorrentCompletionFinalizationCheckResult? finalizationResult)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var seedingDecision = SeedingPolicyEvaluator.Evaluate(
            runtimeSettings.SeedingStopMode, runtimeSettings.SeedingStopRatio, runtimeSettings.SeedingStopMinutes,
            snapshot.UploadedBytes, snapshot.TotalBytes, snapshot.SeedingStartedAtUtc, now
        );
        if (!seedingDecision.ShouldStop)
        {
            return new SeedingPolicyApplicationResult(snapshot, EngineStopReady: true);
        }

        var engineStopReady = IsManagerStoppedForCompletion(manager);
        if (finalizationResult?.IsReady != true)
        {
            return new SeedingPolicyApplicationResult(snapshot, engineStopReady);
        }

        if (!engineStopReady)
        {
            engineStopReady = managerStopCoordinator.TryTakeCompletedOrSchedule(
                snapshot.TorrentId,
                snapshot.Name,
                async backgroundCancellationToken =>
                    await EnsureManagerStoppedAsync(manager, backgroundCancellationToken),
                _backgroundOperationCts.Token,
                out _
            );
        }

        var completedSnapshot = new TorrentSnapshot
        {
            TorrentId                         = snapshot.TorrentId,
            Name                              = snapshot.Name,
            CategoryKey                       = snapshot.CategoryKey,
            CompletionCallbackLabel           = snapshot.CompletionCallbackLabel,
            InvokeCompletionCallback          = snapshot.InvokeCompletionCallback,
            CompletionCallbackState           = snapshot.CompletionCallbackState,
            CompletionCallbackPendingSinceUtc = snapshot.CompletionCallbackPendingSinceUtc,
            CompletionCallbackInvokedAtUtc    = snapshot.CompletionCallbackInvokedAtUtc,
            CompletionCallbackLastError       = snapshot.CompletionCallbackLastError,
            CompletionCallbackFeedbackReceivedAtUtc = snapshot.CompletionCallbackFeedbackReceivedAtUtc,
            CompletionCallbackFeedbackJson = snapshot.CompletionCallbackFeedbackJson,
            State                             = ContractTorrentState.Completed,
            DesiredState                      = snapshot.DesiredState,
            MagnetUri                         = snapshot.MagnetUri,
            InfoHash                          = snapshot.InfoHash,
            DownloadRootPath                  = snapshot.DownloadRootPath,
            SavePath                          = snapshot.SavePath,
            ProgressPercent                   = snapshot.ProgressPercent,
            DownloadedBytes                   = snapshot.DownloadedBytes,
            UploadedBytes                     = snapshot.UploadedBytes,
            TotalBytes                        = snapshot.TotalBytes,
            ConnectedPeerCount                = 0,
            DownloadRateBytesPerSecond        = 0,
            UploadRateBytesPerSecond          = 0,
            TrackerCount                      = snapshot.TrackerCount,
            AddedAtUtc                        = snapshot.AddedAtUtc,
            CompletedAtUtc                    = snapshot.CompletedAtUtc,
            SeedingStartedAtUtc               = snapshot.SeedingStartedAtUtc,
            LastActivityAtUtc                 = now,
            ErrorMessage                      = snapshot.ErrorMessage,
        };

        if (!engineStopReady)
        {
            return new SeedingPolicyApplicationResult(completedSnapshot, EngineStopReady: false);
        }

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level     = ActivityLogLevel.Information,
                Category  = "torrent",
                EventType = "torrent.seeding.stopped_policy",
                Message =
                        $"Stopped seeding for torrent '{snapshot.Name}' because the '{seedingDecision.Reason}' policy was reached.",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        seedingDecision.Reason,
                        seedingDecision.CurrentRatio,
                        seedingDecision.CurrentSeedingMinutes, runtimeSettings.SeedingStopMode,
                        runtimeSettings.SeedingStopRatio, runtimeSettings.SeedingStopMinutes,
                    }
                ),
            }, cancellationToken
        );

        return new SeedingPolicyApplicationResult(completedSnapshot, EngineStopReady: true);
    }

    private static TorrentSnapshot ApplyFileCompletionVisibilityIfNeeded(
        TorrentSnapshot snapshot,
        TorrentManager manager,
        TorrentCompletionFinalizationCheckResult? finalizationResult)
    {
        if (snapshot.State is ContractTorrentState.Completed or ContractTorrentState.Paused or ContractTorrentState.Error or
            ContractTorrentState.Removed or ContractTorrentState.ResolvingMetadata)
        {
            return snapshot;
        }

        if (!manager.HasMetadata)
        {
            return snapshot;
        }

        var looksDownloadComplete = manager.Complete ||
                                    snapshot.ProgressPercent >= 100d ||
                                    snapshot.TotalBytes is > 0 && snapshot.DownloadedBytes >= snapshot.TotalBytes.Value;
        if (!looksDownloadComplete)
        {
            return snapshot;
        }

        if (finalizationResult?.IsReady == true)
        {
            return snapshot;
        }

        snapshot.State = ContractTorrentState.WaitingForFileCompletion;
        snapshot.CompletedAtUtc = null;
        snapshot.SeedingStartedAtUtc = null;
        snapshot.ConnectedPeerCount = 0;
        snapshot.DownloadRateBytesPerSecond = 0;
        snapshot.UploadRateBytesPerSecond = 0;
        return snapshot;
    }

    private static ContractTorrentState MapState(TorrentManager manager, ContractTorrentState existingState,
        TorrentDesiredState                                     desiredState)
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
            MonoTorrent.Client.TorrentState.Paused =>
                    desiredState == TorrentDesiredState.Paused ? ContractTorrentState.Paused :
                            ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Hashing or MonoTorrent.Client.TorrentState.HashingPaused or
                    MonoTorrent.Client.TorrentState.FetchingHashes => ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Starting => ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Stopping => desiredState == TorrentDesiredState.Paused ?
                    ContractTorrentState.Paused : existingState      == ContractTorrentState.Completed ?
                            ContractTorrentState.Completed : ContractTorrentState.Queued,
            MonoTorrent.Client.TorrentState.Stopped => existingState switch
            {
                ContractTorrentState.Completed => ContractTorrentState.Completed,
                _ when manager.Complete => ContractTorrentState.Queued,
                _ => ContractTorrentState.Queued,
            },
            _ => existingState,
        };
    }

    private static bool ShouldStartOnRecovery(TorrentSnapshot snapshot)
    {
        return snapshot.DesiredState == TorrentDesiredState.Runnable &&
                !HasPersistedCompletion(snapshot) &&
                snapshot.State is not ContractTorrentState.Completed and not ContractTorrentState.Error and
                        not ContractTorrentState.Removed;
    }

    private static bool ShouldPreservePersistedCompletion(TorrentSnapshot snapshot, TorrentManager manager)
    {
        return HasPersistedCompletion(snapshot) && !manager.Complete;
    }

    private static bool HasPersistedCompletion(TorrentSnapshot snapshot)
    {
        return snapshot.State is ContractTorrentState.Completed or ContractTorrentState.Seeding ||
               snapshot.CompletedAtUtc is not null;
    }

    internal static TorrentSnapshot NormalizeCompletedErrorSnapshot(TorrentSnapshot snapshot, bool finalPayloadVisible,
        DateTimeOffset now)
    {
        if (snapshot.State != ContractTorrentState.Error || !finalPayloadVisible || !LooksTransferComplete(snapshot))
        {
            return snapshot;
        }

        snapshot.State                      = ContractTorrentState.Completed;
        snapshot.ProgressPercent            = Math.Max(100d, snapshot.ProgressPercent);
        snapshot.CompletedAtUtc           ??= snapshot.SeedingStartedAtUtc ?? now;
        snapshot.ConnectedPeerCount         = 0;
        snapshot.DownloadRateBytesPerSecond = 0;
        snapshot.UploadRateBytesPerSecond   = 0;
        snapshot.ErrorMessage               = null;
        return snapshot;
    }

    private static TorrentSnapshot NormalizeCompletedErrorIfPayloadVisible(
        TorrentSnapshot snapshot,
        TorrentCompletionFinalizationCheckResult? finalizationResult,
        DateTimeOffset now)
    {
        if (snapshot.State != ContractTorrentState.Error)
        {
            return snapshot;
        }

        return NormalizeCompletedErrorSnapshot(snapshot, finalizationResult?.IsReady == true, now);
    }

    private static bool LooksTransferComplete(TorrentSnapshot snapshot)
    {
        return snapshot.CompletedAtUtc is not null ||
               snapshot.SeedingStartedAtUtc is not null ||
               snapshot.ProgressPercent >= 100d ||
               snapshot.TotalBytes is > 0 && snapshot.DownloadedBytes >= snapshot.TotalBytes.Value;
    }

    private static int CountTrackers(TorrentManager manager)
    {
        return manager.TrackerManager?.Tiers.Sum(tier => tier.Trackers.Count) ??
                manager.MagnetLink?.AnnounceUrls?.Count ?? 0;
    }

    private static bool CanPause(ContractTorrentState state)
    {
        return state is ContractTorrentState.Downloading or ContractTorrentState.Seeding or
                ContractTorrentState.Queued or ContractTorrentState.ResolvingMetadata or
                ContractTorrentState.WaitingForFileCompletion;
    }

    private static bool CanResume(ContractTorrentState state)
    {
        return state is ContractTorrentState.Paused or ContractTorrentState.Error;
    }

    private static bool IsManagerRunning(TorrentManager manager)
    {
        return manager.State is not MonoTorrent.Client.TorrentState.Stopped and
                not MonoTorrent.Client.TorrentState.Paused and not MonoTorrent.Client.TorrentState.Error;
    }

    private static bool IsManagerStoppedForCompletion(TorrentManager manager)
    {
        return manager.State is MonoTorrent.Client.TorrentState.Stopped or MonoTorrent.Client.TorrentState.Paused;
    }

    private async Task EnsureManagerStoppedAsync(TorrentManager manager, CancellationToken cancellationToken)
    {
        if (manager.State is MonoTorrent.Client.TorrentState.Stopped or MonoTorrent.Client.TorrentState.Stopping)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await MeasureMonoTorrentOperationAsync(
            "manager_stop",
            GetTorrentId(manager),
            manager.Name,
            async () => await manager.StopAsync(TimeSpan.FromSeconds(2))
        );
    }

    private async Task EnsureManagerPausedAsync(TorrentManager manager, CancellationToken cancellationToken)
    {
        await WaitForManagerToBecomeRestartableAsync(manager, cancellationToken);

        if (manager.State is MonoTorrent.Client.TorrentState.Paused or MonoTorrent.Client.TorrentState.Stopped)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await MeasureMonoTorrentOperationAsync(
            "manager_pause",
            GetTorrentId(manager),
            manager.Name,
            async () => await manager.PauseAsync()
        );
    }

    private async Task EnsureManagerStartedAsync(TorrentManager manager, CancellationToken cancellationToken)
    {
        await WaitForManagerToBecomeRestartableAsync(manager, cancellationToken);

        if (!IsManagerRunning(manager))
        {
            await MeasureMonoTorrentOperationAsync(
                "manager_start",
                GetTorrentId(manager),
                manager.Name,
                async () => await manager.StartAsync()
            );
        }
    }

    private static async Task WaitForManagerToBecomeRestartableAsync(TorrentManager manager,
        CancellationToken                                                           cancellationToken)
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
                    StatusCodes.Status409Conflict, nameof(manager)
                );
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private static TorrentSnapshot CreatePausedSnapshot(TorrentSnapshot snapshot, DateTimeOffset now)
    {
        snapshot.DesiredState               = TorrentDesiredState.Paused;
        snapshot.State                      = ContractTorrentState.Paused;
        snapshot.ConnectedPeerCount         = 0;
        snapshot.DownloadRateBytesPerSecond = 0;
        snapshot.UploadRateBytesPerSecond   = 0;
        snapshot.LastActivityAtUtc          = now;
        return snapshot;
    }

    private async Task<TorrentSnapshot> PreserveLatestPersistedCallbackProgressAsync(TorrentSnapshot candidate,
        CancellationToken cancellationToken)
    {
        var latest = await torrentStateStore.GetAsync(candidate.TorrentId, cancellationToken);
        if (latest is null || !ShouldPreserveLatestPersistedCallbackProgress(candidate, latest))
        {
            return candidate;
        }

        candidate.CompletionCallbackState = latest.CompletionCallbackState;
        candidate.CompletionCallbackPendingSinceUtc = latest.CompletionCallbackPendingSinceUtc;
        candidate.CompletionCallbackInvokedAtUtc = latest.CompletionCallbackInvokedAtUtc;
        candidate.CompletionCallbackLastError = latest.CompletionCallbackLastError;
        candidate.CompletionCallbackFeedbackReceivedAtUtc = latest.CompletionCallbackFeedbackReceivedAtUtc;
        candidate.CompletionCallbackFeedbackJson = latest.CompletionCallbackFeedbackJson;
        return candidate;
    }

    private static bool ShouldPreserveLatestPersistedCallbackProgress(TorrentSnapshot candidate, TorrentSnapshot latest)
    {
        if (!string.IsNullOrWhiteSpace(latest.CompletionCallbackFeedbackJson) &&
            string.IsNullOrWhiteSpace(candidate.CompletionCallbackFeedbackJson))
        {
            return true;
        }

        return GetCallbackProgressRank(latest.CompletionCallbackState) >
               GetCallbackProgressRank(candidate.CompletionCallbackState);
    }

    private static int GetCallbackProgressRank(TorrentCompletionCallbackState? state)
    {
        return state switch
        {
            null => 0,
            TorrentCompletionCallbackState.PendingFinalization => 1,
            TorrentCompletionCallbackState.WaitingForFeedback => 2,
            TorrentCompletionCallbackState.Invoked => 3,
            TorrentCompletionCallbackState.Failed => 3,
            TorrentCompletionCallbackState.TimedOut => 3,
            _ => 0,
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
            CanRemove                         = snapshot.State is not ContractTorrentState.Removed,
        };
    }

    private TorrentDetailDto MapDetail(TorrentSnapshot snapshot, TorrentQueueDiagnostic diagnostic,
        RuntimeSettingsSnapshot?                       runtimeSettings, TorrentManager? manager = null)
    {
        var callbackFinalPayloadPath = Path.Combine(
            snapshot.DownloadRootPath ?? servicePaths.DownloadRootPath, snapshot.Name
        );
        string? callbackPendingReason = null;
        if (runtimeSettings is not null &&
            (snapshot.State == ContractTorrentState.WaitingForFileCompletion ||
             TorrentCompletionCallbackDiagnostics.ShouldSurfaceFinalizationStatus(
                 snapshot.CompletionCallbackState,
                 snapshot.CompletionCallbackLastError
             )))
        {
            var finalizationResult = CreateFinalizationCheckResult(snapshot, runtimeSettings, manager);
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
            CompletionCallbackFeedback         = CompletionCallbackFeedbackMapper.Deserialize(snapshot.CompletionCallbackFeedbackJson),
            ErrorMessage                       = snapshot.ErrorMessage,
            CanRefreshMetadata                 = CanRefreshMetadata(snapshot.State),
            CanRetryCompletionCallback         = CanRetryCompletionCallback(snapshot.CompletionCallbackState),
            CanPause                           = CanPause(snapshot.State),
            CanResume                          = CanResume(snapshot.State),
            CanRemove                          = snapshot.State is not ContractTorrentState.Removed,
        };
    }

    private static bool CanRefreshMetadata(ContractTorrentState state)
    {
        return state == ContractTorrentState.ResolvingMetadata;
    }

    private static bool CanRetryCompletionCallback(TorrentCompletionCallbackState? callbackState)
    {
        return callbackState is TorrentCompletionCallbackState.Failed or TorrentCompletionCallbackState.TimedOut;
    }

    private TorrentCompletionFinalizationCheckResult CreateFinalizationCheckResult(TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings, TorrentManager? manager)
    {
        return CreateFinalizationCheckResult(snapshot, runtimeSettings, GetObservedFilePaths(manager));
    }

    private TorrentCompletionFinalizationCheckResult CreateFinalizationCheckResult(TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings, IReadOnlyList<TorrentCompletionObservedFilePaths>? observedFiles)
    {
        return finalizationChecker.Check(snapshot, runtimeSettings, observedFiles);
    }

    private static IReadOnlyList<TorrentCompletionObservedFilePaths>? GetObservedFilePaths(TorrentManager? manager)
    {
        var observedFiles = manager is null || manager.Files.Count == 0
                ? null
                : manager.Files
                         .Where(file => !string.IsNullOrWhiteSpace(file.DownloadCompleteFullPath))
                         .Select(
                              file => new TorrentCompletionObservedFilePaths
                              {
                                  CompletePath = Path.GetFullPath(file.DownloadCompleteFullPath),
                              }
                          )
                         .ToArray();

        return observedFiles;
    }

    private static bool ShouldAutoRequeueFinalizationTimeout(TorrentSnapshot snapshot,
        TorrentCompletionFinalizationCheckResult? finalizationResult)
    {
        return snapshot.CompletionCallbackState == TorrentCompletionCallbackState.TimedOut &&
               snapshot.CompletionCallbackInvokedAtUtc is null &&
               !string.IsNullOrWhiteSpace(snapshot.CompletionCallbackLastError) &&
               TorrentCompletionCallbackDiagnostics.IsFinalizationVisibilityTimeout(snapshot.CompletionCallbackLastError) &&
               finalizationResult?.IsReady == true;
    }

    private static bool ShouldEvaluateTimedOutFinalization(TorrentSnapshot snapshot)
    {
        return snapshot.CompletionCallbackState == TorrentCompletionCallbackState.TimedOut &&
               snapshot.CompletionCallbackInvokedAtUtc is null &&
               TorrentCompletionCallbackDiagnostics.IsFinalizationVisibilityTimeout(
                   snapshot.CompletionCallbackLastError
               );
    }

    private static bool ShouldEvaluateCompletionFinalization(
        DateTimeOffset? previousCompletedAtUtc,
        TorrentSnapshot snapshot)
    {
        return previousCompletedAtUtc is null &&
               snapshot.CompletedAtUtc is not null &&
               (snapshot.State is ContractTorrentState.Completed or ContractTorrentState.Seeding or
                       ContractTorrentState.Queued) &&
               snapshot.InvokeCompletionCallback &&
               !string.IsNullOrWhiteSpace(snapshot.CompletionCallbackLabel) &&
               snapshot.CompletionCallbackState is null;
    }

    private static bool CanSkipCompletedSynchronization(TorrentSnapshot snapshot, TorrentManager manager)
    {
        if (snapshot.State != ContractTorrentState.Completed || !IsManagerStoppedForCompletion(manager))
        {
            return false;
        }

        if (!snapshot.InvokeCompletionCallback || string.IsNullOrWhiteSpace(snapshot.CompletionCallbackLabel))
        {
            return true;
        }

        return snapshot.CompletionCallbackState switch
        {
            TorrentCompletionCallbackState.WaitingForFeedback =>
                    snapshot.CompletionCallbackInvokedAtUtc is not null,
            TorrentCompletionCallbackState.Invoked or
            TorrentCompletionCallbackState.Failed => true,
            TorrentCompletionCallbackState.TimedOut =>
                    !TorrentCompletionCallbackDiagnostics.IsFinalizationVisibilityTimeout(
                        snapshot.CompletionCallbackLastError
                    ),
            _ => false,
        };
    }

    private async Task<TorrentManager?> TryGetManagerAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _managers.GetValueOrDefault(torrentId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MeasureStorageOperationAsync(
        string operation,
        Guid torrentId,
        Func<Task> action)
    {
        await MeasureStorageOperationAsync(
            operation,
            torrentId,
            async () =>
            {
                await action();
                return true;
            }
        );
    }

    private async Task<T> MeasureStorageOperationAsync<T>(
        string operation,
        Guid torrentId,
        Func<Task<T>> action)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "succeeded";
        try
        {
            return await action();
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await durationDiagnostics.RecordIfSlowAsync(
                "storage",
                operation,
                stopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.StorageSlowThreshold,
                outcome,
                torrentId
            );
        }
    }

    private async Task<T> MeasureMonoTorrentOperationAsync<T>(
        string operation,
        Guid? torrentId,
        string? torrentName,
        Func<Task<T>> action)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "succeeded";
        try
        {
            return await action();
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await durationDiagnostics.RecordIfSlowAsync(
                "monotorrent",
                operation,
                stopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.MonoTorrentSlowThreshold,
                outcome,
                torrentId,
                new { TorrentName = torrentName }
            );
        }
    }

    private async Task ExecuteRecoveryActionAsync<TDecision>(
        string recoveryKind,
        string action,
        TorrentSnapshot snapshot,
        ConcurrentDictionary<Guid, int> attemptCounts,
        Func<Task> operation,
        TDecision decision)
    {
        var attemptNumber = attemptCounts.AddOrUpdate(snapshot.TorrentId, 1, (_, count) => count + 1);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "succeeded";
        try
        {
            await operation();
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await durationDiagnostics.WriteRecoveryActionCompletedAsync(
                recoveryKind,
                action,
                attemptNumber,
                stopwatch.Elapsed,
                outcome,
                snapshot.TorrentId,
                snapshot.Name,
                decision
            );
        }
    }

    private Guid? GetTorrentId(TorrentManager manager)
    {
        return _torrentIdsByManager.TryGetValue(manager, out var torrentId) ? torrentId : null;
    }

    private async Task MeasureMonoTorrentOperationAsync(
        string operation,
        Guid? torrentId,
        string? torrentName,
        Func<Task> action)
    {
        await MeasureMonoTorrentOperationAsync(
            operation,
            torrentId,
            torrentName,
            async () =>
            {
                await action();
                return true;
            }
        );
    }

    private sealed record PendingCallbackWork(
        TorrentSnapshot Snapshot,
        IReadOnlyList<TorrentCompletionObservedFilePaths>? ObservedFiles,
        TorrentCompletionFinalizationCheckResult? FinalizationResult,
        bool EngineStopReady = true);

    private sealed record SeedingPolicyApplicationResult(TorrentSnapshot Snapshot, bool EngineStopReady);
}
