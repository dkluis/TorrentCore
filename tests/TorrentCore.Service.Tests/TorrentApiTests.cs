using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
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
        Assert.Equal(4, hostStatus.AvailableMetadataResolutionSlots);
        Assert.Equal(4, hostStatus.AvailableDownloadSlots);
        Assert.Equal(0, hostStatus.ResolvingMetadataCount);
        Assert.Equal(0, hostStatus.MetadataQueueCount);
        Assert.Equal(0, hostStatus.DownloadingCount);
        Assert.Equal(0, hostStatus.DownloadQueueCount);
        Assert.Equal(0, hostStatus.SeedingCount);
        Assert.Equal(0, hostStatus.PausedCount);
        Assert.Equal(0, hostStatus.CompletedCount);
        Assert.Equal(0, hostStatus.ErrorCount);
        Assert.Equal(0, hostStatus.CurrentConnectedPeerCount);
        Assert.Equal(0, hostStatus.CurrentDownloadRateBytesPerSecond);
        Assert.Equal(0, hostStatus.CurrentUploadRateBytesPerSecond);
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
        Assert.False(settings.CompletionCallbackEnabled);
        Assert.Null(settings.CompletionCallbackCommandPath);
        Assert.Null(settings.CompletionCallbackArguments);
        Assert.Null(settings.CompletionCallbackWorkingDirectory);
        Assert.Equal(30, settings.CompletionCallbackTimeoutSeconds);
        Assert.Equal(120, settings.CompletionCallbackFinalizationTimeoutSeconds);
        Assert.Null(settings.CompletionCallbackApiBaseUrlOverride);
        Assert.Null(settings.CompletionCallbackApiKeyOverride);
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
                CompletionCallbackEnabled = true,
                CompletionCallbackCommandPath = "/usr/local/bin/torrentcore-callback",
                CompletionCallbackArguments = "--run",
                CompletionCallbackWorkingDirectory = "/Users/dick/TorrentCore/Scripts",
                CompletionCallbackTimeoutSeconds = 45,
                CompletionCallbackFinalizationTimeoutSeconds = 180,
                CompletionCallbackApiBaseUrlOverride = "http://127.0.0.1:5501/api/complete",
                CompletionCallbackApiKeyOverride = "integration-key",
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
            Assert.True(settings.CompletionCallbackEnabled);
            Assert.Equal("/usr/local/bin/torrentcore-callback", settings.CompletionCallbackCommandPath);
            Assert.Equal("--run", settings.CompletionCallbackArguments);
            Assert.Equal("/Users/dick/TorrentCore/Scripts", settings.CompletionCallbackWorkingDirectory);
            Assert.Equal(45, settings.CompletionCallbackTimeoutSeconds);
            Assert.Equal(180, settings.CompletionCallbackFinalizationTimeoutSeconds);
            Assert.Equal("http://127.0.0.1:5501/api/complete", settings.CompletionCallbackApiBaseUrlOverride);
            Assert.Equal("integration-key", settings.CompletionCallbackApiKeyOverride);
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
            Assert.True(settings.CompletionCallbackEnabled);
            Assert.Equal("/usr/local/bin/torrentcore-callback", settings.CompletionCallbackCommandPath);
            Assert.Equal("--run", settings.CompletionCallbackArguments);
            Assert.Equal("/Users/dick/TorrentCore/Scripts", settings.CompletionCallbackWorkingDirectory);
            Assert.Equal(45, settings.CompletionCallbackTimeoutSeconds);
            Assert.Equal(180, settings.CompletionCallbackFinalizationTimeoutSeconds);
            Assert.Equal("http://127.0.0.1:5501/api/complete", settings.CompletionCallbackApiBaseUrlOverride);
            Assert.Equal("integration-key", settings.CompletionCallbackApiKeyOverride);
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
    public async Task GetCategories_ReturnsSeededDefaults()
    {
        var rootPath = CreateTempRootPath("torrentcore-category-defaults");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var categories = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentCategoryDto>>("api/categories");

        Assert.NotNull(categories);
        Assert.Equal(["TV", "Movie", "Audiobook", "Music"], categories.Select(category => category.Key).ToArray());
        Assert.All(categories, category =>
        {
            Assert.True(category.Enabled);
            Assert.True(category.InvokeCompletionCallback);
            Assert.Equal(Path.Combine(downloadPath, category.Key), category.DownloadRootPath);
        });
    }

    [Fact]
    public async Task UpdateCategory_ChangesFutureRoutingWithoutChangingCategoryKey()
    {
        var rootPath = CreateTempRootPath("torrentcore-category-update");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var updatedMoviePath = Path.Combine(rootPath, "media", "movies");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var updateResponse = await httpClient.PutAsJsonAsync("api/categories/Movie", new UpdateTorrentCategoryRequest
        {
            DisplayName = "Movies",
            CallbackLabel = "Movie",
            DownloadRootPath = updatedMoviePath,
            Enabled = true,
            InvokeCompletionCallback = true,
            SortOrder = 12,
        });
        updateResponse.EnsureSuccessStatusCode();

        var updatedCategory = await updateResponse.Content.ReadFromJsonAsync<TorrentCategoryDto>();
        var addResponse = await AddMagnetAsync(httpClient, "C1C1C1C1C1C1C1C1C1C1C1C1C1C1C1C1C1C1C1C1", "Updated Category Torrent", "Movie");
        var torrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        Assert.NotNull(updatedCategory);
        Assert.Equal("Movie", updatedCategory.Key);
        Assert.Equal("Movies", updatedCategory.DisplayName);
        Assert.Equal(Path.GetFullPath(updatedMoviePath), updatedCategory.DownloadRootPath);
        Assert.Equal(12, updatedCategory.SortOrder);

        Assert.NotNull(torrent);
        Assert.Equal("Movie", torrent.CategoryKey);
        Assert.StartsWith(Path.GetFullPath(updatedMoviePath), torrent.SavePath, StringComparison.Ordinal);
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
        Assert.Null(torrent.CategoryKey);
        Assert.Equal(TorrentState.ResolvingMetadata, torrent.State);
        Assert.Equal("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", torrent.InfoHash);
        Assert.Equal(0, torrent.TrackerCount);
        Assert.Equal(0, torrent.ConnectedPeerCount);
    }

    [Fact]
    public async Task AddMagnet_WithCategory_UsesCategoryDownloadRoot_AndPersistsCategoryKey()
    {
        var rootPath = CreateTempRootPath("torrentcore-category-add");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "BCBCBCBCBCBCBCBCBCBCBCBCBCBCBCBCBCBCBCBC", "Categorized Torrent", "Movie");
        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(torrent);
        Assert.Equal("Movie", torrent.CategoryKey);
        Assert.StartsWith(Path.Combine(downloadPath, "Movie"), torrent.SavePath, StringComparison.Ordinal);

        Assert.NotNull(torrents);
        Assert.Contains(
            torrents,
            item => item.TorrentId == torrent.TorrentId &&
                    item.CategoryKey == "Movie");
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
    public async Task MonoTorrentEngine_PauseWhileResolvingMetadata_RemainsPausedInDetailAndList()
    {
        await using var factory = CreateFactory(engineMode: TorrentEngineMode.MonoTorrent);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "6969696969696969696969696969696969696969", "MonoTorrent Pause Metadata");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var pauseResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/pause", content: null);
        pauseResponse.EnsureSuccessStatusCode();

        var pausedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Paused,
            timeout: TimeSpan.FromSeconds(5));

        var torrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            items => items is not null && items.Any(torrent => torrent.TorrentId == addedTorrent.TorrentId && torrent.State == TorrentState.Paused),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(pausedTorrent);
        Assert.Equal(TorrentState.Paused, pausedTorrent.State);
        Assert.NotNull(torrents);
        Assert.Contains(torrents, torrent => torrent.TorrentId == addedTorrent.TorrentId && torrent.State == TorrentState.Paused);

        await Task.Delay(750);

        var pausedTorrentAfterDelay = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}");
        var torrentsAfterDelay = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(pausedTorrentAfterDelay);
        Assert.Equal(TorrentState.Paused, pausedTorrentAfterDelay.State);
        Assert.NotNull(torrentsAfterDelay);
        Assert.Contains(torrentsAfterDelay, torrent => torrent.TorrentId == addedTorrent.TorrentId && torrent.State == TorrentState.Paused);
    }

    [Fact]
    public async Task MonoTorrentEngine_RepeatedReads_DoNotChangePausedTorrentState()
    {
        await using var factory = CreateFactory(engineMode: TorrentEngineMode.MonoTorrent);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "6A6A6A6A6A6A6A6A6A6A6A6A6A6A6A6A6A6A6A6A", "MonoTorrent Read Stability");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var pauseResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/pause", content: null);
        pauseResponse.EnsureSuccessStatusCode();

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Paused,
            timeout: TimeSpan.FromSeconds(5));

        for (var index = 0; index < 5; index++)
        {
            var detail = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}");
            var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

            Assert.NotNull(detail);
            Assert.Equal(TorrentState.Paused, detail.State);
            Assert.Equal(TorrentWaitReason.PausedByOperator, detail.WaitReason);

            Assert.NotNull(torrents);
            Assert.Contains(
                torrents,
                torrent => torrent.TorrentId == addedTorrent.TorrentId &&
                           torrent.State == TorrentState.Paused &&
                           torrent.WaitReason == TorrentWaitReason.PausedByOperator);

            await Task.Delay(100);
        }

        await Task.Delay(500);

        var finalDetail = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}");
        Assert.NotNull(finalDetail);
        Assert.Equal(TorrentState.Paused, finalDetail.State);
        Assert.Equal(TorrentWaitReason.PausedByOperator, finalDetail.WaitReason);
    }

    [Fact]
    public async Task MonoTorrentEngine_GetEndpoints_ProjectLiveStateOverStalePersistedSnapshot()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-live-projection");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            downloadPath: downloadPath,
            storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A", "MonoTorrent Live Projection");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var initialDetail = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State is not TorrentState.Error and not TorrentState.Removed,
            timeout: TimeSpan.FromSeconds(5));

        await ForcePersistedTorrentSnapshotAsync(
            storagePath,
            addedTorrent!.TorrentId,
            TorrentState.Error,
            TorrentDesiredState.Runnable,
            errorMessage: "stale persisted error");

        var projectedDetail = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null &&
                       torrent.State is not TorrentState.Error and not TorrentState.Removed &&
                       torrent.ErrorMessage is null,
            timeout: TimeSpan.FromSeconds(5));

        var projectedList = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(initialDetail);
        Assert.NotNull(projectedDetail);
        Assert.NotNull(projectedList);
        Assert.DoesNotContain(projectedDetail.State, new[] { TorrentState.Error, TorrentState.Removed });
        Assert.Null(projectedDetail.ErrorMessage);
        Assert.Contains(
            projectedList,
            torrent => torrent.TorrentId == addedTorrent.TorrentId &&
                       torrent.State == projectedDetail.State &&
                       torrent.ErrorMessage is null);
    }

    [Fact]
    public async Task MonoTorrentEngine_ResumePausedQueuedTorrent_WaitsForMetadataSlot_WhenCapacityIsFull()
    {
        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            maxActiveMetadataResolutions: 1);
        using var httpClient = factory.CreateClient();

        var firstResponse = await AddMagnetAsync(httpClient, "6B6B6B6B6B6B6B6B6B6B6B6B6B6B6B6B6B6B6B6B", "MonoTorrent Slot One");
        var firstTorrent = await firstResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var secondResponse = await AddMagnetAsync(httpClient, "6C6C6C6C6C6C6C6C6C6C6C6C6C6C6C6C6C6C6C6C", "MonoTorrent Slot Two");
        var secondTorrent = await secondResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var queuedSecond = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            torrents => torrents is not null &&
                        torrents.Any(torrent => torrent.TorrentId == firstTorrent!.TorrentId && torrent.State == TorrentState.ResolvingMetadata) &&
                        torrents.Any(torrent => torrent.TorrentId == secondTorrent!.TorrentId &&
                                               torrent.State == TorrentState.Queued &&
                                               torrent.WaitReason == TorrentWaitReason.WaitingForMetadataSlot &&
                                               torrent.QueuePosition == 1),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(queuedSecond);

        var pauseResponse = await httpClient.PostAsync($"api/torrents/{secondTorrent!.TorrentId}/pause", content: null);
        pauseResponse.EnsureSuccessStatusCode();

        var pausedSecond = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{secondTorrent.TorrentId}"),
            torrent => torrent is not null &&
                       torrent.State == TorrentState.Paused &&
                       torrent.WaitReason == TorrentWaitReason.PausedByOperator,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(pausedSecond);

        var resumeResponse = await httpClient.PostAsync($"api/torrents/{secondTorrent.TorrentId}/resume", content: null);
        resumeResponse.EnsureSuccessStatusCode();

        var resumedSecond = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{secondTorrent.TorrentId}"),
            torrent => torrent is not null &&
                       torrent.State == TorrentState.Queued &&
                       torrent.WaitReason == TorrentWaitReason.WaitingForMetadataSlot &&
                       torrent.QueuePosition == 1,
            timeout: TimeSpan.FromSeconds(5));

        var torrentsAfterResume = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(resumedSecond);
        Assert.Equal(TorrentState.Queued, resumedSecond.State);
        Assert.Equal(TorrentWaitReason.WaitingForMetadataSlot, resumedSecond.WaitReason);
        Assert.Equal(1, resumedSecond.QueuePosition);

        Assert.NotNull(torrentsAfterResume);
        Assert.Contains(
            torrentsAfterResume,
            torrent => torrent.TorrentId == firstTorrent!.TorrentId && torrent.State == TorrentState.ResolvingMetadata);
        Assert.Contains(
            torrentsAfterResume,
            torrent => torrent.TorrentId == secondTorrent.TorrentId &&
                       torrent.State == TorrentState.Queued &&
                       torrent.WaitReason == TorrentWaitReason.WaitingForMetadataSlot &&
                       torrent.QueuePosition == 1);
    }

    [Fact]
    public async Task MonoTorrentEngine_MultiplePausedQueuedTorrents_ReenterMetadataQueueInOrder_OnResume()
    {
        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            maxActiveMetadataResolutions: 1);
        using var httpClient = factory.CreateClient();

        var firstResponse = await AddMagnetAsync(httpClient, "6D6D6D6D6D6D6D6D6D6D6D6D6D6D6D6D6D6D6D6D", "MonoTorrent Multi One");
        var firstTorrent = await firstResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var secondResponse = await AddMagnetAsync(httpClient, "6E6E6E6E6E6E6E6E6E6E6E6E6E6E6E6E6E6E6E6E", "MonoTorrent Multi Two");
        var secondTorrent = await secondResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var thirdResponse = await AddMagnetAsync(httpClient, "6F6F6F6F6F6F6F6F6F6F6F6F6F6F6F6F6F6F6F6F", "MonoTorrent Multi Three");
        var thirdTorrent = await thirdResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            torrents => torrents is not null &&
                        torrents.Any(torrent => torrent.TorrentId == firstTorrent!.TorrentId && torrent.State == TorrentState.ResolvingMetadata) &&
                        torrents.Any(torrent => torrent.TorrentId == secondTorrent!.TorrentId &&
                                               torrent.State == TorrentState.Queued &&
                                               torrent.WaitReason == TorrentWaitReason.WaitingForMetadataSlot &&
                                               torrent.QueuePosition == 1) &&
                        torrents.Any(torrent => torrent.TorrentId == thirdTorrent!.TorrentId &&
                                               torrent.State == TorrentState.Queued &&
                                               torrent.WaitReason == TorrentWaitReason.WaitingForMetadataSlot &&
                                               torrent.QueuePosition == 2),
            timeout: TimeSpan.FromSeconds(5));

        (await httpClient.PostAsync($"api/torrents/{secondTorrent!.TorrentId}/pause", content: null)).EnsureSuccessStatusCode();
        (await httpClient.PostAsync($"api/torrents/{thirdTorrent!.TorrentId}/pause", content: null)).EnsureSuccessStatusCode();

        var pausedTorrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            torrents => torrents is not null &&
                        torrents.Any(torrent => torrent.TorrentId == firstTorrent!.TorrentId && torrent.State == TorrentState.ResolvingMetadata) &&
                        torrents.Any(torrent => torrent.TorrentId == secondTorrent.TorrentId &&
                                               torrent.State == TorrentState.Paused &&
                                               torrent.WaitReason == TorrentWaitReason.PausedByOperator) &&
                        torrents.Any(torrent => torrent.TorrentId == thirdTorrent.TorrentId &&
                                               torrent.State == TorrentState.Paused &&
                                               torrent.WaitReason == TorrentWaitReason.PausedByOperator),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(pausedTorrents);

        (await httpClient.PostAsync($"api/torrents/{secondTorrent.TorrentId}/resume", content: null)).EnsureSuccessStatusCode();
        (await httpClient.PostAsync($"api/torrents/{thirdTorrent.TorrentId}/resume", content: null)).EnsureSuccessStatusCode();

        var resumedTorrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            torrents => torrents is not null &&
                        torrents.Any(torrent => torrent.TorrentId == firstTorrent!.TorrentId && torrent.State == TorrentState.ResolvingMetadata) &&
                        torrents.Any(torrent => torrent.TorrentId == secondTorrent.TorrentId &&
                                               torrent.State == TorrentState.Queued &&
                                               torrent.WaitReason == TorrentWaitReason.WaitingForMetadataSlot &&
                                               torrent.QueuePosition == 1) &&
                        torrents.Any(torrent => torrent.TorrentId == thirdTorrent.TorrentId &&
                                               torrent.State == TorrentState.Queued &&
                                               torrent.WaitReason == TorrentWaitReason.WaitingForMetadataSlot &&
                                               torrent.QueuePosition == 2),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(resumedTorrents);
    }

    [Fact]
    public async Task MonoTorrentEngine_PausedTorrent_StaysPausedAcrossRestart()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-pause-restart");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        Guid torrentId;

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var addResponse = await AddMagnetAsync(httpClient, "7070707070707070707070707070707070707070", "MonoTorrent Restart Pause");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;

            var pauseResponse = await httpClient.PostAsync($"api/torrents/{torrentId}/pause", content: null);
            pauseResponse.EnsureSuccessStatusCode();

            await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.State == TorrentState.Paused,
                timeout: TimeSpan.FromSeconds(5));
        }

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var pausedTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null &&
                           torrent.State == TorrentState.Paused &&
                           torrent.WaitReason == TorrentWaitReason.PausedByOperator,
                timeout: TimeSpan.FromSeconds(5));

            var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

            Assert.NotNull(pausedTorrent);
            Assert.Equal(TorrentState.Paused, pausedTorrent.State);
            Assert.Equal(TorrentWaitReason.PausedByOperator, pausedTorrent.WaitReason);

            Assert.NotNull(torrents);
            Assert.Contains(
                torrents,
                torrent => torrent.TorrentId == torrentId &&
                           torrent.State == TorrentState.Paused &&
                           torrent.WaitReason == TorrentWaitReason.PausedByOperator);

            await Task.Delay(750);

            var pausedTorrentAfterDelay = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}");

            Assert.NotNull(pausedTorrentAfterDelay);
            Assert.Equal(TorrentState.Paused, pausedTorrentAfterDelay.State);
            Assert.Equal(TorrentWaitReason.PausedByOperator, pausedTorrentAfterDelay.WaitReason);
        }
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
    public async Task FakeRuntime_InvokesCompletionCallback_WithTransmissionCompatibleEnvironment()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-env");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(
            httpClient,
            "/bin/sh",
            callbackScriptPath,
            rootPath,
            "http://127.0.0.1:5501/api/transmission/completions",
            "callback-test-key");

        var response = await AddMagnetAsync(httpClient, "7373737373737373737373737373737373737373", "Callback Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        CreateSingleFilePayload(Path.Combine(downloadPath, "Movie", "Callback Movie"));

        var completedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Completed,
            timeout: TimeSpan.FromSeconds(5));

        var callbackInvocations = await WaitForAsync(
            () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
            invocations => invocations.Count == 1,
            timeout: TimeSpan.FromSeconds(5));

        var callbackInvocation = Assert.Single(callbackInvocations);
        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=50&torrentId={addedTorrent!.TorrentId}");

        Assert.NotNull(completedTorrent);
        Assert.Equal("0", callbackInvocation["TR_TORRENT_ID"]);
        Assert.Equal("7373737373737373737373737373737373737373", callbackInvocation["TR_TORRENT_HASH"]);
        Assert.Equal("Callback Movie", callbackInvocation["TR_TORRENT_NAME"]);
        Assert.Equal(Path.Combine(downloadPath, "Movie"), callbackInvocation["TR_TORRENT_DIR"]);
        Assert.Equal("Movie", callbackInvocation["TR_TORRENT_LABELS"]);
        Assert.Equal("http://127.0.0.1:5501/api/transmission/completions", callbackInvocation["TVMAZE_API_COMPLETE_URL"]);
        Assert.Equal("callback-test-key", callbackInvocation["TVMAZE_API_COMPLETE_API_KEY"]);

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.callback.invoked");
    }

    [Fact]
    public async Task FakeRuntime_CompletionCallback_IsNotInvokedAgain_AfterRestart()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-restart");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         runtimeTickIntervalMilliseconds: 50,
                         metadataResolutionDelayMilliseconds: 0,
                         downloadProgressPercentPerTick: 50))
        {
            using var httpClient = factory.CreateClient();

            await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

            var response = await AddMagnetAsync(httpClient, "7474747474747474747474747474747474747474", "Callback TV", "TV");
            var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
            CreateSingleFilePayload(Path.Combine(downloadPath, "TV", "Callback TV"));

            await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
                torrent => torrent is not null && torrent.State == TorrentState.Completed,
                timeout: TimeSpan.FromSeconds(5));

            await WaitForAsync(
                () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
                invocations => invocations.Count == 1,
                timeout: TimeSpan.FromSeconds(5));
        }

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         runtimeTickIntervalMilliseconds: 50,
                         metadataResolutionDelayMilliseconds: 0,
                         downloadProgressPercentPerTick: 50))
        {
            using var httpClient = factory.CreateClient();

            var hostStatusResponse = await httpClient.GetAsync("api/host/status");
            hostStatusResponse.EnsureSuccessStatusCode();

            await Task.Delay(500);

            var callbackInvocations = ReadCallbackInvocations(callbackOutputPath);
            Assert.Single(callbackInvocations);
        }
    }

    [Fact]
    public async Task FakeRuntime_CompletionCallback_WaitsForSingleFileFinalizationVisibility()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-single-finalization");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);
        var finalPayloadPath = Path.Combine(downloadPath, "Movie", "Finalization Movie");
        var partialPayloadPath = finalPayloadPath + ".!mt";

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

        Directory.CreateDirectory(Path.GetDirectoryName(finalPayloadPath)!);
        File.WriteAllText(finalPayloadPath, "final");
        File.WriteAllText(partialPayloadPath, "partial");

        var response = await AddMagnetAsync(httpClient, "7575757575757575757575757575757575757575", "Finalization Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Completed,
            timeout: TimeSpan.FromSeconds(5));

        await Task.Delay(300);
        Assert.Empty(ReadCallbackInvocations(callbackOutputPath));

        var pendingState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId);
        Assert.Equal(TorrentCompletionCallbackState.PendingFinalization.ToString(), pendingState.State);

        File.Delete(partialPayloadPath);

        await WaitForAsync(
            () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
            invocations => invocations.Count == 1,
            timeout: TimeSpan.FromSeconds(5));

        var invokedState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent.TorrentId);
        Assert.Equal(TorrentCompletionCallbackState.Invoked.ToString(), invokedState.State);
        Assert.NotNull(invokedState.InvokedAtUtc);
    }

    [Fact]
    public async Task FakeRuntime_CompletionCallback_WaitsForMultiFileFinalizationTree()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-multi-finalization");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);
        var finalPayloadPath = Path.Combine(downloadPath, "TV", "Finalization Show");

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

        Directory.CreateDirectory(finalPayloadPath);
        var partialEpisodePath = Path.Combine(finalPayloadPath, "Season 01", "Episode 01.mkv.!mt");
        Directory.CreateDirectory(Path.GetDirectoryName(partialEpisodePath)!);
        File.WriteAllText(partialEpisodePath, "partial");

        var response = await AddMagnetAsync(httpClient, "7676767676767676767676767676767676767676", "Finalization Show", "TV");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Completed,
            timeout: TimeSpan.FromSeconds(5));

        await Task.Delay(300);
        Assert.Empty(ReadCallbackInvocations(callbackOutputPath));

        var pendingState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId);
        Assert.Equal(TorrentCompletionCallbackState.PendingFinalization.ToString(), pendingState.State);

        File.Move(partialEpisodePath, Path.Combine(finalPayloadPath, "Season 01", "Episode 01.mkv"));

        await WaitForAsync(
            () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
            invocations => invocations.Count == 1,
            timeout: TimeSpan.FromSeconds(5));

        var invokedState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent.TorrentId);
        Assert.Equal(TorrentCompletionCallbackState.Invoked.ToString(), invokedState.State);
    }

    [Fact]
    public async Task FakeRuntime_PendingFinalization_SurvivesRestart_AndInvokesWhenPayloadAppears()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-pending-restart");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);
        Guid torrentId;

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         runtimeTickIntervalMilliseconds: 50,
                         metadataResolutionDelayMilliseconds: 0,
                         downloadProgressPercentPerTick: 50))
        {
            using var httpClient = factory.CreateClient();
            await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

            var response = await AddMagnetAsync(httpClient, "7777777777777777777777777777777777777777", "Restart Pending Movie", "Movie");
            var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;

            await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.State == TorrentState.Completed,
                timeout: TimeSpan.FromSeconds(5));

            await Task.Delay(300);
            Assert.Empty(ReadCallbackInvocations(callbackOutputPath));

            var pendingState = await ReadPersistedCallbackStateAsync(storagePath, torrentId);
            Assert.Equal(TorrentCompletionCallbackState.PendingFinalization.ToString(), pendingState.State);
        }

        CreateSingleFilePayload(Path.Combine(downloadPath, "Movie", "Restart Pending Movie"));

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         runtimeTickIntervalMilliseconds: 50,
                         metadataResolutionDelayMilliseconds: 0,
                         downloadProgressPercentPerTick: 50))
        {
            using var httpClient = factory.CreateClient();

            await WaitForAsync(
                () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
                invocations => invocations.Count == 1,
                timeout: TimeSpan.FromSeconds(5));

            var invokedState = await ReadPersistedCallbackStateAsync(storagePath, torrentId);
            Assert.Equal(TorrentCompletionCallbackState.Invoked.ToString(), invokedState.State);
            Assert.NotNull(invokedState.InvokedAtUtc);

            var hostStatusResponse = await httpClient.GetAsync("api/host/status");
            hostStatusResponse.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task FakeRuntime_PendingFinalization_TimesOut_WhenPayloadNeverAppears()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-finalization-timeout");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(
            httpClient,
            "/bin/sh",
            callbackScriptPath,
            rootPath,
            finalizationTimeoutSeconds: 1);

        var response = await AddMagnetAsync(httpClient, "7878787878787878787878787878787878787878", "Timeout Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        await WaitForAsync(
            async () => await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId),
            state => state.State == TorrentCompletionCallbackState.TimedOut.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        Assert.Empty(ReadCallbackInvocations(callbackOutputPath));

        var timedOutState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId);
        Assert.Contains("Timed out waiting for final payload visibility", timedOutState.LastError ?? string.Empty, StringComparison.Ordinal);

        var torrentDetail = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}");
        Assert.NotNull(torrentDetail);
        Assert.Equal(TorrentCompletionCallbackState.TimedOut.ToString(), torrentDetail.CompletionCallbackState);
        Assert.True(torrentDetail.CanRetryCompletionCallback);
        Assert.Contains("Timed out waiting for final payload visibility", torrentDetail.CompletionCallbackLastError ?? string.Empty, StringComparison.Ordinal);

        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");
        var torrentSummary = Assert.Single(torrents!, torrent => torrent.TorrentId == addedTorrent.TorrentId);
        Assert.Equal(TorrentCompletionCallbackState.TimedOut.ToString(), torrentSummary.CompletionCallbackState);
        Assert.True(torrentSummary.CanRetryCompletionCallback);

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=50&torrentId={addedTorrent.TorrentId}");
        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.callback.finalization_timed_out");
    }

    [Fact]
    public async Task FakeRuntime_CompletionCallback_Failure_PersistsFailedState()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-failed");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath, exitCode: 1);

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

        var response = await AddMagnetAsync(httpClient, "7979797979797979797979797979797979797979", "Failed Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        CreateSingleFilePayload(Path.Combine(downloadPath, "Movie", "Failed Movie"));

        await WaitForAsync(
            async () => await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId),
            state => state.State == TorrentCompletionCallbackState.Failed.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        var failedState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId);
        Assert.Equal("The callback exited with code 1.", failedState.LastError);

        var torrentDetail = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}");
        Assert.NotNull(torrentDetail);
        Assert.Equal(TorrentCompletionCallbackState.Failed.ToString(), torrentDetail.CompletionCallbackState);
        Assert.True(torrentDetail.CanRetryCompletionCallback);
        Assert.Equal("The callback exited with code 1.", torrentDetail.CompletionCallbackLastError);
    }

    [Fact]
    public async Task FakeRuntime_RetryCompletionCallback_RequeuesTimedOutState_AndInvokesWhenPayloadAppears()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-retry-timeout");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(
            httpClient,
            "/bin/sh",
            callbackScriptPath,
            rootPath,
            finalizationTimeoutSeconds: 1);

        var response = await AddMagnetAsync(httpClient, "8080808080808080808080808080808080808080", "Retry Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var timedOutTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.TimedOut.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(timedOutTorrent);
        Assert.True(timedOutTorrent.CanRetryCompletionCallback);

        var retryResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/completion-callback/retry", content: null);
        retryResponse.EnsureSuccessStatusCode();

        var retryResult = await retryResponse.Content.ReadFromJsonAsync<TorrentActionResultDto>();
        Assert.NotNull(retryResult);
        Assert.Equal("retry_completion_callback", retryResult.Action);

        var pendingTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.PendingFinalization.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(pendingTorrent);
        Assert.False(pendingTorrent.CanRetryCompletionCallback);
        Assert.Null(pendingTorrent.CompletionCallbackLastError);

        CreateSingleFilePayload(Path.Combine(downloadPath, "Movie", "Retry Movie"));

        await WaitForAsync(
            () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
            invocations => invocations.Count == 1,
            timeout: TimeSpan.FromSeconds(5));

        var invokedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.Invoked.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(invokedTorrent);
        Assert.False(invokedTorrent.CanRetryCompletionCallback);
        Assert.NotNull(invokedTorrent.CompletionCallbackInvokedAtUtc);
    }

    [Fact]
    public async Task FakeRuntime_RetryCompletionCallback_ReturnsConflict_ForInvokedState()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-retry-conflict");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

        var response = await AddMagnetAsync(httpClient, "8181818181818181818181818181818181818181", "Retry Conflict Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        CreateSingleFilePayload(Path.Combine(downloadPath, "Movie", "Retry Conflict Movie"));

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.Invoked.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        var retryResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/completion-callback/retry", content: null);
        var error = await retryResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, retryResponse.StatusCode);
        Assert.Equal("invalid_callback_state", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FakeRuntime_PauseAndResumeWhileDownloading_PreservesPausedStateUntilResumed()
    {
        await using var factory = CreateFactory(
            runtimeTickIntervalMilliseconds: 100,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 0.5,
            maxActiveDownloads: 1);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "7171717171717171717171717171717171717171", "Fake Active Pause");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var downloadingTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Downloading,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(downloadingTorrent);

        var pauseResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/pause", content: null);
        pauseResponse.EnsureSuccessStatusCode();

        var pausedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null &&
                       torrent.State == TorrentState.Paused &&
                       torrent.WaitReason == TorrentWaitReason.PausedByOperator,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(pausedTorrent);
        var pausedProgress = pausedTorrent.ProgressPercent;

        await Task.Delay(500);

        var pausedAfterDelay = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}");

        Assert.NotNull(pausedAfterDelay);
        Assert.Equal(TorrentState.Paused, pausedAfterDelay.State);
        Assert.Equal(TorrentWaitReason.PausedByOperator, pausedAfterDelay.WaitReason);
        Assert.Equal(pausedProgress, pausedAfterDelay.ProgressPercent);

        var resumeResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent.TorrentId}/resume", content: null);
        resumeResponse.EnsureSuccessStatusCode();

        var resumedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Downloading,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(resumedTorrent);
        Assert.Equal(TorrentState.Downloading, resumedTorrent.State);
    }

    [Fact]
    public async Task FakeRuntime_PausedDownloadingTorrent_StaysPausedAcrossRestart()
    {
        var rootPath = CreateTempRootPath("torrentcore-fake-active-pause-restart");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        Guid torrentId;

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         runtimeTickIntervalMilliseconds: 100,
                         metadataResolutionDelayMilliseconds: 0,
                         downloadProgressPercentPerTick: 0.5,
                         maxActiveDownloads: 1))
        {
            using var httpClient = factory.CreateClient();

            var response = await AddMagnetAsync(httpClient, "7272727272727272727272727272727272727272", "Fake Active Restart Pause");
            var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;

            await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.State == TorrentState.Downloading,
                timeout: TimeSpan.FromSeconds(5));

            var pauseResponse = await httpClient.PostAsync($"api/torrents/{torrentId}/pause", content: null);
            pauseResponse.EnsureSuccessStatusCode();

            await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null &&
                           torrent.State == TorrentState.Paused &&
                           torrent.WaitReason == TorrentWaitReason.PausedByOperator,
                timeout: TimeSpan.FromSeconds(5));
        }

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         runtimeTickIntervalMilliseconds: 100,
                         metadataResolutionDelayMilliseconds: 0,
                         downloadProgressPercentPerTick: 0.5,
                         maxActiveDownloads: 1))
        {
            using var httpClient = factory.CreateClient();

            var pausedTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null &&
                           torrent.State == TorrentState.Paused &&
                           torrent.WaitReason == TorrentWaitReason.PausedByOperator,
                timeout: TimeSpan.FromSeconds(5));

            Assert.NotNull(pausedTorrent);

            await Task.Delay(500);

            var pausedAfterDelay = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}");

            Assert.NotNull(pausedAfterDelay);
            Assert.Equal(TorrentState.Paused, pausedAfterDelay.State);
            Assert.Equal(TorrentWaitReason.PausedByOperator, pausedAfterDelay.WaitReason);
        }
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
        var queuedTorrent = Assert.Single(torrents, torrent => torrent.State == TorrentState.Queued);
        Assert.Equal(TorrentWaitReason.WaitingForMetadataSlot, queuedTorrent.WaitReason);
        Assert.Equal(1, queuedTorrent.QueuePosition);
    }

    [Fact]
    public async Task GetHostStatus_ReportsQueueAndRuntimeStateBreakdown()
    {
        await using var factory = CreateFactory(
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 5_000,
            maxActiveMetadataResolutions: 1);
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "3131313131313131313131313131313131313131", "Status One");
        await AddMagnetAsync(httpClient, "4141414141414141414141414141414141414141", "Status Two");

        var hostStatus = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status"),
            status => status is not null &&
                      status.TorrentCount == 2 &&
                      status.ResolvingMetadataCount == 1 &&
                      status.MetadataQueueCount == 1 &&
                      status.DownloadingCount == 0 &&
                      status.DownloadQueueCount == 0,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(hostStatus);
        Assert.Equal(2, hostStatus.TorrentCount);
        Assert.Equal(0, hostStatus.AvailableMetadataResolutionSlots);
        Assert.Equal(4, hostStatus.AvailableDownloadSlots);
        Assert.Equal(1, hostStatus.ResolvingMetadataCount);
        Assert.Equal(1, hostStatus.MetadataQueueCount);
        Assert.Equal(0, hostStatus.DownloadingCount);
        Assert.Equal(0, hostStatus.DownloadQueueCount);
        Assert.Equal(0, hostStatus.SeedingCount);
        Assert.Equal(0, hostStatus.PausedCount);
        Assert.Equal(0, hostStatus.CompletedCount);
        Assert.Equal(0, hostStatus.ErrorCount);
        Assert.Equal(0, hostStatus.CurrentConnectedPeerCount);
        Assert.Equal(0, hostStatus.CurrentDownloadRateBytesPerSecond);
        Assert.Equal(0, hostStatus.CurrentUploadRateBytesPerSecond);
    }

    [Fact]
    public async Task FakeRuntime_ReportsDownloadQueueWaitReason_AndQueuePosition()
    {
        await using var factory = CreateFactory(
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 5,
            maxActiveDownloads: 1);
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "5151515151515151515151515151515151515151", "Download Slot One");
        await AddMagnetAsync(httpClient, "6161616161616161616161616161616161616161", "Download Slot Two");

        var torrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            items => items is not null &&
                     items.Count == 2 &&
                     items.Count(torrent => torrent.State == TorrentState.Downloading) == 1 &&
                     items.Count(torrent => torrent.State == TorrentState.Queued) == 1 &&
                     items.Any(torrent => torrent.WaitReason == TorrentWaitReason.WaitingForDownloadSlot && torrent.QueuePosition == 1),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(torrents);
        Assert.Contains(torrents, torrent => torrent.WaitReason == TorrentWaitReason.WaitingForDownloadSlot && torrent.QueuePosition == 1);
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
    public async Task AddMagnet_ReturnsBadRequest_ForUnknownCategory()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = "magnet:?xt=urn:btih:1234123412341234123412341234123412341234&dn=Unknown%20Category",
            CategoryKey = "Podcast",
        });

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_category", error.GetProperty("code").GetString());
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

    private static async Task<HttpResponseMessage> AddMagnetAsync(HttpClient httpClient, string infoHash, string name, string? categoryKey = null)
    {
        return await httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(name)}",
            CategoryKey = categoryKey,
        });
    }

    private static async Task ForcePersistedTorrentSnapshotAsync(
        string storagePath,
        Guid torrentId,
        TorrentState state,
        TorrentDesiredState desiredState,
        string? errorMessage)
    {
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE torrents
            SET
                state = $state,
                desired_state = $desired_state,
                progress_percent = 0,
                downloaded_bytes = 0,
                download_rate_bytes_per_second = 0,
                upload_rate_bytes_per_second = 0,
                connected_peer_count = 0,
                error_message = $error_message
            WHERE torrent_id = $torrent_id;
            """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$desired_state", desiredState.ToString());
        command.Parameters.AddWithValue("$error_message", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdateCompletionCallbackSettingsAsync(
        HttpClient httpClient,
        string commandPath,
        string? arguments,
        string? workingDirectory,
        string? apiBaseUrlOverride = null,
        string? apiKeyOverride = null,
        int? finalizationTimeoutSeconds = null)
    {
        var response = await httpClient.PutAsJsonAsync("api/host/runtime-settings", new UpdateRuntimeSettingsRequest
        {
            SeedingStopMode = SeedingStopMode.StopImmediately.ToString(),
            SeedingStopRatio = 1.0,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never.ToString(),
            CompletedTorrentCleanupMinutes = 60,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            CompletionCallbackEnabled = true,
            CompletionCallbackCommandPath = commandPath,
            CompletionCallbackArguments = arguments,
            CompletionCallbackWorkingDirectory = workingDirectory,
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = finalizationTimeoutSeconds,
            CompletionCallbackApiBaseUrlOverride = apiBaseUrlOverride,
            CompletionCallbackApiKeyOverride = apiKeyOverride,
        });
        response.EnsureSuccessStatusCode();
    }

    private static string CreateCallbackCaptureScript(string rootPath, string outputPath, int exitCode = 0)
    {
        Directory.CreateDirectory(rootPath);

        var scriptPath = Path.Combine(rootPath, "capture-callback.sh");
        File.WriteAllText(
            scriptPath,
            $$"""
            #!/bin/sh
            {
              printf 'TR_TORRENT_ID=%s\n' "${TR_TORRENT_ID}"
              printf 'TR_TORRENT_HASH=%s\n' "${TR_TORRENT_HASH}"
              printf 'TR_TORRENT_NAME=%s\n' "${TR_TORRENT_NAME}"
              printf 'TR_TORRENT_DIR=%s\n' "${TR_TORRENT_DIR}"
              printf 'TR_TORRENT_LABELS=%s\n' "${TR_TORRENT_LABELS}"
              printf 'TVMAZE_API_COMPLETE_URL=%s\n' "${TVMAZE_API_COMPLETE_URL}"
              printf 'TVMAZE_API_COMPLETE_API_KEY=%s\n' "${TVMAZE_API_COMPLETE_API_KEY}"
              printf -- '---\n'
            } >> '{{outputPath}}'
            exit {{exitCode}}
            """);

        return scriptPath;
    }

    private static void CreateSingleFilePayload(string finalPayloadPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPayloadPath)!);
        File.WriteAllText(finalPayloadPath, "payload");
    }

    private static async Task<PersistedCallbackState> ReadPersistedCallbackStateAsync(string storagePath, Guid torrentId)
    {
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                completion_callback_state,
                completion_callback_pending_since_utc,
                completion_callback_invoked_at_utc,
                completion_callback_last_error
            FROM torrents
            WHERE torrent_id = $torrent_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new PersistedCallbackState(null, null, null, null);
        }

        return new PersistedCallbackState(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadCallbackInvocations(string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            return [];
        }

        var invocations = new List<IReadOnlyDictionary<string, string>>();
        var current = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in File.ReadAllLines(outputPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line == "---")
            {
                invocations.Add(new Dictionary<string, string>(current, StringComparer.Ordinal));
                current.Clear();
                continue;
            }

            var delimiterIndex = line.IndexOf('=');
            if (delimiterIndex <= 0)
            {
                continue;
            }

            current[line[..delimiterIndex]] = line[(delimiterIndex + 1)..];
        }

        if (current.Count > 0)
        {
            invocations.Add(new Dictionary<string, string>(current, StringComparer.Ordinal));
        }

        return invocations;
    }

    private static string CreateTempRootPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
    }

    private sealed record PersistedCallbackState(
        string? State,
        DateTimeOffset? PendingSinceUtc,
        DateTimeOffset? InvokedAtUtc,
        string? LastError);

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
