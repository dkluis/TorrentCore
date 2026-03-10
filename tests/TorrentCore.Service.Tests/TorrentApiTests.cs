using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class TorrentApiTests
{
    [Fact]
    public async Task GetHostStatus_ReturnsReadyHostContract()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(hostStatus);
        Assert.Equal("TorrentCore.Service", hostStatus.ServiceName);
        Assert.Equal("Fake", hostStatus.EngineRuntime);
        Assert.Equal(55_123, hostStatus.EngineListenPort);
        Assert.Equal(55_124, hostStatus.EngineDhtPort);
        Assert.Equal(150, hostStatus.EngineMaximumConnections);
        Assert.Equal(8, hostStatus.EngineMaximumHalfOpenConnections);
        Assert.Equal(0, hostStatus.EngineMaximumDownloadRateBytesPerSecond);
        Assert.Equal(0, hostStatus.EngineMaximumUploadRateBytesPerSecond);
        Assert.Equal(4, hostStatus.MaxActiveMetadataResolutions);
        Assert.Equal(4, hostStatus.MaxActiveDownloads);
        Assert.True(hostStatus.PartialFilesEnabled);
        Assert.Equal(".!mt", hostStatus.PartialFileSuffix);
        Assert.Equal(SeedingStopMode.Unlimited.ToString(), hostStatus.SeedingStopMode);
        Assert.Equal(EngineHostStatus.Ready, hostStatus.Status);
        Assert.True(hostStatus.SupportsMagnetAdds);
        Assert.True(hostStatus.SupportsPersistentStorage);
        Assert.True(hostStatus.StartupRecoveryCompleted);
        Assert.NotEqual(Guid.Empty, hostStatus.ServiceInstanceId);
    }

    [Fact]
    public async Task GetHostStatus_UsesConfiguredPaths_AndCreatesDirectories()
    {
        var rootPath = CreateTempRootPath("torrentcore-phase2-host");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(hostStatus);
        Assert.Equal(Path.GetFullPath(downloadPath), hostStatus.DownloadRootPath);
        Assert.True(Directory.Exists(downloadPath));
        Assert.True(Directory.Exists(storagePath));
        Assert.True(File.Exists(Path.Combine(storagePath, "torrentcore.db")));
        Assert.True(hostStatus.StartupRecoveryCompleted);
    }

    [Fact]
    public async Task GetHostStatus_UsesConfiguredEngineThrottleValues()
    {
        await using var factory = CreateFactory(
            engineMaximumConnections: 60,
            engineMaximumHalfOpenConnections: 4,
            engineMaximumDownloadRateBytesPerSecond: 12_500_000,
            engineMaximumUploadRateBytesPerSecond: 3_000_000);
        using var httpClient = factory.CreateClient();

        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(hostStatus);
        Assert.Equal(60, hostStatus.EngineMaximumConnections);
        Assert.Equal(4, hostStatus.EngineMaximumHalfOpenConnections);
        Assert.Equal(12_500_000, hostStatus.EngineMaximumDownloadRateBytesPerSecond);
        Assert.Equal(3_000_000, hostStatus.EngineMaximumUploadRateBytesPerSecond);
    }

    [Fact]
    public async Task GetRuntimeSettings_ReturnsEffectiveDefaults()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var settings = await httpClient.GetFromJsonAsync<RuntimeSettingsDto>("api/host/runtime-settings");

        Assert.NotNull(settings);
        Assert.Equal("Fake", settings.EngineRuntime);
        Assert.True(settings.SupportsLiveUpdates);
        Assert.False(settings.UsesPersistedOverrides);
        Assert.True(settings.PartialFilesEnabled);
        Assert.Equal(".!mt", settings.PartialFileSuffix);
        Assert.Equal(SeedingStopMode.Unlimited.ToString(), settings.SeedingStopMode);
        Assert.Equal(CompletedTorrentCleanupMode.Never.ToString(), settings.CompletedTorrentCleanupMode);
        Assert.Equal(60, settings.CompletedTorrentCleanupMinutes);
        Assert.Equal(5, settings.EngineConnectionFailureLogBurstLimit);
        Assert.Equal(60, settings.EngineConnectionFailureLogWindowSeconds);
        Assert.Equal(150, settings.EngineMaximumConnections);
        Assert.Equal(8, settings.EngineMaximumHalfOpenConnections);
        Assert.Equal(0, settings.EngineMaximumDownloadRateBytesPerSecond);
        Assert.Equal(0, settings.EngineMaximumUploadRateBytesPerSecond);
        Assert.Equal(4, settings.MaxActiveMetadataResolutions);
        Assert.Equal(4, settings.MaxActiveDownloads);
        Assert.Equal(150, settings.AppliedEngineMaximumConnections);
        Assert.Equal(8, settings.AppliedEngineMaximumHalfOpenConnections);
        Assert.Equal(0, settings.AppliedEngineMaximumDownloadRateBytesPerSecond);
        Assert.Equal(0, settings.AppliedEngineMaximumUploadRateBytesPerSecond);
        Assert.False(settings.EngineSettingsRequireRestart);
    }

    [Fact]
    public async Task UpdateRuntimeSettings_PersistsAcrossRestart_AndUpdatesHostStatus()
    {
        var rootPath = CreateTempRootPath("torrentcore-runtime-update");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using (var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var updateResponse = await httpClient.PutAsJsonAsync("api/host/runtime-settings", new UpdateRuntimeSettingsRequest
            {
                SeedingStopMode = SeedingStopMode.StopAfterRatioOrTime.ToString(),
                SeedingStopRatio = 1.5,
                SeedingStopMinutes = 90,
                CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(),
                CompletedTorrentCleanupMinutes = 15,
                EngineConnectionFailureLogBurstLimit = 2,
                EngineConnectionFailureLogWindowSeconds = 180,
                EngineMaximumConnections = 70,
                EngineMaximumHalfOpenConnections = 6,
                EngineMaximumDownloadRateBytesPerSecond = 4_000_000,
                EngineMaximumUploadRateBytesPerSecond = 1_500_000,
                MaxActiveMetadataResolutions = 3,
                MaxActiveDownloads = 2,
            });
            updateResponse.EnsureSuccessStatusCode();

            var settings = await updateResponse.Content.ReadFromJsonAsync<RuntimeSettingsDto>();
            var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

            Assert.NotNull(settings);
            Assert.True(settings.UsesPersistedOverrides);
            Assert.Equal(SeedingStopMode.StopAfterRatioOrTime.ToString(), settings.SeedingStopMode);
            Assert.Equal(1.5, settings.SeedingStopRatio);
            Assert.Equal(90, settings.SeedingStopMinutes);
            Assert.Equal(CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(), settings.CompletedTorrentCleanupMode);
            Assert.Equal(15, settings.CompletedTorrentCleanupMinutes);
            Assert.Equal(2, settings.EngineConnectionFailureLogBurstLimit);
            Assert.Equal(180, settings.EngineConnectionFailureLogWindowSeconds);
            Assert.Equal(70, settings.EngineMaximumConnections);
            Assert.Equal(6, settings.EngineMaximumHalfOpenConnections);
            Assert.Equal(4_000_000, settings.EngineMaximumDownloadRateBytesPerSecond);
            Assert.Equal(1_500_000, settings.EngineMaximumUploadRateBytesPerSecond);
            Assert.Equal(3, settings.MaxActiveMetadataResolutions);
            Assert.Equal(2, settings.MaxActiveDownloads);
            Assert.True(settings.EngineSettingsRequireRestart);
            Assert.NotNull(settings.UpdatedAtUtc);

            Assert.NotNull(hostStatus);
            Assert.Equal(SeedingStopMode.StopAfterRatioOrTime.ToString(), hostStatus.SeedingStopMode);
            Assert.Equal(1.5, hostStatus.SeedingStopRatio);
            Assert.Equal(90, hostStatus.SeedingStopMinutes);
            Assert.Equal(CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(), hostStatus.CompletedTorrentCleanupMode);
            Assert.Equal(15, hostStatus.CompletedTorrentCleanupMinutes);
            Assert.Equal(2, hostStatus.EngineConnectionFailureLogBurstLimit);
            Assert.Equal(180, hostStatus.EngineConnectionFailureLogWindowSeconds);
            Assert.Equal(150, hostStatus.EngineMaximumConnections);
            Assert.Equal(8, hostStatus.EngineMaximumHalfOpenConnections);
            Assert.Equal(0, hostStatus.EngineMaximumDownloadRateBytesPerSecond);
            Assert.Equal(0, hostStatus.EngineMaximumUploadRateBytesPerSecond);
            Assert.Equal(3, hostStatus.MaxActiveMetadataResolutions);
            Assert.Equal(2, hostStatus.MaxActiveDownloads);
        }

        await using (var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var settings = await httpClient.GetFromJsonAsync<RuntimeSettingsDto>("api/host/runtime-settings");
            var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");
            var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=50&eventType=service.runtime_settings.updated");

            Assert.NotNull(settings);
            Assert.True(settings.UsesPersistedOverrides);
            Assert.Equal(SeedingStopMode.StopAfterRatioOrTime.ToString(), settings.SeedingStopMode);
            Assert.Equal(1.5, settings.SeedingStopRatio);
            Assert.Equal(90, settings.SeedingStopMinutes);
            Assert.Equal(CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(), settings.CompletedTorrentCleanupMode);
            Assert.Equal(15, settings.CompletedTorrentCleanupMinutes);
            Assert.Equal(2, settings.EngineConnectionFailureLogBurstLimit);
            Assert.Equal(180, settings.EngineConnectionFailureLogWindowSeconds);
            Assert.Equal(70, settings.EngineMaximumConnections);
            Assert.Equal(6, settings.EngineMaximumHalfOpenConnections);
            Assert.Equal(4_000_000, settings.EngineMaximumDownloadRateBytesPerSecond);
            Assert.Equal(1_500_000, settings.EngineMaximumUploadRateBytesPerSecond);
            Assert.Equal(3, settings.MaxActiveMetadataResolutions);
            Assert.Equal(2, settings.MaxActiveDownloads);
            Assert.Equal(70, settings.AppliedEngineMaximumConnections);
            Assert.Equal(6, settings.AppliedEngineMaximumHalfOpenConnections);
            Assert.Equal(4_000_000, settings.AppliedEngineMaximumDownloadRateBytesPerSecond);
            Assert.Equal(1_500_000, settings.AppliedEngineMaximumUploadRateBytesPerSecond);
            Assert.False(settings.EngineSettingsRequireRestart);

            Assert.NotNull(hostStatus);
            Assert.Equal(SeedingStopMode.StopAfterRatioOrTime.ToString(), hostStatus.SeedingStopMode);
            Assert.Equal(CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(), hostStatus.CompletedTorrentCleanupMode);
            Assert.Equal(2, hostStatus.EngineConnectionFailureLogBurstLimit);
            Assert.Equal(180, hostStatus.EngineConnectionFailureLogWindowSeconds);
            Assert.Equal(70, hostStatus.EngineMaximumConnections);
            Assert.Equal(6, hostStatus.EngineMaximumHalfOpenConnections);
            Assert.Equal(4_000_000, hostStatus.EngineMaximumDownloadRateBytesPerSecond);
            Assert.Equal(1_500_000, hostStatus.EngineMaximumUploadRateBytesPerSecond);
            Assert.Equal(3, hostStatus.MaxActiveMetadataResolutions);
            Assert.Equal(2, hostStatus.MaxActiveDownloads);

            Assert.NotNull(logs);
            Assert.Contains(logs, log => log.EventType == "service.runtime_settings.updated");
        }
    }

    [Fact]
    public async Task GetTorrents_ReturnsPersistedTorrentAfterAdd()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "Listed Torrent");

        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(torrents);
        Assert.Contains(torrents, torrent => torrent.Name == "Listed Torrent");
    }

    [Fact]
    public async Task AddMagnet_ReturnsCreatedTorrent_ForValidMagnet()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", "API Test Torrent");
        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(torrent);
        Assert.Equal("API Test Torrent", torrent.Name);
        Assert.Equal(TorrentState.ResolvingMetadata, torrent.State);
        Assert.Equal("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", torrent.InfoHash);
        Assert.Equal(0, torrent.TrackerCount);
        Assert.Equal(0, torrent.ConnectedPeerCount);
    }

    [Fact]
    public async Task RemoveTorrent_WithoutRequestBody_DefaultsToRemoveOnly()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "B1B1B1B1B1B1B1B1B1B1B1B1B1B1B1B1B1B1B1B1", "Remove Without Body");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var removeResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/remove", content: null);
        var actionResult = await removeResponse.Content.ReadFromJsonAsync<TorrentActionResultDto>();
        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.NotNull(actionResult);
        Assert.Equal("remove", actionResult.Action);
        Assert.False(actionResult.DataDeleted);
        Assert.NotNull(torrents);
        Assert.DoesNotContain(torrents, torrent => torrent.TorrentId == addedTorrent.TorrentId);
    }

    [Fact]
    public async Task MonoTorrentEngine_AddMagnet_UsesRealEngineRuntime()
    {
        await using var factory = CreateFactory(engineMode: TorrentEngineMode.MonoTorrent);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "9999999999999999999999999999999999999999", "MonoTorrent Runtime");
        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(torrent);
        Assert.Equal("MonoTorrent", hostStatus!.EngineRuntime);
        Assert.Equal(55_123, hostStatus.EngineListenPort);
        Assert.Equal(55_124, hostStatus.EngineDhtPort);
        Assert.True(hostStatus.PartialFilesEnabled);
        Assert.Equal(".!mt", hostStatus.PartialFileSuffix);
        Assert.Equal(SeedingStopMode.Unlimited.ToString(), hostStatus.SeedingStopMode);
        Assert.True(hostStatus.StartupRecoveryCompleted);
        Assert.Equal("9999999999999999999999999999999999999999", torrent.InfoHash);
        Assert.DoesNotContain(torrent.State, new[] { TorrentState.Error, TorrentState.Removed });
    }

    [Fact]
    public async Task MonoTorrentEngine_WritesEngineLifecycleLogs()
    {
        await using var factory = CreateFactory(engineMode: TorrentEngineMode.MonoTorrent);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "8888888888888888888888888888888888888888", "MonoTorrent Logs");
        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var pauseResponse = await httpClient.PostAsync($"api/torrents/{torrent!.TorrentId}/pause", content: null);
        pauseResponse.EnsureSuccessStatusCode();

        var logs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=100&torrentId={torrent.TorrentId}"),
            entries => entries is not null && entries.Any(entry => entry.EventType == "torrent.engine.state_changed"),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.Category == "engine" && log.EventType == "torrent.engine.state_changed");
    }

    [Fact]
    public async Task MonoTorrentEngine_LogsEngineReadyAndThrottlesConnectionFailures()
    {
        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            engineConnectionFailureLogBurstLimit: 1,
            engineConnectionFailureLogWindowSeconds: 300);
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "7777777777777777777777777777777777777777", "Throttle Torrent");

        var logs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=200&category=engine"),
            entries => entries is not null && entries.Any(entry => entry.EventType == "engine.monotorrent.ready"),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "engine.monotorrent.ready");
    }

    [Fact]
    public async Task MonoTorrentEngine_RemoveActiveTorrent_StopsThenRemoves()
    {
        await using var factory = CreateFactory(engineMode: TorrentEngineMode.MonoTorrent);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "6767676767676767676767676767676767676767", "MonoTorrent Remove");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var removeResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/remove", content: null);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var actionResult = await removeResponse.Content.ReadFromJsonAsync<TorrentActionResultDto>();
        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(actionResult);
        Assert.Equal("remove", actionResult.Action);
        Assert.False(actionResult.DataDeleted);
        Assert.NotNull(torrents);
        Assert.DoesNotContain(torrents, torrent => torrent.TorrentId == addedTorrent.TorrentId);
    }

    [Fact]
    public async Task MonoTorrentEngine_ResumePausedTorrent_LeavesPausedState()
    {
        await using var factory = CreateFactory(engineMode: TorrentEngineMode.MonoTorrent);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "6868686868686868686868686868686868686868", "MonoTorrent Resume");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var pauseResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/pause", content: null);
        pauseResponse.EnsureSuccessStatusCode();

        var resumeResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent.TorrentId}/resume", content: null);
        resumeResponse.EnsureSuccessStatusCode();

        var resumedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.State != TorrentState.Paused,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(resumedTorrent);
        Assert.NotEqual(TorrentState.Paused, resumedTorrent.State);
    }

    [Fact]
    public async Task FakeRuntime_EventuallyResolvesMetadata_AndCompletesDownload()
    {
        await using var factory = CreateFactory(
            seedingStopMode: SeedingStopMode.StopImmediately,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "ABABABABABABABABABABABABABABABABABABABAB", "Runtime Torrent");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var completedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Completed,
            timeout: TimeSpan.FromSeconds(5));

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=50&torrentId=" + addedTorrent!.TorrentId);

        Assert.NotNull(completedTorrent);
        Assert.True(completedTorrent.TotalBytes > 0);
        Assert.Equal(completedTorrent.TotalBytes, completedTorrent.DownloadedBytes);
        Assert.Equal(100, completedTorrent.ProgressPercent);
        Assert.True(completedTorrent.TrackerCount > 0);
        Assert.NotNull(completedTorrent.CompletedAtUtc);

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.metadata.resolved");
        Assert.Contains(logs, log => log.EventType == "torrent.download.started");
        Assert.Contains(logs, log => log.EventType == "torrent.download.completed");
        Assert.Contains(logs, log => log.EventType == "torrent.seeding.stopped_policy");
    }

    [Fact]
    public async Task FakeRuntime_AutoCleanup_RemovesCompletedTorrentWithoutDeletingData()
    {
        await using var factory = CreateFactory(
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        var updateResponse = await httpClient.PutAsJsonAsync("api/host/runtime-settings", new UpdateRuntimeSettingsRequest
        {
            SeedingStopMode = SeedingStopMode.StopImmediately.ToString(),
            SeedingStopRatio = 1.0,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(),
            CompletedTorrentCleanupMinutes = 0,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
        });
        updateResponse.EnsureSuccessStatusCode();

        var response = await AddMagnetAsync(httpClient, "CDCDCDCDCDCDCDCDCDCDCDCDCDCDCDCDCDCDCDCD", "Auto Cleanup Torrent");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var remainingTorrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            torrents => torrents is not null && torrents.All(torrent => torrent.TorrentId != addedTorrent!.TorrentId),
            timeout: TimeSpan.FromSeconds(5));

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=100");

        Assert.NotNull(remainingTorrents);
        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.cleanup.auto_removed" && log.TorrentId == addedTorrent!.TorrentId);
    }

    [Fact]
    public async Task FakeRuntime_UsesSingleActiveDownloadQueue_ByDefault()
    {
        await using var factory = CreateFactory(
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 5,
            maxActiveDownloads: 1);
        using var httpClient = factory.CreateClient();

        var firstResponse = await AddMagnetAsync(httpClient, "1010101010101010101010101010101010101010", "Queue One");
        var firstTorrent = await firstResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var secondResponse = await AddMagnetAsync(httpClient, "2020202020202020202020202020202020202020", "Queue Two");
        var secondTorrent = await secondResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var queuedAndActive = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            torrents => torrents is not null &&
                        torrents.Count == 2 &&
                        torrents.Count(torrent => torrent.State == TorrentState.Downloading) == 1 &&
                        torrents.Count(torrent => torrent.State == TorrentState.Queued) == 1,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(queuedAndActive);
        Assert.Contains(queuedAndActive, torrent => torrent.TorrentId == firstTorrent!.TorrentId || torrent.TorrentId == secondTorrent!.TorrentId);
        Assert.Contains(queuedAndActive, torrent => torrent.State == TorrentState.Queued);
        Assert.Contains(queuedAndActive, torrent => torrent.State == TorrentState.Downloading);
    }

    [Fact]
    public async Task FakeRuntime_QueuesMetadataResolution_WhenCapacityIsFull()
    {
        await using var factory = CreateFactory(
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 5_000,
            maxActiveMetadataResolutions: 1);
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "3030303030303030303030303030303030303030", "Resolve One");
        await AddMagnetAsync(httpClient, "4040404040404040404040404040404040404040", "Resolve Two");

        var torrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            items => items is not null &&
                     items.Count == 2 &&
                     items.Count(torrent => torrent.State == TorrentState.ResolvingMetadata) == 1 &&
                     items.Count(torrent => torrent.State == TorrentState.Queued) == 1,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(torrents);
        Assert.Contains(torrents, torrent => torrent.State == TorrentState.ResolvingMetadata);
        Assert.Contains(torrents, torrent => torrent.State == TorrentState.Queued);
    }

    [Fact]
    public async Task TorrentState_SurvivesRestart()
    {
        var rootPath = CreateTempRootPath("torrentcore-phase2-restart");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        Guid torrentId;

        await using (var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();
            var addResponse = await AddMagnetAsync(httpClient, "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", "Restarted Torrent");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;

            var pauseResponse = await httpClient.PostAsync($"api/torrents/{torrentId}/pause", content: null);
            pauseResponse.EnsureSuccessStatusCode();
        }

        await using (var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var torrent = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}");
            var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

            Assert.NotNull(torrent);
            Assert.Equal(TorrentState.Paused, torrent.State);
            Assert.Contains(torrents!, summary => summary.TorrentId == torrentId && summary.State == TorrentState.Paused);
        }
    }

    [Fact]
    public async Task StartupRecovery_NormalizesActiveTorrentState_AfterRestart()
    {
        var rootPath = CreateTempRootPath("torrentcore-phase2-recovery");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        Guid torrentId;

        await using (var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();
            var addResponse = await AddMagnetAsync(httpClient, "1212121212121212121212121212121212121212", "Recovery Torrent");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;
        }

        await using (var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var recoveredTorrent = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}");
            var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");
            var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=50");

            Assert.NotNull(recoveredTorrent);
            Assert.DoesNotContain(recoveredTorrent.State, new[] { TorrentState.Error, TorrentState.Removed });

            Assert.NotNull(hostStatus);
            Assert.Equal(1, hostStatus.StartupRecoveredTorrentCount);
            Assert.Equal(1, hostStatus.StartupNormalizedTorrentCount);
            Assert.NotNull(hostStatus.StartupRecoveryCompletedAtUtc);

            Assert.NotNull(logs);
            Assert.Contains(logs, log => log.EventType == "service.recovery.completed" && log.ServiceInstanceId == hostStatus.ServiceInstanceId);
            Assert.Contains(logs, log => log.EventType == "torrent.recovery.normalized" && log.TorrentId == torrentId);
            Assert.Contains(logs, log => log.EventType == "service.startup.ready" && log.ServiceInstanceId == hostStatus.ServiceInstanceId);
        }
    }

    [Fact]
    public async Task GetLogs_ReturnsStartupAndTorrentEvents()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD", "Logged Torrent");

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=20");

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "service.startup.ready");
        Assert.Contains(logs, log => log.EventType == "torrent.added");
        Assert.Contains(logs, log => log.ServiceInstanceId is not null);
    }

    [Fact]
    public async Task GetLogs_FiltersByCategory_AndEventType()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE", "Filtered Torrent");

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=20&category=torrent&eventType=torrent.added");

        Assert.NotNull(logs);
        Assert.NotEmpty(logs);
        Assert.All(logs, log =>
        {
            Assert.Equal("torrent", log.Category);
            Assert.Equal("torrent.added", log.EventType);
        });
    }

    [Fact]
    public async Task GetLogs_RetentionEnforcesConfiguredMaximum()
    {
        var rootPath = CreateTempRootPath("torrentcore-logs-retention");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath, maxActivityLogEntries: 100);
        using var httpClient = factory.CreateClient();

        for (var index = 0; index < 130; index++)
        {
            var hash = index.ToString("D40");
            var response = await AddMagnetAsync(httpClient, hash, $"Retention {index}");
            response.EnsureSuccessStatusCode();
        }

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=500");

        Assert.NotNull(logs);
        Assert.True(logs.Count <= 100);
    }

    [Fact]
    public async Task AddMagnet_ReturnsBadRequest_ForInvalidMagnet()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = "https://example.com/not-a-magnet",
        });

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("invalid_magnet", error.GetProperty("code").GetString());
        Assert.Equal("MagnetUri must be a valid magnet URI.", error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AddMagnet_ReturnsConflict_ForPersistedDuplicate()
    {
        var rootPath = CreateTempRootPath("torrentcore-duplicate");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using (var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();
            var firstResponse = await AddMagnetAsync(httpClient, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "First Torrent");
            firstResponse.EnsureSuccessStatusCode();
        }

        await using (var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();
            var duplicateResponse = await AddMagnetAsync(httpClient, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "Duplicate Torrent");
            var error = await duplicateResponse.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
            Assert.Equal("duplicate_magnet", error.GetProperty("code").GetString());
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        TorrentEngineMode engineMode = TorrentEngineMode.Fake,
        string? downloadPath = null,
        string? storagePath = null,
        int? maxActivityLogEntries = null,
        int? engineListenPort = null,
        int? engineDhtPort = null,
        bool? engineAllowPortForwarding = null,
        bool? engineAllowLocalPeerDiscovery = null,
        int? engineMaximumConnections = null,
        int? engineMaximumHalfOpenConnections = null,
        int? engineMaximumDownloadRateBytesPerSecond = null,
        int? engineMaximumUploadRateBytesPerSecond = null,
        int? engineConnectionFailureLogBurstLimit = null,
        int? engineConnectionFailureLogWindowSeconds = null,
        bool? usePartialFiles = null,
        SeedingStopMode? seedingStopMode = null,
        double? seedingStopRatio = null,
        int? seedingStopMinutes = null,
        int? maxActiveMetadataResolutions = null,
        int? maxActiveDownloads = null,
        int? runtimeTickIntervalMilliseconds = null,
        int? metadataResolutionDelayMilliseconds = null,
        double? downloadProgressPercentPerTick = null)
    {
        var rootPath = CreateTempRootPath("torrentcore-api");
        var resolvedDownloadPath = downloadPath ?? Path.Combine(rootPath, "downloads");
        var resolvedStoragePath = storagePath ?? Path.Combine(rootPath, "storage");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        [$"{TorrentCoreServiceOptions.SectionName}:EngineMode"] = engineMode.ToString(),
                        [$"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath"] = resolvedDownloadPath,
                        [$"{TorrentCoreServiceOptions.SectionName}:StorageRootPath"] = resolvedStoragePath,
                    };

                    if (maxActivityLogEntries is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MaxActivityLogEntries"] = maxActivityLogEntries.Value.ToString();
                    }

                    if (engineListenPort is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineListenPort"] = engineListenPort.Value.ToString();
                    }

                    if (engineDhtPort is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineDhtPort"] = engineDhtPort.Value.ToString();
                    }

                    if (engineAllowPortForwarding is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineAllowPortForwarding"] = engineAllowPortForwarding.Value.ToString();
                    }

                    if (engineAllowLocalPeerDiscovery is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineAllowLocalPeerDiscovery"] = engineAllowLocalPeerDiscovery.Value.ToString();
                    }

                    if (engineMaximumConnections is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineMaximumConnections"] = engineMaximumConnections.Value.ToString();
                    }

                    if (engineMaximumHalfOpenConnections is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineMaximumHalfOpenConnections"] = engineMaximumHalfOpenConnections.Value.ToString();
                    }

                    if (engineMaximumDownloadRateBytesPerSecond is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineMaximumDownloadRateBytesPerSecond"] = engineMaximumDownloadRateBytesPerSecond.Value.ToString();
                    }

                    if (engineMaximumUploadRateBytesPerSecond is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineMaximumUploadRateBytesPerSecond"] = engineMaximumUploadRateBytesPerSecond.Value.ToString();
                    }

                    if (engineConnectionFailureLogBurstLimit is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineConnectionFailureLogBurstLimit"] = engineConnectionFailureLogBurstLimit.Value.ToString();
                    }

                    if (engineConnectionFailureLogWindowSeconds is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:EngineConnectionFailureLogWindowSeconds"] = engineConnectionFailureLogWindowSeconds.Value.ToString();
                    }

                    if (usePartialFiles is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:UsePartialFiles"] = usePartialFiles.Value.ToString();
                    }

                    if (seedingStopMode is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:SeedingStopMode"] = seedingStopMode.Value.ToString();
                    }

                    if (seedingStopRatio is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:SeedingStopRatio"] = seedingStopRatio.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    if (seedingStopMinutes is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:SeedingStopMinutes"] = seedingStopMinutes.Value.ToString();
                    }

                    if (maxActiveMetadataResolutions is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MaxActiveMetadataResolutions"] = maxActiveMetadataResolutions.Value.ToString();
                    }

                    if (maxActiveDownloads is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MaxActiveDownloads"] = maxActiveDownloads.Value.ToString();
                    }

                    if (runtimeTickIntervalMilliseconds is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:RuntimeTickIntervalMilliseconds"] = runtimeTickIntervalMilliseconds.Value.ToString();
                    }

                    if (metadataResolutionDelayMilliseconds is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MetadataResolutionDelayMilliseconds"] = metadataResolutionDelayMilliseconds.Value.ToString();
                    }

                    if (downloadProgressPercentPerTick is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:DownloadProgressPercentPerTick"] = downloadProgressPercentPerTick.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    configurationBuilder.AddInMemoryCollection(settings);
                });
            });
    }

    private static async Task<HttpResponseMessage> AddMagnetAsync(HttpClient httpClient, string infoHash, string name)
    {
        return await httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(name)}",
        });
    }

    private static string CreateTempRootPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
    }

    private static async Task<T> WaitForAsync<T>(
        Func<Task<T>> action,
        Func<T, bool> predicate,
        TimeSpan timeout,
        int pollIntervalMilliseconds = 50)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            var result = await action();
            if (predicate(result))
            {
                return result;
            }

            await Task.Delay(pollIntervalMilliseconds);
        }

        var finalResult = await action();
        Assert.True(predicate(finalResult), "Timed out waiting for the expected condition.");
        return finalResult;
    }
}
