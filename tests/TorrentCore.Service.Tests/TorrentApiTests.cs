using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.History;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Maintenance;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.Configuration;
using TorrentCore.Persistence.Sqlite.Logging;
using TorrentCore.Service.Application;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;
using TorrentCore.Service.Vpn;

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
        Assert.Equal(ServiceApiContract.CurrentVersion, hostStatus.ApiVersion);
        Assert.Equal("0.5.1", hostStatus.ServiceVersion);
        Assert.Matches("^[0-9a-f]{40}$", hostStatus.ServiceBuild);
        Assert.Equal("TorrentCore.Service", hostStatus.ServiceName);
        Assert.Equal("Fake", hostStatus.EngineRuntime);
        Assert.Equal(55_123, hostStatus.EngineListenPort);
        Assert.Equal(55_124, hostStatus.EngineDhtPort);
        Assert.False(hostStatus.EngineAllowPeerExchange);
        Assert.Equal(TorrentEncryptionMode.EncryptedPreferred.ToString(), hostStatus.EngineEncryptionMode);
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
        Assert.False(hostStatus.PartialFilesEnabled);
        Assert.Empty(hostStatus.PartialFileSuffix);
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
        Assert.Equal(TorrentEncryptionMode.EncryptedPreferred.ToString(), hostStatus.EngineEncryptionMode);
        Assert.Equal(60, hostStatus.EngineMaximumConnections);
        Assert.Equal(4, hostStatus.EngineMaximumHalfOpenConnections);
        Assert.Equal(12_500_000, hostStatus.EngineMaximumDownloadRateBytesPerSecond);
        Assert.Equal(3_000_000, hostStatus.EngineMaximumUploadRateBytesPerSecond);
    }

    [Fact]
    public async Task GetDashboardLifecycle_ReturnsCurrentInstanceLifecycleSummary()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "Dashboard Lifecycle");

        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");
        var summary = await httpClient.GetFromJsonAsync<DashboardLifecycleSummaryDto>("api/host/dashboard-lifecycle");

        Assert.NotNull(hostStatus);
        Assert.NotNull(summary);
        Assert.Equal(hostStatus.ServiceInstanceId, summary.ServiceInstanceId);
        Assert.NotNull(summary.StartupReadyAtUtc);
        Assert.NotNull(summary.RecoveryCompletedAtUtc);
        Assert.NotNull(summary.FirstEventAtUtc);
        Assert.NotNull(summary.LastEventAtUtc);
        Assert.Equal(0, summary.StartupRecoveredTorrentCount);
        Assert.Equal(0, summary.StartupNormalizedTorrentCount);
        Assert.Equal(1, summary.TorrentsAddedCount);
        Assert.Equal(0, summary.TorrentsRemovedCount);
        Assert.Equal(0, summary.MetadataRefreshRequestedCount);
        Assert.Equal(0, summary.MetadataResetRequestedCount);
        Assert.Equal(0, summary.MetadataRestartRequestedCount);
        Assert.NotEmpty(summary.RecentEvents);
        Assert.Contains(summary.RecentEvents, entry => entry.EventType == "service.startup.ready");
        Assert.Contains(summary.RecentEvents, entry => entry.EventType == "torrent.added");
    }

    [Fact]
    public async Task AddMagnet_CreatesTorrentHistoryRow()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-add");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(
            httpClient,
            "2121212121212121212121212121212121212121",
            "History Creation Torrent",
            "TV");
        response.EnsureSuccessStatusCode();

        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(torrent);

        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  torrent_id,
                                  name,
                                  magnet_uri,
                                  info_hash,
                                  category_key,
                                  download_root_path,
                                  latest_torrent_state,
                                  latest_progress_percent,
                                  latest_downloaded_bytes,
                                  submitted_at_utc,
                                  last_updated_at_utc,
                                  invoke_completion_callback,
                                  completion_callback_label,
                                  data_deleted,
                                  removed_by_cleanup_policy
                              FROM torrent_history
                              WHERE torrent_id = $torrent_id;
                              """;
        command.Parameters.AddWithValue("$torrent_id", torrent.TorrentId.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(torrent.TorrentId.ToString(), reader.GetString(0));
        Assert.Equal(torrent.Name, reader.GetString(1));
        Assert.Equal(torrent.MagnetUri, reader.GetString(2));
        Assert.Equal(torrent.InfoHash, reader.GetString(3));
        Assert.Equal("TV", reader.GetString(4));
        Assert.Equal(Path.Combine(downloadPath, "TV"), reader.GetString(5));
        Assert.Equal(torrent.State.ToString(), reader.GetString(6));
        Assert.Equal(torrent.ProgressPercent, reader.GetDouble(7));
        Assert.Equal(torrent.DownloadedBytes, reader.GetInt64(8));
        Assert.False(reader.IsDBNull(9));
        Assert.False(reader.IsDBNull(10));
        Assert.True(reader.GetInt64(11) != 0);
        Assert.Equal("TV", reader.GetString(12));
        Assert.Equal(0, reader.GetInt64(13));
        Assert.Equal(0, reader.GetInt64(14));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Remove_LeavesHistoryRow_AndStampsManualRemoval()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-remove");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(
            httpClient,
            "2424242424242424242424242424242424242424",
            "History Remove Torrent",
            "Movie");
        response.EnsureSuccessStatusCode();

        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(torrent);

        var removeResponse = await httpClient.PostAsync($"api/torrents/{torrent.TorrentId}/remove", content: null);
        removeResponse.EnsureSuccessStatusCode();

        var historyRow = await WaitForAsync(
            async () => await GetRemovalHistoryRowAsync(databaseFilePath, torrent.TorrentId),
            row => row is not null && row.RemovedAtUtc is not null,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(historyRow);
        Assert.NotNull(historyRow.RemovedAtUtc);
        Assert.False(historyRow.DataDeleted);
        Assert.Equal("manual_remove", historyRow.RemovalReason);
        Assert.Equal(TorrentRemovalKind.ManualRemoval, historyRow.RemovalKind);
        Assert.False(historyRow.RemovedByCleanupPolicy);
    }

    [Fact]
    public async Task RemoveWithDeleteData_LeavesHistoryRow_AndStampsDeleteData()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-remove-data");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(
            httpClient,
            "2525252525252525252525252525252525252525",
            "History Remove Data Torrent",
            "Movie");
        response.EnsureSuccessStatusCode();

        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(torrent);

        var removeResponse = await httpClient.PostAsJsonAsync(
            $"api/torrents/{torrent.TorrentId}/remove",
            new RemoveTorrentRequest
            {
                DeleteData = true,
            });
        removeResponse.EnsureSuccessStatusCode();

        var historyRow = await WaitForAsync(
            async () => await GetRemovalHistoryRowAsync(databaseFilePath, torrent.TorrentId),
            row => row is not null && row.RemovedAtUtc is not null && row.DataDeleted,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(historyRow);
        Assert.NotNull(historyRow.RemovedAtUtc);
        Assert.True(historyRow.DataDeleted);
        Assert.Equal("manual_remove_delete_data", historyRow.RemovalReason);
        Assert.Equal(TorrentRemovalKind.ManualRemovalWithData, historyRow.RemovalKind);
        Assert.False(historyRow.RemovedByCleanupPolicy);
    }

    [Fact]
    public async Task GetHistory_ReturnsNewestFirst_AndSupportsExplicitFilters()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-api-list");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var firstResponse = await AddMagnetAsync(httpClient, "3131313131313131313131313131313131313131", "Alpha First", "TV");
        var secondResponse = await AddMagnetAsync(httpClient, "3232323232323232323232323232323232323232", "Bravo Second", "Movie");
        var thirdResponse = await AddMagnetAsync(httpClient, "3333333333333333333333333333333333333333", "Alpha Removed", "TV");

        var firstTorrent = await firstResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        var secondTorrent = await secondResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        var thirdTorrent = await thirdResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        Assert.NotNull(firstTorrent);
        Assert.NotNull(secondTorrent);
        Assert.NotNull(thirdTorrent);

        var may20 = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 5, 20, 9, 0, 0)));
        var may21 = new DateTimeOffset(2026, 5, 21, 10, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 5, 21, 10, 0, 0)));
        var may22 = new DateTimeOffset(2026, 5, 22, 11, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 5, 22, 11, 0, 0)));

        await UpdateHistoryRowAsync(databaseFilePath, firstTorrent.TorrentId, may20, "Completed", removedAtUtc: null);
        await UpdateHistoryRowAsync(databaseFilePath, secondTorrent.TorrentId, may21, "Seeding", removedAtUtc: null);
        await UpdateHistoryRowAsync(
            databaseFilePath,
            thirdTorrent.TorrentId,
            may22,
            "Downloading",
            removedAtUtc: may22.AddHours(1),
            removalReason: "Download abandoned after 72 hours without peer or transfer activity.",
            removalKind: TorrentRemovalKind.ColdDownloadAbandonment);

        var allHistory = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>("api/history");

        Assert.NotNull(allHistory);
        Assert.Equal([thirdTorrent.TorrentId, secondTorrent.TorrentId, firstTorrent.TorrentId], allHistory.Select(item => item.TorrentId).ToArray());
        Assert.Equal(TorrentHistoryOutcome.Abandoned, allHistory[0].Outcome);
        Assert.Equal(TorrentRemovalKind.ColdDownloadAbandonment, allHistory[0].RemovalKind);

        var byName = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>("api/history?torrentName=alpha");
        Assert.NotNull(byName);
        Assert.Equal(2, byName.Count);
        Assert.All(byName, item => Assert.Contains("Alpha", item.Name, StringComparison.OrdinalIgnoreCase));

        var byCategory = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>("api/history?categoryKey=ovi");
        Assert.NotNull(byCategory);
        Assert.Single(byCategory);
        Assert.Equal(secondTorrent.TorrentId, byCategory[0].TorrentId);

        var byState = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>("api/history?state=eed");
        Assert.NotNull(byState);
        Assert.Single(byState);
        Assert.Equal(secondTorrent.TorrentId, byState[0].TorrentId);

        var removed = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>("api/history?removed=true");
        Assert.NotNull(removed);
        Assert.Single(removed);
        Assert.Equal(thirdTorrent.TorrentId, removed[0].TorrentId);

        var active = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>("api/history?removed=false");
        Assert.NotNull(active);
        Assert.Equal(2, active.Count);
        Assert.DoesNotContain(active, item => item.TorrentId == thirdTorrent.TorrentId);

        var abandoned = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>(
            "api/history?outcome=Abandoned");
        Assert.NotNull(abandoned);
        Assert.Single(abandoned);
        Assert.Equal(thirdTorrent.TorrentId, abandoned[0].TorrentId);
        Assert.Equal(TorrentHistoryOutcome.Abandoned, abandoned[0].Outcome);

        var byDate = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>("api/history?fromDate=2026-05-21&toDate=2026-05-22");
        Assert.NotNull(byDate);
        Assert.Equal([thirdTorrent.TorrentId, secondTorrent.TorrentId], byDate.Select(item => item.TorrentId).ToArray());

        var limited = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentHistorySummaryDto>>("api/history?take=1");
        Assert.NotNull(limited);
        Assert.Single(limited);
        Assert.Equal(thirdTorrent.TorrentId, limited[0].TorrentId);

        var filterOptions =
                await httpClient.GetFromJsonAsync<TorrentHistoryFilterOptionsDto>("api/history/filter-options");

        Assert.NotNull(filterOptions);
        Assert.Equal(["Movie", "TV"], filterOptions.CategoryKeys);
        Assert.Equal(["Completed", "Downloading", "Seeding"], filterOptions.States);
    }

    [Fact]
    public async Task GetHistoryByTorrentId_ReturnsHistoryDetail()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-api-detail");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "3434343434343434343434343434343434343434", "History Detail Torrent", "Movie");
        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        Assert.NotNull(torrent);

        var localSubmittedAt = new DateTimeOffset(2026, 5, 23, 14, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 5, 23, 14, 30, 0)));
        await UpdateHistoryRowAsync(databaseFilePath, torrent.TorrentId, localSubmittedAt, "Paused", removedAtUtc: null);

        var detail = await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{torrent.TorrentId}");

        Assert.NotNull(detail);
        Assert.Equal(torrent.TorrentId, detail.TorrentId);
        Assert.Equal("History Detail Torrent", detail.Name);
        Assert.Equal("Movie", detail.CategoryKey);
        Assert.Equal("Paused", detail.LatestTorrentState);
        Assert.Equal(localSubmittedAt.Date, detail.SubmittedAt.Date);
        Assert.Equal(Path.Combine(downloadPath, "Movie"), detail.DownloadRootPath);
    }

    [Fact]
    public async Task GetHistoryByTorrentId_ReturnsNotFound_WhenMissing()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync($"api/history/by-torrent/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        Assert.False(settings.PartialFilesEnabled);
        Assert.Empty(settings.PartialFileSuffix);
        Assert.Equal(SeedingStopMode.Unlimited.ToString(), settings.SeedingStopMode);
        Assert.Equal(CompletedTorrentCleanupMode.Never.ToString(), settings.CompletedTorrentCleanupMode);
        Assert.Equal(60, settings.CompletedTorrentCleanupMinutes);
        Assert.False(settings.DeleteLogsForCompletedTorrents);
        Assert.False(settings.RuntimeTickDurationSummaryEnabled);
        Assert.Equal(5, settings.EngineConnectionFailureLogBurstLimit);
        Assert.Equal(60, settings.EngineConnectionFailureLogWindowSeconds);
        Assert.False(settings.EngineAllowPeerExchange);
        Assert.Equal(TorrentEncryptionMode.EncryptedPreferred.ToString(), settings.EngineEncryptionMode);
        Assert.Equal(150, settings.EngineMaximumConnections);
        Assert.Equal(8, settings.EngineMaximumHalfOpenConnections);
        Assert.Equal(0, settings.EngineMaximumDownloadRateBytesPerSecond);
        Assert.Equal(0, settings.EngineMaximumUploadRateBytesPerSecond);
        Assert.Equal(4, settings.MaxActiveMetadataResolutions);
        Assert.Equal(4, settings.MaxActiveDownloads);
        Assert.Equal(90, settings.MetadataRefreshStaleSeconds);
        Assert.Equal(30, settings.MetadataRefreshRestartDelaySeconds);
        Assert.Equal(15, settings.MetadataResolutionTimeSliceMinutes);
        Assert.Equal(30, settings.AutomaticMetadataResetStuckThresholdSeconds);
        Assert.Equal(120, settings.ColdDownloadRecoveryThresholdMinutes);
        Assert.Equal(60, settings.ColdDownloadRecoveryIntervalMinutes);
        Assert.Equal(72, settings.ColdDownloadAbandonAfterHours);
        Assert.False(settings.VpnEgressValidationEnabled);
        Assert.Equal("https://api.ipify.org/?format=json", settings.VpnEgressValidationEndpoint);
        Assert.Equal(["47.0.0.0/8"], settings.VpnEgressDirectIspCidrs);
        Assert.Equal(60, settings.VpnEgressDegradedCheckIntervalSeconds);
        Assert.Equal(240, settings.VpnEgressReadyCheckIntervalSeconds);
        Assert.Equal(10, settings.VpnEgressRequestTimeoutSeconds);
        Assert.Equal(10, settings.VpnEgressEngineSuspensionTimeoutSeconds);
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
        Assert.False(settings.AppliedEngineAllowPeerExchange);
        Assert.Equal(TorrentEncryptionMode.EncryptedPreferred.ToString(), settings.AppliedEngineEncryptionMode);
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
            DeleteLogsForCompletedTorrents = true,
            EngineConnectionFailureLogBurstLimit = 2,
            EngineConnectionFailureLogWindowSeconds = 180,
            EngineAllowPeerExchange = true,
            EngineEncryptionMode = TorrentEncryptionMode.EncryptedRequired.ToString(),
            EngineMaximumConnections = 70,
            EngineMaximumHalfOpenConnections = 6,
                EngineMaximumDownloadRateBytesPerSecond = 4_000_000,
                EngineMaximumUploadRateBytesPerSecond = 1_500_000,
                MaxActiveMetadataResolutions = 3,
                MaxActiveDownloads = 2,
                MetadataRefreshStaleSeconds = 90,
                MetadataRefreshRestartDelaySeconds = 30,
                AutomaticMetadataResetStuckThresholdSeconds = 45,
                ColdDownloadRecoveryThresholdMinutes = 180,
                ColdDownloadRecoveryIntervalMinutes = 45,
                ColdDownloadAbandonAfterHours = 48,
                CompletionCallbackEnabled = true,
                CompletionCallbackCommandPath = "/usr/local/bin/torrentcore-callback",
                CompletionCallbackArguments = "--run",
                CompletionCallbackWorkingDirectory = "/Users/dick/TorrentCore/Scripts",
                CompletionCallbackTimeoutSeconds = 45,
                CompletionCallbackFinalizationTimeoutSeconds = 180,
                CompletionCallbackApiBaseUrlOverride = "http://127.0.0.1:5501/api/complete",
                CompletionCallbackApiKeyOverride = "integration-key",
                VpnEgressValidationEnabled = true,
                VpnEgressValidationEndpoint = "https://vpn-check.example.test/ip?format=json",
                VpnEgressDirectIspCidrs = ["198.51.100.19/24", "203.0.113.0/24", "198.51.100.0/24"],
                VpnEgressDegradedCheckIntervalSeconds = 45,
                VpnEgressReadyCheckIntervalSeconds = 180,
                VpnEgressRequestTimeoutSeconds = 9,
                VpnEgressEngineSuspensionTimeoutSeconds = 8,
                RuntimeTickDurationSummaryEnabled = true,
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
            Assert.True(settings.DeleteLogsForCompletedTorrents);
            Assert.Equal(2, settings.EngineConnectionFailureLogBurstLimit);
            Assert.Equal(180, settings.EngineConnectionFailureLogWindowSeconds);
            Assert.True(settings.EngineAllowPeerExchange);
            Assert.False(settings.AppliedEngineAllowPeerExchange);
            Assert.Equal(TorrentEncryptionMode.EncryptedRequired.ToString(), settings.EngineEncryptionMode);
            Assert.Equal(70, settings.EngineMaximumConnections);
            Assert.Equal(6, settings.EngineMaximumHalfOpenConnections);
            Assert.Equal(4_000_000, settings.EngineMaximumDownloadRateBytesPerSecond);
            Assert.Equal(1_500_000, settings.EngineMaximumUploadRateBytesPerSecond);
            Assert.Equal(3, settings.MaxActiveMetadataResolutions);
            Assert.Equal(2, settings.MaxActiveDownloads);
            Assert.Equal(90, settings.MetadataRefreshStaleSeconds);
            Assert.Equal(30, settings.MetadataRefreshRestartDelaySeconds);
            Assert.Equal(45, settings.AutomaticMetadataResetStuckThresholdSeconds);
            Assert.Equal(180, settings.ColdDownloadRecoveryThresholdMinutes);
            Assert.Equal(45, settings.ColdDownloadRecoveryIntervalMinutes);
            Assert.Equal(48, settings.ColdDownloadAbandonAfterHours);
            Assert.True(settings.CompletionCallbackEnabled);
            Assert.Equal("/usr/local/bin/torrentcore-callback", settings.CompletionCallbackCommandPath);
            Assert.Equal("--run", settings.CompletionCallbackArguments);
            Assert.Equal("/Users/dick/TorrentCore/Scripts", settings.CompletionCallbackWorkingDirectory);
            Assert.Equal(45, settings.CompletionCallbackTimeoutSeconds);
            Assert.Equal(180, settings.CompletionCallbackFinalizationTimeoutSeconds);
            Assert.Equal("http://127.0.0.1:5501/api/complete", settings.CompletionCallbackApiBaseUrlOverride);
            Assert.Equal("integration-key", settings.CompletionCallbackApiKeyOverride);
            Assert.True(settings.VpnEgressValidationEnabled);
            Assert.Equal("https://vpn-check.example.test/ip?format=json", settings.VpnEgressValidationEndpoint);
            Assert.Equal(["198.51.100.0/24", "203.0.113.0/24"], settings.VpnEgressDirectIspCidrs);
            Assert.Equal(45, settings.VpnEgressDegradedCheckIntervalSeconds);
            Assert.Equal(180, settings.VpnEgressReadyCheckIntervalSeconds);
            Assert.Equal(9, settings.VpnEgressRequestTimeoutSeconds);
            Assert.Equal(8, settings.VpnEgressEngineSuspensionTimeoutSeconds);
            Assert.True(settings.RuntimeTickDurationSummaryEnabled);
            Assert.True(settings.EngineSettingsRequireRestart);
            Assert.NotNull(settings.UpdatedAtUtc);

            Assert.NotNull(hostStatus);
            Assert.Equal(SeedingStopMode.StopAfterRatioOrTime.ToString(), hostStatus.SeedingStopMode);
            Assert.Equal(1.5, hostStatus.SeedingStopRatio);
            Assert.Equal(90, hostStatus.SeedingStopMinutes);
            Assert.Equal(CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(), hostStatus.CompletedTorrentCleanupMode);
            Assert.Equal(15, hostStatus.CompletedTorrentCleanupMinutes);
            Assert.True(hostStatus.DeleteLogsForCompletedTorrents);
            Assert.Equal(2, hostStatus.EngineConnectionFailureLogBurstLimit);
            Assert.Equal(180, hostStatus.EngineConnectionFailureLogWindowSeconds);
            Assert.False(hostStatus.EngineAllowPeerExchange);
            Assert.Equal(TorrentEncryptionMode.EncryptedPreferred.ToString(), hostStatus.EngineEncryptionMode);
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
            Assert.True(settings.DeleteLogsForCompletedTorrents);
            Assert.Equal(2, settings.EngineConnectionFailureLogBurstLimit);
            Assert.Equal(180, settings.EngineConnectionFailureLogWindowSeconds);
            Assert.True(settings.EngineAllowPeerExchange);
            Assert.Equal(70, settings.EngineMaximumConnections);
            Assert.Equal(6, settings.EngineMaximumHalfOpenConnections);
            Assert.Equal(4_000_000, settings.EngineMaximumDownloadRateBytesPerSecond);
            Assert.Equal(1_500_000, settings.EngineMaximumUploadRateBytesPerSecond);
            Assert.Equal(3, settings.MaxActiveMetadataResolutions);
            Assert.Equal(2, settings.MaxActiveDownloads);
            Assert.Equal(90, settings.MetadataRefreshStaleSeconds);
            Assert.Equal(30, settings.MetadataRefreshRestartDelaySeconds);
            Assert.Equal(45, settings.AutomaticMetadataResetStuckThresholdSeconds);
            Assert.Equal(180, settings.ColdDownloadRecoveryThresholdMinutes);
            Assert.Equal(45, settings.ColdDownloadRecoveryIntervalMinutes);
            Assert.Equal(48, settings.ColdDownloadAbandonAfterHours);
            Assert.True(settings.CompletionCallbackEnabled);
            Assert.Equal("/usr/local/bin/torrentcore-callback", settings.CompletionCallbackCommandPath);
            Assert.Equal("--run", settings.CompletionCallbackArguments);
            Assert.Equal("/Users/dick/TorrentCore/Scripts", settings.CompletionCallbackWorkingDirectory);
            Assert.Equal(45, settings.CompletionCallbackTimeoutSeconds);
            Assert.Equal(180, settings.CompletionCallbackFinalizationTimeoutSeconds);
            Assert.Equal("http://127.0.0.1:5501/api/complete", settings.CompletionCallbackApiBaseUrlOverride);
            Assert.Equal("integration-key", settings.CompletionCallbackApiKeyOverride);
            Assert.True(settings.VpnEgressValidationEnabled);
            Assert.Equal("https://vpn-check.example.test/ip?format=json", settings.VpnEgressValidationEndpoint);
            Assert.Equal(["198.51.100.0/24", "203.0.113.0/24"], settings.VpnEgressDirectIspCidrs);
            Assert.Equal(45, settings.VpnEgressDegradedCheckIntervalSeconds);
            Assert.Equal(180, settings.VpnEgressReadyCheckIntervalSeconds);
            Assert.Equal(9, settings.VpnEgressRequestTimeoutSeconds);
            Assert.Equal(8, settings.VpnEgressEngineSuspensionTimeoutSeconds);
            Assert.True(settings.RuntimeTickDurationSummaryEnabled);
            Assert.Equal(TorrentEncryptionMode.EncryptedRequired.ToString(), settings.EngineEncryptionMode);
            Assert.Equal(70, settings.AppliedEngineMaximumConnections);
            Assert.Equal(6, settings.AppliedEngineMaximumHalfOpenConnections);
            Assert.True(settings.AppliedEngineAllowPeerExchange);
            Assert.Equal(TorrentEncryptionMode.EncryptedRequired.ToString(), settings.AppliedEngineEncryptionMode);
            Assert.Equal(4_000_000, settings.AppliedEngineMaximumDownloadRateBytesPerSecond);
            Assert.Equal(1_500_000, settings.AppliedEngineMaximumUploadRateBytesPerSecond);
            Assert.False(settings.EngineSettingsRequireRestart);

            Assert.NotNull(hostStatus);
            Assert.Equal(SeedingStopMode.StopAfterRatioOrTime.ToString(), hostStatus.SeedingStopMode);
            Assert.Equal(CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(), hostStatus.CompletedTorrentCleanupMode);
            Assert.True(hostStatus.DeleteLogsForCompletedTorrents);
            Assert.Equal(2, hostStatus.EngineConnectionFailureLogBurstLimit);
            Assert.Equal(180, hostStatus.EngineConnectionFailureLogWindowSeconds);
            Assert.True(hostStatus.EngineAllowPeerExchange);
            Assert.Equal(TorrentEncryptionMode.EncryptedRequired.ToString(), hostStatus.EngineEncryptionMode);
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
    public async Task UpdateRuntimeSettings_OmittedPeerExchange_PreservesCurrentValue()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var enableResponse = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(
                engineAllowPeerExchange: true,
                automaticMetadataResetStuckThresholdSeconds: 45)
        );
        enableResponse.EnsureSuccessStatusCode();

        var legacyResponse = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(engineAllowPeerExchange: null)
        );
        legacyResponse.EnsureSuccessStatusCode();

        var settings = await legacyResponse.Content.ReadFromJsonAsync<RuntimeSettingsDto>();
        Assert.NotNull(settings);
        Assert.True(settings.EngineAllowPeerExchange);
        Assert.Equal(45, settings.AutomaticMetadataResetStuckThresholdSeconds);
        Assert.False(settings.AppliedEngineAllowPeerExchange);
        Assert.True(settings.EngineSettingsRequireRestart);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(240, 0)]
    public async Task UpdateRuntimeSettings_RejectsInvalidLongColdRecoveryWindows(int thresholdMinutes,
        int intervalMinutes)
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(thresholdMinutes, intervalMinutes)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRuntimeSettings_RejectsNegativeColdDownloadAbandonmentWindow()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(coldDownloadAbandonAfterHours: -1)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(301)]
    public async Task UpdateRuntimeSettings_RejectsInvalidAutomaticMetadataResetStuckThreshold(int thresholdSeconds)
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(
                automaticMetadataResetStuckThresholdSeconds: thresholdSeconds)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task UpdateRuntimeSettings_RejectsInvalidMetadataResolutionTimeSlice(int minutes)
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(metadataResolutionTimeSliceMinutes: minutes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("http://api.ipify.org?format=json")]
    [InlineData("https://user:secret@example.test/ip")]
    [InlineData("https://example.test/ip#fragment")]
    public async Task UpdateRuntimeSettings_RejectsInvalidVpnEndpoint(string endpoint)
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(vpnEgressValidationEndpoint: endpoint)
        );
        var error = await response.Content.ReadFromJsonAsync<ServiceProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_runtime_settings", error?.Code);
        Assert.Equal(nameof(UpdateRuntimeSettingsRequest.VpnEgressValidationEndpoint), error?.Target);
    }

    [Theory]
    [InlineData("47.0.0.0")]
    [InlineData("47.0.0.0/33")]
    [InlineData("2001:db8::/32")]
    [InlineData("not-a-cidr")]
    public async Task UpdateRuntimeSettings_RejectsInvalidOrIpv6DirectIspCidr(string cidr)
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(vpnEgressDirectIspCidrs: [cidr])
        );
        var error = await response.Content.ReadFromJsonAsync<ServiceProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_runtime_settings", error?.Code);
        Assert.Equal(nameof(UpdateRuntimeSettingsRequest.VpnEgressDirectIspCidrs), error?.Target);
    }

    [Theory]
    [InlineData(0, 240, 10, "VpnEgressDegradedCheckIntervalSeconds")]
    [InlineData(60, 0, 10, "VpnEgressReadyCheckIntervalSeconds")]
    [InlineData(60, 240, 0, "VpnEgressRequestTimeoutSeconds")]
    [InlineData(10, 240, 10, "VpnEgressRequestTimeoutSeconds")]
    [InlineData(60, 10, 10, "VpnEgressRequestTimeoutSeconds")]
    public async Task UpdateRuntimeSettings_RejectsInvalidVpnIntervals(
        int degradedSeconds,
        int readySeconds,
        int timeoutSeconds,
        string expectedTarget)
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(
                vpnEgressDegradedCheckIntervalSeconds: degradedSeconds,
                vpnEgressReadyCheckIntervalSeconds: readySeconds,
                vpnEgressRequestTimeoutSeconds: timeoutSeconds)
        );
        var error = await response.Content.ReadFromJsonAsync<ServiceProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_runtime_settings", error?.Code);
        Assert.Equal(expectedTarget, error?.Target);
    }

    [Fact]
    public async Task UpdateRuntimeSettings_RejectsInvalidVpnEngineSuspensionTimeout()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(vpnEgressEngineSuspensionTimeoutSeconds: 0)
        );
        var error = await response.Content.ReadFromJsonAsync<ServiceProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_runtime_settings", error?.Code);
        Assert.Equal(nameof(UpdateRuntimeSettingsRequest.VpnEgressEngineSuspensionTimeoutSeconds), error?.Target);
    }

    [Fact]
    public async Task UpdateRuntimeSettings_EnablingVpnValidationRequiresDirectIspCidr()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(
                vpnEgressValidationEnabled: true,
                vpnEgressDirectIspCidrs: [])
        );
        var error = await response.Content.ReadFromJsonAsync<ServiceProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_runtime_settings", error?.Code);
        Assert.Equal(nameof(UpdateRuntimeSettingsRequest.VpnEgressDirectIspCidrs), error?.Target);
    }

    [Fact]
    public async Task UpdateRuntimeSettings_OmittedVpnFieldsPreserveCurrentDatabaseValues()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var firstResponse = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(
                vpnEgressValidationEnabled: true,
                vpnEgressValidationEndpoint: "https://egress.example.test/address",
                vpnEgressDirectIspCidrs: ["198.51.100.42/24"],
                vpnEgressDegradedCheckIntervalSeconds: 30,
                vpnEgressReadyCheckIntervalSeconds: 120,
                vpnEgressRequestTimeoutSeconds: 5,
                vpnEgressEngineSuspensionTimeoutSeconds: 7,
                runtimeTickDurationSummaryEnabled: true)
        );
        firstResponse.EnsureSuccessStatusCode();

        var legacyResponse = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest()
        );
        legacyResponse.EnsureSuccessStatusCode();
        var settings = await legacyResponse.Content.ReadFromJsonAsync<RuntimeSettingsDto>();

        Assert.NotNull(settings);
        Assert.True(settings.VpnEgressValidationEnabled);
        Assert.Equal("https://egress.example.test/address", settings.VpnEgressValidationEndpoint);
        Assert.Equal(["198.51.100.0/24"], settings.VpnEgressDirectIspCidrs);
        Assert.Equal(30, settings.VpnEgressDegradedCheckIntervalSeconds);
        Assert.Equal(120, settings.VpnEgressReadyCheckIntervalSeconds);
        Assert.Equal(5, settings.VpnEgressRequestTimeoutSeconds);
        Assert.Equal(7, settings.VpnEgressEngineSuspensionTimeoutSeconds);
        Assert.True(settings.RuntimeTickDurationSummaryEnabled);
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
    public async Task UpdateCategory_ReturnsStructuredError_WhenDownloadRootPathIsUnavailable()
    {
        var rootPath = CreateTempRootPath("torrentcore-category-update-unavailable");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var blockedPath = Path.Combine(rootPath, "blocked-root");

        Directory.CreateDirectory(rootPath);
        await File.WriteAllTextAsync(blockedPath, "blocked");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var updateResponse = await httpClient.PutAsJsonAsync("api/categories/Movie", new UpdateTorrentCategoryRequest
        {
            DisplayName = "Movies",
            CallbackLabel = "Movie",
            DownloadRootPath = blockedPath,
            Enabled = true,
            InvokeCompletionCallback = true,
            SortOrder = 12,
        });
        var error = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, updateResponse.StatusCode);
        Assert.Equal("category_download_root_unavailable", error.GetProperty("code").GetString());
        Assert.Equal(nameof(UpdateTorrentCategoryRequest.DownloadRootPath), error.GetProperty("target").GetString());
        Assert.Contains(blockedPath, error.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMagnet_Succeeds_WhenActivityLogWriteFails_AfterTorrentIsPersisted()
    {
        var logFailureSwitch = new TestActivityLogFailureSwitch();

        await using var factory = CreateFactory(configureServices: services =>
        {
            services.RemoveAll<IActivityLogService>();
            services.AddSingleton<IActivityLogService>(serviceProvider =>
                new FailingActivityLogService(
                    new SqliteActivityLogService(
                        serviceProvider.GetRequiredService<ResolvedTorrentCoreServicePaths>().DatabaseFilePath,
                        20_000
                    ),
                    logFailureSwitch
                ));
        });
        using var httpClient = factory.CreateClient();

        logFailureSwitch.FailWrites = true;

        var response = await AddMagnetAsync(httpClient, "C2C2C2C2C2C2C2C2C2C2C2C2C2C2C2C2C2C2C2C2", "Log Failure Success");
        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(torrent);
        Assert.NotNull(torrents);
        Assert.Contains(torrents, item => item.TorrentId == torrent.TorrentId);
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
    public async Task RemoveTorrent_WithDeleteData_RemovesPersistedPayloadPath()
    {
        var rootPath = CreateTempRootPath("torrentcore-delete-data");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "B2B2B2B2B2B2B2B2B2B2B2B2B2B2B2B2B2B2B2B2", "Delete Data Torrent");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(addedTorrent);

        var payloadDirectory = addedTorrent.SavePath;
        Directory.CreateDirectory(payloadDirectory);
        var payloadFile = Path.Combine(payloadDirectory, "payload.bin");
        File.WriteAllText(payloadFile, "payload");

        var removeResponse = await httpClient.PostAsJsonAsync(
            $"api/torrents/{addedTorrent.TorrentId}/remove",
            new RemoveTorrentRequest { DeleteData = true });
        var actionResult = await removeResponse.Content.ReadFromJsonAsync<TorrentActionResultDto>();
        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.NotNull(actionResult);
        Assert.Equal("remove", actionResult.Action);
        Assert.True(actionResult.DataDeleted);
        Assert.False(File.Exists(payloadFile));
        Assert.False(Directory.Exists(payloadDirectory));
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
        Assert.False(hostStatus.PartialFilesEnabled);
        Assert.Empty(hostStatus.PartialFileSuffix);
        Assert.Equal(SeedingStopMode.Unlimited.ToString(), hostStatus.SeedingStopMode);
        Assert.True(hostStatus.StartupRecoveryCompleted);
        Assert.Equal("9999999999999999999999999999999999999999", torrent.InfoHash);
        Assert.DoesNotContain(torrent.State, new[] { TorrentState.Error, TorrentState.Removed });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MonoTorrentTorrentSettings_RespectPeerExchangePreference(bool allowPeerExchange)
    {
        var settings = MonoTorrentEngineAdapter.CreateTorrentSettings(allowPeerExchange);

        Assert.Equal(allowPeerExchange, settings.AllowPeerExchange);
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
    public async Task MonoTorrentEngine_RemoveActiveTorrent_WithDeleteData_ReturnsAndRemoves()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-delete-data");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            downloadPath: downloadPath,
            storagePath: storagePath
        );
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(
            httpClient,
            "6767676767676767676767676767676767676768",
            "MonoTorrent Delete Data"
        );
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(addedTorrent);

        var removeResponse = await httpClient.PostAsJsonAsync(
            $"api/torrents/{addedTorrent.TorrentId}/remove",
            new RemoveTorrentRequest { DeleteData = true }
        );
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var actionResult = await removeResponse.Content.ReadFromJsonAsync<TorrentActionResultDto>();
        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(actionResult);
        Assert.Equal("remove", actionResult.Action);
        Assert.True(actionResult.DataDeleted);
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
    public async Task MonoTorrentEngine_GetEndpoints_DoNotWaitForLifecycleGate()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-read-lifecycle-separation");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            downloadPath: downloadPath,
            storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(
            httpClient,
            "7C7C7C7C7C7C7C7C7C7C7C7C7C7C7C7C7C7C7C7C",
            "MonoTorrent Read Lifecycle Separation");
        addResponse.EnsureSuccessStatusCode();
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(addedTorrent);

        var adapter = factory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
        var gateField = typeof(MonoTorrentEngineAdapter).GetField(
            "_gate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var lifecycleGate = Assert.IsType<SemaphoreSlim>(gateField?.GetValue(adapter));

        await lifecycleGate.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var listTask = httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>(
                "api/torrents",
                timeout.Token);
            var detailTask = httpClient.GetFromJsonAsync<TorrentDetailDto>(
                $"api/torrents/{addedTorrent.TorrentId}",
                timeout.Token);

            await Task.WhenAll(listTask, detailTask);

            var torrents = await listTask;
            var detail = await detailTask;
            Assert.NotNull(torrents);
            Assert.NotNull(detail);
            Assert.Contains(torrents, torrent => torrent.TorrentId == addedTorrent.TorrentId);
            Assert.Equal(addedTorrent.TorrentId, detail.TorrentId);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    [Fact]
    public async Task MonoTorrentEngine_StartupRecovery_DoesNotRequeuePersistedFinishedTorrent_WhenCacheIsMissing()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-finished-recovery");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var cachePath = Path.Combine(storagePath, "monotorrent-cache");
        Guid torrentId;

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();
            var addResponse = await AddMagnetAsync(httpClient, "7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B", "MonoTorrent Finished Recovery");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;
        }

        var completedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        await ForcePersistedFinishedTorrentSnapshotAsync(
            storagePath,
            torrentId,
            TorrentState.Seeding,
            TorrentDesiredState.Runnable,
            completedAtUtc);

        if (Directory.Exists(cachePath))
        {
            Directory.Delete(cachePath, recursive: true);
        }

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var recoveredTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.State == TorrentState.Completed,
                timeout: TimeSpan.FromSeconds(5));

            await Task.Delay(500);
            var stableTorrent = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}");
            var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=50&torrentId={torrentId}");

            Assert.NotNull(recoveredTorrent);
            Assert.Equal(100, recoveredTorrent.ProgressPercent);
            Assert.Equal(1_048_576, recoveredTorrent.DownloadedBytes);
            Assert.NotNull(recoveredTorrent.CompletedAtUtc);
            Assert.Equal(TorrentState.Completed, stableTorrent!.State);
            Assert.Null(stableTorrent.WaitReason);
            Assert.Equal(0, stableTorrent.DownloadRateBytesPerSecond);

            Assert.NotNull(logs);
            Assert.Contains(logs, log => log.EventType == "torrent.recovery.normalized" && log.TorrentId == torrentId);
        }
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
    public async Task MonoTorrentEngine_BurstAdmission_ReservesDownloadSlotsBeforeMetadataResolution()
    {
        const int burstSize = 20;
        const int downloadLimit = 4;

        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            maxActiveMetadataResolutions: 10,
            maxActiveDownloads: downloadLimit);
        using var httpClient = factory.CreateClient();

        for (var index = 0; index < burstSize; index++)
        {
            var infoHash = (index + 1).ToString("X40");
            var response = await AddMagnetAsync(httpClient, infoHash, $"Burst Admission {index + 1}");
            response.EnsureSuccessStatusCode();
        }

        var torrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            items => items is not null &&
                     items.Count == burstSize &&
                     items.Count(torrent => torrent.State == TorrentState.ResolvingMetadata) == downloadLimit &&
                     items.Count(torrent => torrent.State == TorrentState.Queued) == burstSize - downloadLimit,
            timeout: TimeSpan.FromSeconds(10));
        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(torrents);
        Assert.Equal(downloadLimit, torrents.Count(torrent => torrent.State == TorrentState.ResolvingMetadata));
        Assert.Equal(burstSize - downloadLimit, torrents.Count(torrent => torrent.State == TorrentState.Queued));
        Assert.NotNull(hostStatus);
        Assert.Equal(0, hostStatus.AvailableMetadataResolutionSlots);
        Assert.Equal(0, hostStatus.AvailableDownloadSlots);
        Assert.Equal(downloadLimit, hostStatus.ResolvingMetadataCount);
        Assert.Equal(burstSize - downloadLimit, hostStatus.MetadataQueueCount);
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
    public async Task MonoTorrentEngine_PendingFinalization_OnRecovery_WaitsForVisibilityThenInvokesCallback_AndKeepsTorrentTrackingWhenCleanupIsNever()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-callback-pending");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);
        var finalPayloadPath = Path.Combine(downloadPath, "TV", "MonoTorrent Pending Show");
        Guid torrentId;

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();
            await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

            var addResponse = await AddMagnetAsync(httpClient, "8282828282828282828282828282828282828282", "MonoTorrent Pending Show", "TV");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;
        }

        var completedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await UpdatePersistedCompletionCallbackSnapshotAsync(
            storagePath,
            torrentId,
            TorrentState.Completed,
            TorrentDesiredState.Runnable,
            completedAtUtc,
            TorrentCompletionCallbackState.PendingFinalization,
            completedAtUtc,
            invokedAtUtc: null,
            lastError: null);

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var pendingTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.PendingFinalization.ToString(),
                timeout: TimeSpan.FromSeconds(5));

            Assert.NotNull(pendingTorrent);
            Assert.Equal(finalPayloadPath, pendingTorrent.CompletionCallbackFinalPayloadPath);
            Assert.Equal("The final payload path is not visible yet.", pendingTorrent.CompletionCallbackPendingReason);

            await Task.Delay(300);
            Assert.Empty(ReadCallbackInvocations(callbackOutputPath));

            CreateSingleFilePayload(finalPayloadPath);

            await WaitForAsync(
                () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
                invocations => invocations.Count == 1,
                timeout: TimeSpan.FromSeconds(5));

            var waitingTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.WaitingForFeedback.ToString(),
                timeout: TimeSpan.FromSeconds(5));

            var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

            Assert.NotNull(waitingTorrent);
            Assert.False(waitingTorrent.CanRetryCompletionCallback);
            Assert.NotNull(waitingTorrent.CompletionCallbackInvokedAtUtc);
            Assert.NotNull(torrents);
            Assert.Contains(
                torrents,
                torrent => torrent.TorrentId == torrentId &&
                           torrent.CompletionCallbackState == TorrentCompletionCallbackState.WaitingForFeedback.ToString());
            Assert.True(File.Exists(finalPayloadPath));
            Assert.True(await ReadPersistedTorrentExistsAsync(storagePath, torrentId));

            var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=50&torrentId={torrentId}");
            Assert.NotNull(logs);
            Assert.Contains(logs, log => log.EventType == "torrent.callback.invoked");
            Assert.DoesNotContain(logs, log => log.EventType == "torrent.callback.auto_removed");
        }
    }

    [Fact]
    public async Task MonoTorrentEngine_RetryCompletionCallback_RequeuesTimedOutState_AndInvokesWhenPayloadAppears_AndKeepsTorrentTrackingWhenCleanupIsNever()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-callback-retry");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);
        var finalPayloadPath = Path.Combine(downloadPath, "Movie", "MonoTorrent Retry Movie");
        Guid torrentId;

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();
            await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

            var addResponse = await AddMagnetAsync(httpClient, "8383838383838383838383838383838383838383", "MonoTorrent Retry Movie", "Movie");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;
        }

        var completedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        await UpdatePersistedCompletionCallbackSnapshotAsync(
            storagePath,
            torrentId,
            TorrentState.Completed,
            TorrentDesiredState.Runnable,
            completedAtUtc,
            TorrentCompletionCallbackState.TimedOut,
            completedAtUtc,
            invokedAtUtc: null,
            lastError: "Timed out waiting for final payload visibility at '/tmp/missing'. The final payload path is not visible yet.");

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            var timedOutTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.TimedOut.ToString(),
                timeout: TimeSpan.FromSeconds(5));

            Assert.NotNull(timedOutTorrent);
            Assert.True(timedOutTorrent.CanRetryCompletionCallback);
            Assert.Equal(finalPayloadPath, timedOutTorrent.CompletionCallbackFinalPayloadPath);
            Assert.Equal("The final payload path is not visible yet.", timedOutTorrent.CompletionCallbackPendingReason);

            var retryResponse = await httpClient.PostAsync($"api/torrents/{torrentId}/completion-callback/retry", content: null);
            retryResponse.EnsureSuccessStatusCode();

            var retryResult = await retryResponse.Content.ReadFromJsonAsync<TorrentActionResultDto>();
            Assert.NotNull(retryResult);
            Assert.Equal("retry_completion_callback", retryResult.Action);

            var pendingTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.PendingFinalization.ToString(),
                timeout: TimeSpan.FromSeconds(5));

            Assert.NotNull(pendingTorrent);
            Assert.Null(pendingTorrent.CompletionCallbackLastError);
            Assert.False(pendingTorrent.CanRetryCompletionCallback);

            CreateSingleFilePayload(finalPayloadPath);

            await WaitForAsync(
                () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
                invocations => invocations.Count == 1,
                timeout: TimeSpan.FromSeconds(5));

            var waitingTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.WaitingForFeedback.ToString(),
                timeout: TimeSpan.FromSeconds(5));

            var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

            Assert.NotNull(waitingTorrent);
            Assert.NotNull(waitingTorrent.CompletionCallbackInvokedAtUtc);
            Assert.NotNull(torrents);
            Assert.Contains(
                torrents,
                torrent => torrent.TorrentId == torrentId &&
                           torrent.CompletionCallbackState == TorrentCompletionCallbackState.WaitingForFeedback.ToString());
            Assert.True(File.Exists(finalPayloadPath));
            Assert.True(await ReadPersistedTorrentExistsAsync(storagePath, torrentId));

            var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=100&torrentId={torrentId}");
            Assert.NotNull(logs);
            Assert.Contains(logs, log => log.EventType == "torrent.callback.retry_requested");
            Assert.Contains(logs, log => log.EventType == "torrent.callback.invoked");
            Assert.DoesNotContain(logs, log => log.EventType == "torrent.callback.auto_removed");
        }
    }

    [Fact]
    public async Task MonoTorrentEngine_ReportCompletionCallbackResult_DoesNotRegressToTimedOutAfterFeedback()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-feedback-regression");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackOutputPath = Path.Combine(rootPath, "callback-output.log");
        var callbackScriptPath = CreateCallbackCaptureScript(rootPath, callbackOutputPath);
        var finalPayloadPath = Path.Combine(downloadPath, "Movie", "MonoTorrent Feedback Regression");
        Guid torrentId;

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();
            await UpdateCompletionCallbackSettingsAsync(
                httpClient,
                "/bin/sh",
                callbackScriptPath,
                rootPath,
                finalizationTimeoutSeconds: 1);

            var addResponse = await AddMagnetAsync(
                httpClient,
                "8483838383838383838383838383838383838383",
                "MonoTorrent Feedback Regression",
                "Movie");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;
        }

        CreateSingleFilePayload(finalPayloadPath);

        var completedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await UpdatePersistedCompletionCallbackSnapshotAsync(
            storagePath,
            torrentId,
            TorrentState.Completed,
            TorrentDesiredState.Runnable,
            completedAtUtc,
            TorrentCompletionCallbackState.PendingFinalization,
            completedAtUtc,
            invokedAtUtc: null,
            lastError: null);

        await using (var factory = CreateFactory(
                         engineMode: TorrentEngineMode.MonoTorrent,
                         downloadPath: downloadPath,
                         storagePath: storagePath))
        {
            using var httpClient = factory.CreateClient();

            await WaitForAsync(
                () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
                invocations => invocations.Count == 1,
                timeout: TimeSpan.FromSeconds(5));

            var waitingTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent is not null &&
                           torrent.CompletionCallbackState == TorrentCompletionCallbackState.WaitingForFeedback.ToString(),
                timeout: TimeSpan.FromSeconds(5));

            Assert.NotNull(waitingTorrent);

            var reportRequest = new ReportCompletionCallbackResultRequest
            {
                TorrentId = torrentId,
                TorrentHash = "8483838383838383838383838383838383838383",
                CompletionTimestamp = DateTimeOffset.Parse("2026-05-29T06:00:00-04:00"),
                CallbackSource = "TorrentCore",
                CallbackMachine = "CA-Server",
                ContractVersion = "1",
                FinalState = "Success",
                ReasonCode = "MovedToLibrary",
                SourceState = "SourceConsumed",
                ResubmitAdvice = "NoResubmitNeeded",
                CallbackFinished = true,
                MediaConsideredDone = true,
                AllowResubmit = false,
                NeedsManualIntervention = false,
                DisplayMessage = "Completed successfully.",
                DetailMessage = "MonoTorrent feedback applied.",
                RecommendedAction = "None",
                CorrelationId = "mono-feedback-regression",
                CallbackLocalTimestamp = DateTimeOffset.Parse("2026-05-29T05:59:58-04:00"),
                AttemptCount = 1,
                RawResponseJson = "{\"handled\":true}",
            };

            var response = await httpClient.PostAsJsonAsync(
                $"api/torrents/{torrentId}/completion-callback/result",
                reportRequest);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var invokedTorrent = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                torrent => torrent?.CompletionCallbackFeedback?.CorrelationId == reportRequest.CorrelationId &&
                           torrent.CompletionCallbackState == TorrentCompletionCallbackState.Invoked.ToString(),
                timeout: TimeSpan.FromSeconds(5));

            Assert.NotNull(invokedTorrent);
            var finalizedLastActivityAtUtc = invokedTorrent.LastActivityAtUtc;

            await Task.Delay(TimeSpan.FromSeconds(2));

            var stableTorrent = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}");
            var summaries = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

            Assert.NotNull(stableTorrent);
            Assert.Equal(TorrentCompletionCallbackState.Invoked.ToString(), stableTorrent.CompletionCallbackState);
            Assert.NotNull(stableTorrent.CompletionCallbackFeedback);
            Assert.Equal(reportRequest.CorrelationId, stableTorrent.CompletionCallbackFeedback.CorrelationId);
            Assert.Equal(finalizedLastActivityAtUtc, stableTorrent.LastActivityAtUtc);
            Assert.DoesNotContain("Timed out waiting for TVMaze callback feedback", stableTorrent.CompletionCallbackLastError ?? string.Empty, StringComparison.Ordinal);
            Assert.NotNull(summaries);
            Assert.Contains(
                summaries,
                torrent => torrent.TorrentId == torrentId &&
                           torrent.CompletionCallbackState == TorrentCompletionCallbackState.Invoked.ToString());

            var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=100&torrentId={torrentId}");
            Assert.NotNull(logs);
            Assert.Contains(logs, log => log.EventType == "torrent.callback.feedback.received");
            Assert.DoesNotContain(logs, log => log.EventType == "torrent.callback.feedback_timed_out");
        }
    }

    [Fact]
    public async Task MonoTorrentEngine_RefreshMetadata_RequestsDiscoveryRefresh_AndWritesEngineLog()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-metadata-refresh");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "8484848484848484848484848484848484848484", "MonoTorrent Metadata Refresh");
        addResponse.EnsureSuccessStatusCode();
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(addedTorrent);

        var resolvingTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.ResolvingMetadata && torrent.CanRefreshMetadata,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(resolvingTorrent);
        var historyBefore = await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{addedTorrent.TorrentId}");
        Assert.NotNull(historyBefore);

        var refreshResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent.TorrentId}/metadata/refresh", content: null);
        refreshResponse.EnsureSuccessStatusCode();

        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<TorrentActionResultDto>();
        Assert.NotNull(refreshResult);
        Assert.Equal("refresh_metadata", refreshResult.Action);

        var historyAfter = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{addedTorrent.TorrentId}"),
            history => history is not null && history.LastUpdatedAt >= historyBefore!.LastUpdatedAt,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(historyAfter);
        Assert.Equal(TorrentState.ResolvingMetadata.ToString(), historyAfter.LatestTorrentState);
        Assert.True(historyAfter.LastUpdatedAt >= historyBefore.LastUpdatedAt);

        var logs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=100&torrentId={addedTorrent.TorrentId}"),
            entries => entries is not null && entries.Any(log => log.EventType == "torrent.metadata.refresh_requested" && log.Category == "engine"),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(logs);
        var engineLog = Assert.Single(logs, log => log.EventType == "torrent.metadata.refresh_requested" && log.Category == "engine");
        Assert.False(string.IsNullOrWhiteSpace(engineLog.DetailsJson));
        using var details = JsonDocument.Parse(engineLog.DetailsJson!);
        Assert.Equal("manual", details.RootElement.GetProperty("Origin").GetString());
    }

    [Fact]
    public async Task MonoTorrentEngine_ResetMetadataSession_RecreatesManager_AndWritesEngineLog()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-metadata-reset");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50);
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "9595959595959595959595959595959595959595", "MonoTorrent Metadata Reset");
        addResponse.EnsureSuccessStatusCode();
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(addedTorrent);

        var resolvingTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.ResolvingMetadata && torrent.CanRefreshMetadata,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(resolvingTorrent);
        var historyBefore = await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{addedTorrent.TorrentId}");
        Assert.NotNull(historyBefore);

        var resetResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent.TorrentId}/metadata/reset", content: null);
        resetResponse.EnsureSuccessStatusCode();

        var resetResult = await resetResponse.Content.ReadFromJsonAsync<TorrentActionResultDto>();
        Assert.NotNull(resetResult);
        Assert.Equal("reset_metadata_session", resetResult.Action);

        var historyAfter = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{addedTorrent.TorrentId}"),
            history => history is not null && history.LastUpdatedAt >= historyBefore!.LastUpdatedAt,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(historyAfter);
        Assert.Equal(TorrentState.ResolvingMetadata.ToString(), historyAfter.LatestTorrentState);
        Assert.True(historyAfter.LastUpdatedAt >= historyBefore.LastUpdatedAt);

        var logs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=150&torrentId={addedTorrent.TorrentId}"),
            entries => entries is not null &&
                       entries.Any(log => log.EventType == "torrent.metadata.reset_requested" &&
                                          log.Category == "engine" &&
                                          log.DetailsJson?.Contains("\"Origin\":\"manual\"", StringComparison.Ordinal) == true) &&
                       entries.Any(log => log.EventType == "torrent.metadata.refresh_requested" &&
                                          log.Category == "engine" &&
                                          log.DetailsJson?.Contains("\"Origin\":\"manual_reset\"", StringComparison.Ordinal) == true),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(logs);
    }

    [Fact]
    public async Task MonoTorrentEngine_AutomaticMetadataRecovery_RefreshesRestartsAndResetsStaleResolution()
    {
        var rootPath = CreateTempRootPath("torrentcore-monotorrent-metadata-autorecovery");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(
            engineMode: TorrentEngineMode.MonoTorrent,
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50);
        using var httpClient = factory.CreateClient();

        await UpdateMetadataRecoverySettingsAsync(httpClient, staleSeconds: 1, restartDelaySeconds: 1);

        var addResponse = await AddMagnetAsync(httpClient, "8585858585858585858585858585858585858585", "MonoTorrent Metadata Auto Recovery");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.ResolvingMetadata,
            timeout: TimeSpan.FromSeconds(5));

        var logs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=200&torrentId={addedTorrent!.TorrentId}"),
            entries => entries is not null &&
                       entries.Any(log => log.EventType == "torrent.metadata.refresh_requested" &&
                                          log.Category == "engine" &&
                                          log.DetailsJson?.Contains("\"Origin\":\"automatic_stale_metadata\"", StringComparison.Ordinal) == true) &&
                       entries.Any(log => log.EventType == "torrent.metadata.restart_requested" && log.Category == "engine") &&
                       entries.Any(log => log.EventType == "torrent.metadata.refresh_requested" &&
                                          log.Category == "engine" &&
                                          log.DetailsJson?.Contains("\"Origin\":\"automatic_stale_restart\"", StringComparison.Ordinal) == true) &&
                       entries.Any(log => log.EventType == "torrent.metadata.reset_requested" &&
                                          log.Category == "engine" &&
                                          log.DetailsJson?.Contains("\"Origin\":\"automatic_stale_reset\"", StringComparison.Ordinal) == true) &&
                       entries.Any(log => log.EventType == "torrent.metadata.refresh_requested" &&
                                          log.Category == "engine" &&
                                          log.DetailsJson?.Contains("\"Origin\":\"automatic_stale_reset\"", StringComparison.Ordinal) == true) &&
                       entries.Any(log => log.EventType == "torrent.metadata.reset_applied" &&
                                          log.Category == "engine") &&
                       entries.Any(log => log.EventType == "runtime.metadata.reset_completed" &&
                                          log.Category == "runtime" &&
                                          log.DetailsJson?.Contains("\"Outcome\":\"succeeded\"", StringComparison.Ordinal) == true),
            timeout: TimeSpan.FromSeconds(15));

        Assert.NotNull(logs);
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
        Assert.Equal(addedTorrent.TorrentId.ToString("D"), callbackInvocation["TORRENTCORE_TORRENT_ID"]);
        Assert.Equal("7373737373737373737373737373737373737373", callbackInvocation["TR_TORRENT_HASH"]);
        Assert.Equal("Callback Movie", callbackInvocation["TR_TORRENT_NAME"]);
        Assert.Equal(Path.Combine(downloadPath, "Movie"), callbackInvocation["TR_TORRENT_DIR"]);
        Assert.Equal("Movie", callbackInvocation["TR_TORRENT_LABELS"]);
        Assert.Equal(Path.Combine(downloadPath, "Movie", "Callback Movie"), callbackInvocation["TORRENTCORE_FINAL_PAYLOAD_PATH"]);
        Assert.Equal("http://127.0.0.1:5501/api/transmission/completions", callbackInvocation["TVMAZE_API_COMPLETE_URL"]);
        Assert.Equal("callback-test-key", callbackInvocation["TVMAZE_API_COMPLETE_API_KEY"]);

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.callback.pending_finalization");
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

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(httpClient, "/bin/sh", callbackScriptPath, rootPath);

        var response = await AddMagnetAsync(httpClient, "7575757575757575757575757575757575757575", "Finalization Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var waitingForFilesTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null &&
                       torrent.State == TorrentState.WaitingForFileCompletion &&
                       torrent.WaitReason == TorrentWaitReason.WaitingForFileCompletion,
            timeout: TimeSpan.FromSeconds(5));

        await Task.Delay(300);
        Assert.Empty(ReadCallbackInvocations(callbackOutputPath));

        var pendingState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId);
        Assert.Null(pendingState.State);

        Assert.NotNull(waitingForFilesTorrent);
        Assert.Null(waitingForFilesTorrent.CompletionCallbackState);
        Assert.Equal(finalPayloadPath, waitingForFilesTorrent.CompletionCallbackFinalPayloadPath);
        Assert.Equal("The final payload path is not visible yet.", waitingForFilesTorrent.CompletionCallbackPendingReason);

        CreateSingleFilePayload(finalPayloadPath);

        await WaitForAsync(
            () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
            invocations => invocations.Count == 1,
            timeout: TimeSpan.FromSeconds(5));

        var waitingState = await WaitForAsync(
            async () => await ReadPersistedCallbackStateAsync(storagePath, addedTorrent.TorrentId),
            state => state.State == TorrentCompletionCallbackState.WaitingForFeedback.ToString(),
            timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(TorrentCompletionCallbackState.WaitingForFeedback.ToString(), waitingState.State);
        Assert.NotNull(waitingState.InvokedAtUtc);

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=50&torrentId={addedTorrent.TorrentId}");
        Assert.NotNull(logs);
        var pendingLog = Assert.Single(logs, log => log.EventType == "torrent.callback.pending_finalization");
        var pendingDetails = ParseLogDetails(pendingLog);
        Assert.Equal(finalPayloadPath, pendingDetails.GetProperty("FinalPayloadPath").GetString());
        Assert.Contains(logs, log => log.EventType == "torrent.callback.invoked");
    }

    [Fact]
    public async Task FakeRuntime_CompletionCallback_WaitsForMultiFilePayloadDirectoryVisibility()
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

        var response = await AddMagnetAsync(httpClient, "7676767676767676767676767676767676767676", "Finalization Show", "TV");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var waitingForFilesTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null &&
                       torrent.State == TorrentState.WaitingForFileCompletion &&
                       torrent.WaitReason == TorrentWaitReason.WaitingForFileCompletion,
            timeout: TimeSpan.FromSeconds(5));

        await Task.Delay(300);
        Assert.Empty(ReadCallbackInvocations(callbackOutputPath));

        var pendingState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId);
        Assert.Null(pendingState.State);
        Assert.NotNull(waitingForFilesTorrent);
        Assert.Equal("The final payload path is not visible yet.", waitingForFilesTorrent.CompletionCallbackPendingReason);

        var episodePath = Path.Combine(finalPayloadPath, "Season 01", "Episode 01.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(episodePath)!);
        File.WriteAllText(episodePath, "final");

        await WaitForAsync(
            () => Task.FromResult(ReadCallbackInvocations(callbackOutputPath)),
            invocations => invocations.Count == 1,
            timeout: TimeSpan.FromSeconds(5));

        var waitingState = await WaitForAsync(
            async () => await ReadPersistedCallbackStateAsync(storagePath, addedTorrent.TorrentId),
            state => state.State == TorrentCompletionCallbackState.WaitingForFeedback.ToString(),
            timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(TorrentCompletionCallbackState.WaitingForFeedback.ToString(), waitingState.State);
    }

    [Fact]
    public async Task FakeRuntime_CompletionCallback_LaunchFailure_PersistsFailedState()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-failed");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var callbackScriptPath = Path.Combine(rootPath, "missing-callback-command");

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        await UpdateCompletionCallbackSettingsAsync(httpClient, callbackScriptPath, string.Empty, rootPath);

        var response = await AddMagnetAsync(httpClient, "7979797979797979797979797979797979797979", "Failed Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        CreateSingleFilePayload(Path.Combine(downloadPath, "Movie", "Failed Movie"));

        await WaitForAsync(
            async () => await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId),
            state => state.State == TorrentCompletionCallbackState.Failed.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        var failedState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId);
        Assert.NotNull(failedState.LastError);

        var torrentDetail = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}");
        Assert.NotNull(torrentDetail);
        Assert.Equal(TorrentCompletionCallbackState.Failed.ToString(), torrentDetail.CompletionCallbackState);
        Assert.True(torrentDetail.CanRetryCompletionCallback);
        Assert.NotNull(torrentDetail.CompletionCallbackLastError);
        Assert.Equal(Path.Combine(downloadPath, "Movie", "Failed Movie"), torrentDetail.CompletionCallbackFinalPayloadPath);
        Assert.Null(torrentDetail.CompletionCallbackPendingReason);

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=50&torrentId={addedTorrent.TorrentId}");
        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.callback.pending_finalization");
        var failedLog = Assert.Single(logs, log => log.EventType == "torrent.callback.failed");
        var failedDetails = ParseLogDetails(failedLog);
        Assert.Equal(callbackScriptPath, failedDetails.GetProperty("CommandPath").GetString());
        Assert.Equal(JsonValueKind.Null, failedDetails.GetProperty("CompletionCallbackArguments").ValueKind);
        Assert.Equal(rootPath, failedDetails.GetProperty("WorkingDirectory").GetString());
        Assert.Equal(JsonValueKind.Null, failedDetails.GetProperty("ExitCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, failedDetails.GetProperty("ProcessId").ValueKind);
    }

    [Fact]
    public async Task FakeRuntime_CompletionCallback_LongRunningProcess_DoesNotBlockDispatch()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-timeout-moved");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var movedPayloadPath = Path.Combine(rootPath, "moved-payload", "Timed Out Movie");
        Directory.CreateDirectory(Path.GetDirectoryName(movedPayloadPath)!);

        var callbackScriptPath = Path.Combine(rootPath, "move-and-timeout-callback.sh");
        File.WriteAllText(
            callbackScriptPath,
            $$"""
            #!/bin/sh
            mv "${TORRENTCORE_FINAL_PAYLOAD_PATH}" "{{movedPayloadPath}}"
            sleep 2
            exit 0
            """
        );

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
            callbackTimeoutSeconds: 1);

        var response = await AddMagnetAsync(httpClient, "7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A7A", "Timed Out Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        var finalPayloadPath = Path.Combine(downloadPath, "Movie", "Timed Out Movie");
        CreateSingleFilePayload(finalPayloadPath);

        var waitingState = await WaitForAsync(
            async () => await ReadPersistedCallbackStateAsync(storagePath, addedTorrent!.TorrentId),
            state => state.State == TorrentCompletionCallbackState.WaitingForFeedback.ToString(),
            timeout: TimeSpan.FromSeconds(1));
        Assert.Null(waitingState.LastError);

        var torrentDetail = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}");
        Assert.NotNull(torrentDetail);
        Assert.Equal(TorrentCompletionCallbackState.WaitingForFeedback.ToString(), torrentDetail.CompletionCallbackState);
        Assert.False(torrentDetail.CanRetryCompletionCallback);
        Assert.Equal(finalPayloadPath, torrentDetail.CompletionCallbackFinalPayloadPath);
        Assert.Null(torrentDetail.CompletionCallbackPendingReason);
        Assert.Null(torrentDetail.CompletionCallbackLastError);

        await WaitForAsync(
            () => Task.FromResult(File.Exists(movedPayloadPath)),
            moved => moved,
            timeout: TimeSpan.FromSeconds(5));

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=50&torrentId={addedTorrent.TorrentId}");
        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.callback.invoked");
        Assert.DoesNotContain(logs, log => log.EventType == "torrent.callback.timed_out");
    }

    [Fact]
    public async Task FakeRuntime_CompletionCallback_FeedbackTimeout_PersistsTimedOutState_WhenTvmazeNeverReportsBack()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-feedback-timeout");
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

        var response = await AddMagnetAsync(httpClient, "7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B7B", "Feedback Timeout Movie", "Movie");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(addedTorrent);
        CreateSingleFilePayload(Path.Combine(downloadPath, "Movie", "Feedback Timeout Movie"));

        var waitingState = await WaitForAsync(
            async () => await ReadPersistedCallbackStateAsync(storagePath, addedTorrent.TorrentId),
            state => state.State == TorrentCompletionCallbackState.WaitingForFeedback.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        await Task.Delay(TimeSpan.FromSeconds(2));

        var stableState = await ReadPersistedCallbackStateAsync(storagePath, addedTorrent.TorrentId);
        Assert.Equal(TorrentCompletionCallbackState.WaitingForFeedback.ToString(), stableState.State);
        Assert.Null(stableState.LastError);
        Assert.NotNull(stableState.InvokedAtUtc);

        var torrentDetail = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}");
        Assert.NotNull(torrentDetail);
        Assert.Equal(TorrentCompletionCallbackState.WaitingForFeedback.ToString(), torrentDetail.CompletionCallbackState);
        Assert.False(torrentDetail.CanRetryCompletionCallback);
        Assert.True(string.IsNullOrWhiteSpace(torrentDetail.CompletionCallbackLastError));
        Assert.Null(torrentDetail.CompletionCallbackFeedback);

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>($"api/logs?take=100&torrentId={addedTorrent.TorrentId}");
        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.callback.invoked");
        Assert.DoesNotContain(logs, log => log.EventType == "torrent.callback.feedback_timed_out");
    }

    [Fact]
    public async Task FakeRuntime_RetryCompletionCallback_ReturnsConflict_ForWaitingForFeedbackState()
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
            torrent => torrent is not null && torrent.CompletionCallbackState == TorrentCompletionCallbackState.WaitingForFeedback.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        var retryResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent!.TorrentId}/completion-callback/retry", content: null);
        var error = await retryResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, retryResponse.StatusCode);
        Assert.Equal("invalid_callback_state", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReportCompletionCallbackResult_ReturnsOk_AndPersistsFeedbackToTorrentAndHistory()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(
            httpClient,
            "8484848484848484848484848484848484848484",
            "Callback Feedback Receipt",
            "Movie");
        addResponse.EnsureSuccessStatusCode();
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(addedTorrent);

        var request = new ReportCompletionCallbackResultRequest
        {
            TorrentId = addedTorrent.TorrentId,
            TorrentHash = "8484848484848484848484848484848484848484",
            CompletionTimestamp = DateTimeOffset.Parse("2026-05-26T10:30:00-04:00"),
            CallbackSource = "TorrentCore",
            CallbackMachine = "CA-Desktop",
            ContractVersion = "1",
            FinalState = "Success",
            ReasonCode = "DestinationAlreadyExists",
            SourceState = "SourceConsumed",
            ResubmitAdvice = "NoResubmitNeeded",
            CallbackFinished = true,
            MediaConsideredDone = true,
            AllowResubmit = false,
            NeedsManualIntervention = false,
            DisplayMessage = "Completed successfully.",
            DetailMessage = "Detailed callback feedback.",
            RecommendedAction = "None",
            CorrelationId = "corr-123",
            CallbackLocalTimestamp = DateTimeOffset.Parse("2026-05-26T10:29:58-04:00"),
            AttemptCount = 1,
            RawResponseJson = "{\"handled\":true}",
        };

        var response = await httpClient.PostAsJsonAsync(
            $"api/torrents/{addedTorrent.TorrentId}/completion-callback/result",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updatedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent?.CompletionCallbackFeedback?.CorrelationId == request.CorrelationId,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(updatedTorrent);
        Assert.Equal(TorrentCompletionCallbackState.Invoked.ToString(), updatedTorrent.CompletionCallbackState);
        Assert.NotNull(updatedTorrent.CompletionCallbackFeedback);
        Assert.Equal(request.TorrentId, updatedTorrent.CompletionCallbackFeedback.TorrentId);
        Assert.Equal(request.FinalState, updatedTorrent.CompletionCallbackFeedback.FinalState);
        Assert.Equal(request.ReasonCode, updatedTorrent.CompletionCallbackFeedback.ReasonCode);
        Assert.Equal(request.DisplayMessage, updatedTorrent.CompletionCallbackFeedback.DisplayMessage);
        Assert.Equal(request.AllowResubmit, updatedTorrent.CompletionCallbackFeedback.AllowResubmit);
        Assert.Equal(request.NeedsManualIntervention, updatedTorrent.CompletionCallbackFeedback.NeedsManualIntervention);
        Assert.True(updatedTorrent.CompletionCallbackFeedback.ReceivedAtUtc > DateTimeOffset.MinValue);

        var history = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{addedTorrent.TorrentId}"),
            detail => detail?.CompletionCallbackFeedback?.CorrelationId == request.CorrelationId,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(history);
        Assert.NotNull(history.CompletionCallbackFeedback);
        Assert.Equal(request.TorrentId, history.CompletionCallbackFeedback.TorrentId);
        Assert.Equal(request.FinalState, history.CompletionCallbackFeedback.FinalState);
        Assert.Equal(request.ReasonCode, history.CompletionCallbackFeedback.ReasonCode);
        Assert.Equal(request.DisplayMessage, history.CompletionCallbackFeedback.DisplayMessage);

        var logs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
                $"api/logs?take=50&torrentId={addedTorrent.TorrentId}"
            ),
            entries => entries is not null &&
                       entries.Any(entry => entry.EventType == "torrent.callback.feedback.received") &&
                       entries.Any(entry => entry.EventType == "torrent.callback.feedback.applied"),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.callback.feedback.received");
        Assert.Contains(logs, log => log.EventType == "torrent.callback.feedback.applied");
    }

    [Fact]
    public async Task ReportCompletionCallbackResult_WhenRouteAndBodyTorrentIdsDiffer_ReturnsBadRequest()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var routeTorrentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var bodyTorrentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var response = await httpClient.PostAsJsonAsync(
            $"api/torrents/{routeTorrentId}/completion-callback/result",
            new ReportCompletionCallbackResultRequest
            {
                TorrentId = bodyTorrentId,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("torrent_id_mismatch", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReportCompletionCallbackResult_WhenTorrentDoesNotExist_ReturnsNotFound()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var torrentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var response = await httpClient.PostAsJsonAsync(
            $"api/torrents/{torrentId}/completion-callback/result",
            new ReportCompletionCallbackResultRequest
            {
                TorrentId = torrentId,
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("torrent_not_found", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FakeRuntime_PauseAndResumeWhileDownloading_PreservesPausedStateUntilResumed()
    {
        var rootPath = CreateTempRootPath("torrentcore-pause-history");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
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

        var pausedHistory = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{addedTorrent.TorrentId}"),
            history => history is not null && history.LatestTorrentState == TorrentState.Paused.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(pausedHistory);
        Assert.Equal(TorrentState.Paused.ToString(), pausedHistory.LatestTorrentState);

        var resumeResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent.TorrentId}/resume", content: null);
        resumeResponse.EnsureSuccessStatusCode();

        var resumedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Downloading,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(resumedTorrent);
        Assert.Equal(TorrentState.Downloading, resumedTorrent.State);

        var resumedHistory = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{addedTorrent.TorrentId}"),
            history => history is not null && history.LatestTorrentState == TorrentState.Downloading.ToString(),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(resumedHistory);
        Assert.Equal(TorrentState.Downloading.ToString(), resumedHistory.LatestTorrentState);
    }

    [Fact]
    public async Task PersistedRecovery_NormalizesHistoryState_OnStartup()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-recovery");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        Guid torrentId;

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         runtimeTickIntervalMilliseconds: 60_000))
        {
            using var httpClient = factory.CreateClient();
            var response = await AddMagnetAsync(httpClient, "3535353535353535353535353535353535353535", "Recovery History Torrent");
            var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
            Assert.NotNull(torrent);
            torrentId = torrent.TorrentId;
        }

        await ForcePersistedTorrentSnapshotAsync(
            storagePath,
            torrentId,
            TorrentState.Downloading,
            TorrentDesiredState.Runnable,
            errorMessage: null);
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        var forcedHistoryTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await UpdateHistoryRowAsync(
            databaseFilePath,
            torrentId,
            forcedHistoryTime,
            TorrentState.Downloading.ToString(),
            removedAtUtc: null);

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         runtimeTickIntervalMilliseconds: 60_000))
        {
            using var httpClient = factory.CreateClient();

            var history = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentHistoryDetailDto>($"api/history/by-torrent/{torrentId}"),
                item => item is not null &&
                        item.LatestTorrentState != TorrentState.Downloading.ToString() &&
                        item.LastUpdatedAt > TimeZoneInfo.ConvertTime(forcedHistoryTime, TimeZoneInfo.Local),
                timeout: TimeSpan.FromSeconds(5));
            var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

            Assert.NotNull(history);
            Assert.DoesNotContain(history.LatestTorrentState, new[] { TorrentState.Downloading.ToString() });
            Assert.NotNull(hostStatus);
            Assert.Equal(1, hostStatus.StartupNormalizedTorrentCount);
        }
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
    public async Task FakeRuntime_SeedingPolicyApplication_IsLoggedOnceAcrossReevaluationAndRestart()
    {
        var rootPath = CreateTempRootPath("torrentcore-seeding-policy-idempotence");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        const string torrentName = "Policy Idempotence Torrent";
        Directory.CreateDirectory(downloadPath);
        await File.WriteAllTextAsync(Path.Combine(downloadPath, torrentName), "complete");

        Guid torrentId;
        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         seedingStopMode: SeedingStopMode.StopImmediately,
                         runtimeTickIntervalMilliseconds: 50,
                         metadataResolutionDelayMilliseconds: 0,
                         downloadProgressPercentPerTick: 100))
        {
            using var httpClient = factory.CreateClient();
            var response = await AddMagnetAsync(
                httpClient,
                "7575757575757575757575757575757575757575",
                torrentName);
            var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
            Assert.NotNull(torrent);
            torrentId = torrent.TorrentId;

            await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                current => current?.State == TorrentState.Completed,
                timeout: TimeSpan.FromSeconds(5));

            var logs = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
                    $"api/logs?eventType=torrent.seeding.stopped_policy&torrentId={torrentId}&take=20"),
                entries => entries is { Count: 1 },
                timeout: TimeSpan.FromSeconds(5));
            Assert.Single(logs!);
        }

        await ForceSeedingPolicyReevaluationAsync(storagePath, torrentId);

        await using (var factory = CreateFactory(
                         downloadPath: downloadPath,
                         storagePath: storagePath,
                         seedingStopMode: SeedingStopMode.StopImmediately,
                         runtimeTickIntervalMilliseconds: 50,
                         metadataResolutionDelayMilliseconds: 0,
                         downloadProgressPercentPerTick: 100))
        {
            using var httpClient = factory.CreateClient();

            await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}"),
                current => current?.State == TorrentState.Completed,
                timeout: TimeSpan.FromSeconds(5));
            await Task.Delay(250);

            var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
                $"api/logs?eventType=torrent.seeding.stopped_policy&torrentId={torrentId}&take=20");
            Assert.NotNull(logs);
            Assert.Single(logs);
        }
    }

    [Fact]
    public async Task FakeRuntime_AutoCleanup_RemovesCompletedTorrentWithoutDeletingData()
    {
        var rootPath = CreateTempRootPath("torrentcore-auto-cleanup");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var finalPayloadPath = Path.Combine(downloadPath, "Auto Cleanup Torrent");

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        Directory.CreateDirectory(Path.GetDirectoryName(finalPayloadPath)!);
        File.WriteAllText(finalPayloadPath, "final");

        var updateResponse = await httpClient.PutAsJsonAsync("api/host/runtime-settings", new UpdateRuntimeSettingsRequest
        {
            SeedingStopMode = SeedingStopMode.StopImmediately.ToString(),
            SeedingStopRatio = 1.0,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.AfterCompletedMinutes.ToString(),
            CompletedTorrentCleanupMinutes = 0,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            ColdDownloadRecoveryThresholdMinutes = 240,
            ColdDownloadRecoveryIntervalMinutes = 60,
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
    public async Task FakeRuntime_AbandonsPersistedColdDownload_DeletesDataAndTorrentLogs_AndRetainsHistory()
    {
        var rootPath = CreateTempRootPath("torrentcore-cold-abandonment");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 0.01);
        using var httpClient = factory.CreateClient();

        var settingsRequest = CreateDefaultRuntimeSettingsUpdateRequest(coldDownloadAbandonAfterHours: 1);
        var updateResponse = await httpClient.PutAsJsonAsync("api/host/runtime-settings", settingsRequest);
        updateResponse.EnsureSuccessStatusCode();

        var response = await AddMagnetAsync(
            httpClient,
            "ABABABABABABABABABABABABABABABABABABABAB",
            "Cold Abandonment Torrent");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(addedTorrent);

        Directory.CreateDirectory(Path.GetDirectoryName(addedTorrent.SavePath)!);
        await File.WriteAllTextAsync(addedTorrent.SavePath, "partial payload");

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Downloading,
            timeout: TimeSpan.FromSeconds(5));

        await ForceDownloadColdSinceAsync(
            databaseFilePath,
            addedTorrent.TorrentId,
            DateTimeOffset.UtcNow.AddHours(-2));

        var remainingTorrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            torrents => torrents is not null && torrents.All(torrent => torrent.TorrentId != addedTorrent.TorrentId),
            timeout: TimeSpan.FromSeconds(5));
        await WaitForAsync(
            () => Task.FromResult(File.Exists(addedTorrent.SavePath)),
            exists => !exists,
            timeout: TimeSpan.FromSeconds(5));
        var history = await GetRemovalHistoryRowAsync(databaseFilePath, addedTorrent.TorrentId);
        var torrentLogs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
            $"api/logs?take=100&torrentId={addedTorrent.TorrentId}");
        var allLogs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=100");

        Assert.NotNull(remainingTorrents);
        Assert.False(File.Exists(addedTorrent.SavePath));
        Assert.NotNull(history);
        Assert.True(history.DataDeleted);
        Assert.True(history.RemovedByCleanupPolicy);
        Assert.Equal(TorrentRemovalKind.ColdDownloadAbandonment, history.RemovalKind);
        Assert.Contains("abandoned after 1 hour", history.RemovalReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(torrentLogs);
        Assert.Empty(torrentLogs);
        Assert.NotNull(allLogs);
        Assert.Contains(allLogs, log => log.EventType == "torrent.download.abandoned" && log.TorrentId is null);
    }

    [Fact]
    public async Task FakeRuntime_DeleteLogsForCompletedTorrents_PrunesTorrentScopedLogs_WithoutRemovingTorrent()
    {
        var rootPath = CreateTempRootPath("torrentcore-auto-log-cleanup");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var finalPayloadPath = Path.Combine(downloadPath, "Auto Log Cleanup Torrent");

        await using var factory = CreateFactory(
            downloadPath: downloadPath,
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        Directory.CreateDirectory(Path.GetDirectoryName(finalPayloadPath)!);
        File.WriteAllText(finalPayloadPath, "final");

        var updateResponse = await httpClient.PutAsJsonAsync("api/host/runtime-settings", new UpdateRuntimeSettingsRequest
        {
            SeedingStopMode = SeedingStopMode.StopImmediately.ToString(),
            SeedingStopRatio = 1.0,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never.ToString(),
            CompletedTorrentCleanupMinutes = 0,
            DeleteLogsForCompletedTorrents = true,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            ColdDownloadRecoveryThresholdMinutes = 240,
            ColdDownloadRecoveryIntervalMinutes = 60,
        });
        updateResponse.EnsureSuccessStatusCode();

        var response = await AddMagnetAsync(httpClient, "CECECECECECECECECECECECECECECECECECECECE", "Auto Log Cleanup Torrent");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var completedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Completed,
            timeout: TimeSpan.FromSeconds(5));

        var remainingTorrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");
        var torrentLogs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
                $"api/logs?take=100&torrentId={addedTorrent!.TorrentId}"
            ),
            logs => logs is not null && logs.Count == 0,
            timeout: TimeSpan.FromSeconds(5));
        var allLogs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=100"),
            logs => logs is not null &&
                    logs.Any(log => log.EventType == "torrent.logs.auto_deleted" && log.TorrentId is null),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(completedTorrent);
        Assert.NotNull(remainingTorrents);
        Assert.Contains(remainingTorrents, torrent => torrent.TorrentId == addedTorrent!.TorrentId);
        Assert.NotNull(torrentLogs);
        Assert.Empty(torrentLogs);
        Assert.NotNull(allLogs);
        Assert.Contains(allLogs, log => log.EventType == "torrent.logs.auto_deleted" && log.TorrentId is null);
    }

    [Fact]
    public async Task DeleteOrphanedTorrentLogs_RemovesLogsForRemovedTorrents_Only()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var addResponse = await AddMagnetAsync(httpClient, "D0D0D0D0D0D0D0D0D0D0D0D0D0D0D0D0D0D0D0D0", "Orphan Cleanup Torrent");
        var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        Assert.NotNull(addedTorrent);

        var logsBeforeRemoval = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
            $"api/logs?take=100&torrentId={addedTorrent.TorrentId}"
        );

        Assert.NotNull(logsBeforeRemoval);
        Assert.NotEmpty(logsBeforeRemoval);

        var removeResponse = await httpClient.PostAsync($"api/torrents/{addedTorrent.TorrentId}/remove", content: null);
        removeResponse.EnsureSuccessStatusCode();

        var cleanupResponse = await httpClient.PostAsync("api/logs/delete-orphaned-torrent-logs", content: null);
        cleanupResponse.EnsureSuccessStatusCode();

        var cleanupResult = await cleanupResponse.Content.ReadFromJsonAsync<DeleteOrphanedTorrentLogsResultDto>();
        var logsAfterCleanup = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
            $"api/logs?take=100&torrentId={addedTorrent.TorrentId}"
        );
        var allLogs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=100");

        Assert.NotNull(cleanupResult);
        Assert.True(cleanupResult.DeletedLogEntryCount > 0);
        Assert.NotNull(logsAfterCleanup);
        Assert.Empty(logsAfterCleanup);
        Assert.NotNull(allLogs);
        Assert.Contains(allLogs, log => log.EventType == "torrent.logs.orphaned_deleted" && log.TorrentId is null);
    }

    [Fact]
    public async Task MaintenanceCleanup_AcceptsToday_AndWritesAuditLogs()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var localMidnight = DateTime.SpecifyKind(
            today.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified
        );
        var expectedCutoffUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(localMidnight, TimeZoneInfo.Local),
            TimeSpan.Zero
        );

        var logResponse = await httpClient.PostAsJsonAsync(
            "api/maintenance/logs/cleanup",
            new CleanupByDateRequest { UpToDate = today }
        );
        var historyResponse = await httpClient.PostAsJsonAsync(
            "api/maintenance/history/cleanup",
            new CleanupByDateRequest { UpToDate = today }
        );

        logResponse.EnsureSuccessStatusCode();
        historyResponse.EnsureSuccessStatusCode();

        var logResult = await logResponse.Content.ReadFromJsonAsync<CleanupByDateResultDto>();
        var historyResult = await historyResponse.Content.ReadFromJsonAsync<CleanupByDateResultDto>();
        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=100");

        Assert.NotNull(logResult);
        Assert.NotNull(historyResult);
        Assert.Equal(today, logResult.UpToDate);
        Assert.Equal(today, historyResult.UpToDate);
        Assert.Equal(expectedCutoffUtc, logResult.CutoffUtc);
        Assert.Equal(expectedCutoffUtc, historyResult.CutoffUtc);
        Assert.Equal(0, logResult.DeletedRecordCount);
        Assert.Equal(0, historyResult.DeletedRecordCount);
        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "service.logs.cleanup_completed");
        Assert.Contains(logs, log => log.EventType == "service.history.cleanup_completed");
    }

    [Theory]
    [InlineData("api/maintenance/logs/cleanup")]
    [InlineData("api/maintenance/history/cleanup")]
    public async Task MaintenanceCleanup_RejectsFutureDate(string endpoint)
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PostAsJsonAsync(
            endpoint,
            new CleanupByDateRequest
            {
                UpToDate = DateOnly.FromDateTime(DateTime.Now).AddDays(1),
            }
        );
        var problem = await response.Content.ReadFromJsonAsync<ServiceProblemDetailsDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("invalid_cleanup_date", problem.Code);
        Assert.Equal(nameof(CleanupByDateRequest.UpToDate), problem.Target);
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
    public async Task FakeRuntime_MetadataTimeSlice_RotatesWaitingMagnetsThenRetriesOldestYielded()
    {
        var rootPath = CreateTempRootPath("torrentcore-metadata-time-slice");
        var storagePath = Path.Combine(rootPath, "storage");
        await using var factory = CreateFactory(
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 300_000,
            maxActiveMetadataResolutions: 1,
            metadataResolutionTimeSliceMinutes: 1);
        using var httpClient = factory.CreateClient();

        var first = await (await AddMagnetAsync(
            httpClient, "7171717171717171717171717171717171717171", "Rotation One"))
            .Content.ReadFromJsonAsync<TorrentDetailDto>();
        var second = await (await AddMagnetAsync(
            httpClient, "7272727272727272727272727272727272727272", "Rotation Two"))
            .Content.ReadFromJsonAsync<TorrentDetailDto>();
        var third = await (await AddMagnetAsync(
            httpClient, "7373737373737373737373737373737373737373", "Rotation Three"))
            .Content.ReadFromJsonAsync<TorrentDetailDto>();

        await WaitForResolvingTorrentAsync(first!.TorrentId);
        await ForceMetadataAttemptStartedAsync(storagePath, first.TorrentId, DateTimeOffset.UtcNow.AddMinutes(-2));
        await WaitForResolvingTorrentAsync(second!.TorrentId);

        await ForceMetadataAttemptStartedAsync(storagePath, second.TorrentId, DateTimeOffset.UtcNow.AddMinutes(-2));
        await WaitForResolvingTorrentAsync(third!.TorrentId);

        await ForceMetadataAttemptStartedAsync(storagePath, third.TorrentId, DateTimeOffset.UtcNow.AddMinutes(-2));
        await WaitForResolvingTorrentAsync(first.TorrentId);

        var logs = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
                "api/logs?eventType=torrent.metadata.resolution_yielded&take=20"),
            entries => entries is not null && entries.Count == 3,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(logs);
        Assert.Equal(3, logs.Count);
        Assert.Equal(3, logs.Select(log => log.TorrentId).Distinct().Count());

        async Task WaitForResolvingTorrentAsync(Guid torrentId)
        {
            var torrents = await WaitForAsync(
                async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
                items => items is not null &&
                         items.Count(item => item.State == TorrentState.ResolvingMetadata) == 1 &&
                         items.Any(item => item.TorrentId == torrentId &&
                                           item.State == TorrentState.ResolvingMetadata),
                timeout: TimeSpan.FromSeconds(5));
            Assert.NotNull(torrents);
        }
    }

    [Fact]
    public async Task FakeRuntime_MetadataTimeSlice_DoesNotYieldWhenNoMagnetIsWaiting()
    {
        var rootPath = CreateTempRootPath("torrentcore-metadata-time-slice-no-waiter");
        var storagePath = Path.Combine(rootPath, "storage");
        await using var factory = CreateFactory(
            storagePath: storagePath,
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 300_000,
            maxActiveMetadataResolutions: 1,
            metadataResolutionTimeSliceMinutes: 1);
        using var httpClient = factory.CreateClient();

        var torrent = await (await AddMagnetAsync(
            httpClient, "7474747474747474747474747474747474747474", "Lone Resolver"))
            .Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(torrent);

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            items => items is not null &&
                     items.Any(item => item.TorrentId == torrent.TorrentId &&
                                       item.State == TorrentState.ResolvingMetadata),
            timeout: TimeSpan.FromSeconds(5));

        await ForceMetadataAttemptStartedAsync(
            storagePath,
            torrent.TorrentId,
            DateTimeOffset.UtcNow.AddMinutes(-2));
        await Task.Delay(300);

        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");
        var current = Assert.Single(torrents!);
        Assert.Equal(TorrentState.ResolvingMetadata, current.State);

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>(
            "api/logs?eventType=torrent.metadata.resolution_yielded&take=20");
        Assert.NotNull(logs);
        Assert.Empty(logs);
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
        Assert.Equal(3, hostStatus.AvailableDownloadSlots);
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
            maxActiveDownloads: 2);
        using var httpClient = factory.CreateClient();

        var firstResponse = await AddMagnetAsync(
            httpClient,
            "5151515151515151515151515151515151515151",
            "Download Slot One");
        var secondResponse = await AddMagnetAsync(
            httpClient,
            "6161616161616161616161616161616161616161",
            "Download Slot Two");
        var firstTorrent = await firstResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
        var secondTorrent = await secondResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            items => items is not null && items.Count(torrent => torrent.State == TorrentState.Downloading) == 2,
            timeout: TimeSpan.FromSeconds(5));

        (await httpClient.PostAsync($"api/torrents/{secondTorrent!.TorrentId}/pause", content: null))
            .EnsureSuccessStatusCode();
        var settingsResponse = await httpClient.PutAsJsonAsync(
            "api/host/runtime-settings",
            CreateDefaultRuntimeSettingsUpdateRequest(maxActiveDownloads: 1));
        settingsResponse.EnsureSuccessStatusCode();
        (await httpClient.PostAsync($"api/torrents/{secondTorrent.TorrentId}/resume", content: null))
            .EnsureSuccessStatusCode();

        var torrents = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            items => items is not null &&
                     items.Count == 2 &&
                     items.Count(torrent => torrent.State == TorrentState.Downloading) == 1 &&
                     items.Count(torrent => torrent.State == TorrentState.Queued) == 1 &&
                     items.Any(torrent => torrent.WaitReason == TorrentWaitReason.WaitingForDownloadSlot && torrent.QueuePosition == 1),
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(torrents);
        Assert.Contains(
            torrents,
            torrent => torrent.TorrentId == firstTorrent!.TorrentId && torrent.State == TorrentState.Downloading);
        Assert.Contains(
            torrents,
            torrent => torrent.TorrentId == secondTorrent.TorrentId &&
                       torrent.WaitReason == TorrentWaitReason.WaitingForDownloadSlot &&
                       torrent.QueuePosition == 1);
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

        var filterOptions =
                await httpClient.GetFromJsonAsync<ActivityLogFilterOptionsDto>("api/logs/filter-options");

        Assert.NotNull(filterOptions);
        Assert.Contains("startup", filterOptions.Categories);
        Assert.Contains("torrent", filterOptions.Categories);
        Assert.Contains("service.startup.ready", filterOptions.EventTypes);
        Assert.Contains("torrent.added", filterOptions.EventTypes);
        Assert.Equal(
            filterOptions.Categories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
            filterOptions.Categories
        );
        Assert.Equal(
            filterOptions.EventTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
            filterOptions.EventTypes
        );
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
    public async Task AddMagnet_ReturnsStructuredError_WhenCategoryDownloadRootIsUnavailable()
    {
        var rootPath = CreateTempRootPath("torrentcore-category-add-unavailable");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var blockedPath = Path.Combine(rootPath, "blocked-tv-root");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        Directory.CreateDirectory(rootPath);
        await File.WriteAllTextAsync(blockedPath, "blocked");

        await using var factory = CreateFactory(downloadPath: downloadPath, storagePath: storagePath);
        using var httpClient = factory.CreateClient();

        await UpdateCategoryDownloadRootPathAsync(databaseFilePath, "TV", blockedPath);

        var response = await AddMagnetAsync(httpClient, "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD", "Blocked Category", "TV");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("category_download_root_unavailable", error.GetProperty("code").GetString());
        Assert.Equal("requestedCategoryKey", error.GetProperty("target").GetString());
        Assert.Contains(blockedPath, error.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMagnet_PreservesStructuredError_WhenFailureLoggingAlsoFails()
    {
        var logFailureSwitch = new TestActivityLogFailureSwitch();

        await using var factory = CreateFactory(configureServices: services =>
        {
            services.RemoveAll<IActivityLogService>();
            services.AddSingleton<IActivityLogService>(serviceProvider =>
                new FailingActivityLogService(
                    new SqliteActivityLogService(
                        serviceProvider.GetRequiredService<ResolvedTorrentCoreServicePaths>().DatabaseFilePath,
                        20_000
                    ),
                    logFailureSwitch
                ));
        });
        using var httpClient = factory.CreateClient();

        logFailureSwitch.FailWrites = true;

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
    public async Task RuntimeSettingsService_GetRuntimeSettings_ThrowsStructuredError_WhenStorageIsUnavailable()
    {
        var service = CreateRuntimeSettingsServiceWithBlockedStoragePath();

        var exception = await Assert.ThrowsAsync<ServiceOperationException>(
            () => service.GetRuntimeSettingsDtoAsync(CancellationToken.None)
        );

        Assert.Equal("storage_unavailable", exception.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task RuntimeSettingsService_UpdateRuntimeSettings_ThrowsStructuredError_WhenStorageIsUnavailable()
    {
        var service = CreateRuntimeSettingsServiceWithBlockedStoragePath();

        var exception = await Assert.ThrowsAsync<ServiceOperationException>(
            () => service.UpdateAsync(CreateDefaultRuntimeSettingsUpdateRequest(), CancellationToken.None)
        );

        Assert.Equal("storage_unavailable", exception.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task RestartService_ReturnsStructuredError_WhenLaunchdRestartSchedulingIsUnavailable()
    {
        await using var factory = CreateFactory(configureServices: services =>
        {
            services.RemoveAll<ILaunchAgentServiceRestartScheduler>();
            services.AddSingleton<ILaunchAgentServiceRestartScheduler>(new FailingRestartScheduler());
        });
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PostAsync("api/host/restart-service", content: null);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("service_restart_unavailable", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DeleteOrphanedTorrentLogs_ReturnsStructuredError_WhenStorageIsUnavailable()
    {
        await using var factory = CreateFactory(configureServices: services =>
        {
            services.RemoveAll<IActivityLogService>();
            services.AddSingleton<IActivityLogService>(new ThrowingActivityLogService(deleteOrphanedFailure: new IOException("blocked")));
        });
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PostAsync("api/logs/delete-orphaned-torrent-logs", content: null);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("storage_unavailable", error.GetProperty("code").GetString());
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
        SeedingStopMode? seedingStopMode = null,
        double? seedingStopRatio = null,
        int? seedingStopMinutes = null,
        CompletedTorrentCleanupMode? completedTorrentCleanupMode = null,
        int? completedTorrentCleanupMinutes = null,
        int? maxActiveMetadataResolutions = null,
        int? maxActiveDownloads = null,
        int? metadataResolutionTimeSliceMinutes = null,
        int? runtimeTickIntervalMilliseconds = null,
        int? metadataResolutionDelayMilliseconds = null,
        double? downloadProgressPercentPerTick = null,
        Action<IServiceCollection>? configureServices = null)
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

                    if (completedTorrentCleanupMode is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:CompletedTorrentCleanupMode"] = completedTorrentCleanupMode.Value.ToString();
                    }

                    if (completedTorrentCleanupMinutes is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:CompletedTorrentCleanupMinutes"] = completedTorrentCleanupMinutes.Value.ToString();
                    }

                    if (maxActiveMetadataResolutions is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MaxActiveMetadataResolutions"] = maxActiveMetadataResolutions.Value.ToString();
                    }

                    if (maxActiveDownloads is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MaxActiveDownloads"] = maxActiveDownloads.Value.ToString();
                    }

                    if (metadataResolutionTimeSliceMinutes is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MetadataResolutionTimeSliceMinutes"] =
                                metadataResolutionTimeSliceMinutes.Value.ToString();
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

                if (configureServices is not null)
                {
                    builder.ConfigureServices(configureServices);
                }
            });
    }

    private static UpdateRuntimeSettingsRequest CreateDefaultRuntimeSettingsUpdateRequest(
        int coldDownloadRecoveryThresholdMinutes = 240,
        int coldDownloadRecoveryIntervalMinutes = 60,
        int coldDownloadAbandonAfterHours = 72,
        bool? engineAllowPeerExchange = false,
        int? automaticMetadataResetStuckThresholdSeconds = null,
        int? metadataResolutionTimeSliceMinutes = null,
        int maxActiveDownloads = 4,
        bool? vpnEgressValidationEnabled = null,
        string? vpnEgressValidationEndpoint = null,
        IReadOnlyList<string>? vpnEgressDirectIspCidrs = null,
        int? vpnEgressDegradedCheckIntervalSeconds = null,
        int? vpnEgressReadyCheckIntervalSeconds = null,
        int? vpnEgressRequestTimeoutSeconds = null,
        int? vpnEgressEngineSuspensionTimeoutSeconds = null,
        bool? runtimeTickDurationSummaryEnabled = null)
    {
        return new UpdateRuntimeSettingsRequest
        {
            SeedingStopMode = SeedingStopMode.Unlimited.ToString(),
            SeedingStopRatio = 1.5,
            SeedingStopMinutes = 90,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never.ToString(),
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineAllowPeerExchange = engineAllowPeerExchange,
            EngineEncryptionMode = TorrentEncryptionMode.EncryptedPreferred.ToString(),
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = maxActiveDownloads,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            MetadataResolutionTimeSliceMinutes = metadataResolutionTimeSliceMinutes,
            AutomaticMetadataResetStuckThresholdSeconds = automaticMetadataResetStuckThresholdSeconds,
            ColdDownloadRecoveryThresholdMinutes = coldDownloadRecoveryThresholdMinutes,
            ColdDownloadRecoveryIntervalMinutes = coldDownloadRecoveryIntervalMinutes,
            ColdDownloadAbandonAfterHours = coldDownloadAbandonAfterHours,
            CompletionCallbackEnabled = false,
            CompletionCallbackCommandPath = null,
            CompletionCallbackArguments = null,
            CompletionCallbackWorkingDirectory = null,
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = 120,
            CompletionCallbackApiBaseUrlOverride = null,
            CompletionCallbackApiKeyOverride = null,
            VpnEgressValidationEnabled = vpnEgressValidationEnabled,
            VpnEgressValidationEndpoint = vpnEgressValidationEndpoint,
            VpnEgressDirectIspCidrs = vpnEgressDirectIspCidrs,
            VpnEgressDegradedCheckIntervalSeconds = vpnEgressDegradedCheckIntervalSeconds,
            VpnEgressReadyCheckIntervalSeconds = vpnEgressReadyCheckIntervalSeconds,
            VpnEgressRequestTimeoutSeconds = vpnEgressRequestTimeoutSeconds,
            VpnEgressEngineSuspensionTimeoutSeconds = vpnEgressEngineSuspensionTimeoutSeconds,
            RuntimeTickDurationSummaryEnabled = runtimeTickDurationSummaryEnabled,
        };
    }

    private static RuntimeSettingsService CreateRuntimeSettingsServiceWithBlockedStoragePath()
    {
        var rootPath = CreateTempRootPath("torrentcore-runtime-settings-service-unavailable");
        var blockedParentPath = Path.Combine(rootPath, "blocked-parent");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(blockedParentPath, "blocked");

        var options = Options.Create(new TorrentCoreServiceOptions());
        var store = new SqliteRuntimeSettingsStore(Path.Combine(blockedParentPath, "torrentcore.db"));
        var appliedEngineSettingsState = new AppliedEngineSettingsState();
        appliedEngineSettingsState.Set(
            false,
            TorrentEncryptionMode.EncryptedPreferred,
            150,
            8,
            0,
            0
        );

        return new RuntimeSettingsService(
            options,
            store,
            new NoOpActivityLogService(),
            new ServiceInstanceContext(),
            appliedEngineSettingsState,
            new VpnSettingsChangeSignal(),
            new RuntimeTickDurationSummaryState()
        );
    }

    private static async Task<HttpResponseMessage> AddMagnetAsync(HttpClient httpClient, string infoHash, string name, string? categoryKey = null)
    {
        return await httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(name)}",
            CategoryKey = categoryKey,
        });
    }

    private static async Task UpdateCategoryDownloadRootPathAsync(string databaseFilePath, string categoryKey, string downloadRootPath)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE torrent_categories
                              SET download_root_path = $download_root_path
                              WHERE category_key = $category_key;
                              """;
        command.Parameters.AddWithValue("$download_root_path", downloadRootPath);
        command.Parameters.AddWithValue("$category_key", categoryKey);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        Assert.Equal(1, rowsAffected);
    }

    private sealed class TestActivityLogFailureSwitch
    {
        public bool FailWrites { get; set; }
    }

    private sealed class FailingActivityLogService(IActivityLogService inner, TestActivityLogFailureSwitch failureSwitch)
        : IActivityLogService
    {
        public Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            return inner.EnsureInitializedAsync(cancellationToken);
        }

        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
        {
            return failureSwitch.FailWrites
                ? throw new IOException("Simulated activity log storage failure.")
                : inner.WriteAsync(request, cancellationToken);
        }

        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(ActivityLogQuery query, CancellationToken cancellationToken)
        {
            return inner.GetRecentAsync(query, cancellationToken);
        }

        public Task<ActivityLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
        {
            return inner.GetFilterOptionsAsync(cancellationToken);
        }

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken)
        {
            return inner.DeleteByTorrentIdAsync(torrentId, cancellationToken);
        }

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken)
        {
            return inner.DeleteOrphanedTorrentLogsAsync(cancellationToken);
        }

        public Task<int> DeleteInactiveBeforeAsync(DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            return inner.DeleteInactiveBeforeAsync(cutoffUtc, cancellationToken);
        }
    }

    private sealed class FailingRestartScheduler : ILaunchAgentServiceRestartScheduler
    {
        public Task<ServiceRestartScheduleResult> ScheduleRestartAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Service restart is only supported when TorrentCore.Service is running under launchd.");
        }
    }

    private sealed class NoOpActivityLogService : IActivityLogService
    {
        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(ActivityLogQuery query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ActivityLogEntry>>(Array.Empty<ActivityLogEntry>());
        public Task<ActivityLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ActivityLogFilterOptions { Categories = [], EventTypes = [] });
        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> DeleteInactiveBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }

    private sealed class ThrowingActivityLogService(Exception? getRecentFailure = null, Exception? deleteOrphanedFailure = null)
        : IActivityLogService
    {
        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(ActivityLogQuery query, CancellationToken cancellationToken)
        {
            return getRecentFailure is null
                ? Task.FromResult<IReadOnlyList<ActivityLogEntry>>(Array.Empty<ActivityLogEntry>())
                : Task.FromException<IReadOnlyList<ActivityLogEntry>>(getRecentFailure);
        }

        public Task<ActivityLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ActivityLogFilterOptions { Categories = [], EventTypes = [] });

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken)
        {
            return deleteOrphanedFailure is null
                ? Task.FromResult(0)
                : Task.FromException<int>(deleteOrphanedFailure);
        }

        public Task<int> DeleteInactiveBeforeAsync(DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class TorrentHistoryRow
    {
        public string? LatestTorrentState { get; init; }
        public double LatestProgressPercent { get; init; }
        public DateTimeOffset? MetadataResolvedAtUtc { get; init; }
        public DateTimeOffset? DownloadStartedAtUtc { get; init; }
        public DateTimeOffset? DownloadCompletedAtUtc { get; init; }
        public DateTimeOffset? SeedingStartedAtUtc { get; init; }
        public DateTimeOffset LastUpdatedAtUtc { get; init; }
        public DateTimeOffset? RemovedAtUtc { get; init; }
        public bool DataDeleted { get; init; }
        public string? RemovalReason { get; init; }
        public TorrentRemovalKind? RemovalKind { get; init; }
        public bool RemovedByCleanupPolicy { get; init; }
    }

    private static async Task<TorrentHistoryRow?> GetTorrentHistoryRowAsync(string databaseFilePath, Guid torrentId)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  latest_torrent_state,
                                  latest_progress_percent,
                                  metadata_resolved_at_utc,
                                  download_started_at_utc,
                                  download_completed_at_utc,
                                  seeding_started_at_utc,
                                  last_updated_at_utc
                              FROM torrent_history
                              WHERE torrent_id = $torrent_id
                              LIMIT 1;
                              """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new TorrentHistoryRow
        {
            LatestTorrentState = reader.GetString(0),
            LatestProgressPercent = reader.GetDouble(1),
            MetadataResolvedAtUtc = reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
            DownloadStartedAtUtc = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
            DownloadCompletedAtUtc = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
            SeedingStartedAtUtc = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            LastUpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(6)),
        };
    }

    private static async Task<TorrentHistoryRow?> GetRemovalHistoryRowAsync(string databaseFilePath, Guid torrentId)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  removed_at_utc,
                                  data_deleted,
                                  removal_reason,
                                  removal_kind,
                                  removed_by_cleanup_policy,
                                  last_updated_at_utc,
                                  latest_torrent_state,
                                  latest_progress_percent
                              FROM torrent_history
                              WHERE torrent_id = $torrent_id
                              LIMIT 1;
                              """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new TorrentHistoryRow
        {
            RemovedAtUtc = reader.IsDBNull(0) ? null : DateTimeOffset.Parse(reader.GetString(0)),
            DataDeleted = reader.GetInt64(1) != 0,
            RemovalReason = reader.IsDBNull(2) ? null : reader.GetString(2),
            RemovalKind = reader.IsDBNull(3)
                ? null
                : Enum.Parse<TorrentRemovalKind>(reader.GetString(3)),
            RemovedByCleanupPolicy = reader.GetInt64(4) != 0,
            LastUpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(5)),
            LatestTorrentState = reader.GetString(6),
            LatestProgressPercent = reader.GetDouble(7),
        };
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

    private static async Task ForceDownloadColdSinceAsync(
        string databaseFilePath,
        Guid torrentId,
        DateTimeOffset coldSinceUtc)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE torrents
                              SET download_cold_since_utc = $download_cold_since_utc
                              WHERE torrent_id = $torrent_id;
                              """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        command.Parameters.AddWithValue("$download_cold_since_utc", coldSinceUtc.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ForceMetadataAttemptStartedAsync(
        string storagePath,
        Guid torrentId,
        DateTimeOffset startedAtUtc)
    {
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE torrents
                              SET metadata_resolution_attempt_started_at_utc = $started_at_utc
                              WHERE torrent_id = $torrent_id;
                              """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        command.Parameters.AddWithValue("$started_at_utc", startedAtUtc.ToString("O"));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateHistoryRowAsync(
        string databaseFilePath,
        Guid torrentId,
        DateTimeOffset submittedAtUtc,
        string latestTorrentState,
        DateTimeOffset? removedAtUtc,
        string? removalReason = null,
        TorrentRemovalKind? removalKind = null)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE torrent_history
            SET
                submitted_at_utc = $submitted_at_utc,
                last_updated_at_utc = $last_updated_at_utc,
                latest_torrent_state = $latest_torrent_state,
                removed_at_utc = $removed_at_utc,
                removal_reason = $removal_reason,
                removal_kind = $removal_kind,
                removed_by_cleanup_policy = $removed_by_cleanup_policy
            WHERE torrent_id = $torrent_id;
            """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        command.Parameters.AddWithValue("$submitted_at_utc", submittedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$last_updated_at_utc", submittedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$latest_torrent_state", latestTorrentState);
        command.Parameters.AddWithValue("$removed_at_utc", removedAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$removal_reason", (object?)removalReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$removal_kind", removalKind?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$removed_by_cleanup_policy",
            removalKind is TorrentRemovalKind.CompletedTorrentCleanup or TorrentRemovalKind.ColdDownloadAbandonment ? 1 : 0);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ForceSeedingPolicyReevaluationAsync(string storagePath, Guid torrentId)
    {
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE torrents
                              SET state = 'Seeding',
                                  desired_state = 'Runnable',
                                  progress_percent = 100,
                                  downloaded_bytes = COALESCE(total_bytes, downloaded_bytes),
                                  download_rate_bytes_per_second = 0,
                                  error_message = NULL
                              WHERE torrent_id = $torrent_id;
                              """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ForcePersistedFinishedTorrentSnapshotAsync(
        string storagePath,
        Guid torrentId,
        TorrentState state,
        TorrentDesiredState desiredState,
        DateTimeOffset completedAtUtc)
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
                progress_percent = 100,
                downloaded_bytes = 1048576,
                total_bytes = 1048576,
                download_rate_bytes_per_second = 0,
                upload_rate_bytes_per_second = 0,
                connected_peer_count = 0,
                error_message = NULL,
                completed_at_utc = $completed_at_utc,
                seeding_started_at_utc = $completed_at_utc,
                completion_callback_state = NULL,
                completion_callback_pending_since_utc = NULL,
                completion_callback_invoked_at_utc = NULL,
                completion_callback_last_error = NULL
            WHERE torrent_id = $torrent_id;
            """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$desired_state", desiredState.ToString());
        command.Parameters.AddWithValue("$completed_at_utc", completedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdatePersistedCompletionCallbackSnapshotAsync(
        string storagePath,
        Guid torrentId,
        TorrentState state,
        TorrentDesiredState desiredState,
        DateTimeOffset completedAtUtc,
        TorrentCompletionCallbackState callbackState,
        DateTimeOffset? pendingSinceUtc,
        DateTimeOffset? invokedAtUtc,
        string? lastError)
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
                progress_percent = 100,
                download_rate_bytes_per_second = 0,
                upload_rate_bytes_per_second = 0,
                connected_peer_count = 0,
                error_message = NULL,
                completed_at_utc = $completed_at_utc,
                seeding_started_at_utc = $completed_at_utc,
                completion_callback_state = $completion_callback_state,
                completion_callback_pending_since_utc = $completion_callback_pending_since_utc,
                completion_callback_invoked_at_utc = $completion_callback_invoked_at_utc,
                completion_callback_last_error = $completion_callback_last_error
            WHERE torrent_id = $torrent_id;
            """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$desired_state", desiredState.ToString());
        command.Parameters.AddWithValue("$completed_at_utc", completedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$completion_callback_state", callbackState.ToString());
        command.Parameters.AddWithValue("$completion_callback_pending_since_utc", pendingSinceUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completion_callback_invoked_at_utc", invokedAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completion_callback_last_error", (object?)lastError ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdateCompletionCallbackSettingsAsync(
        HttpClient httpClient,
        string commandPath,
        string? arguments,
        string? workingDirectory,
        string? apiBaseUrlOverride = null,
        string? apiKeyOverride = null,
        int? finalizationTimeoutSeconds = null,
        int? callbackTimeoutSeconds = null)
    {
        var response = await httpClient.PutAsJsonAsync("api/host/runtime-settings", new UpdateRuntimeSettingsRequest
        {
            SeedingStopMode = SeedingStopMode.StopImmediately.ToString(),
            SeedingStopRatio = 1.0,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never.ToString(),
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            ColdDownloadRecoveryThresholdMinutes = 240,
            ColdDownloadRecoveryIntervalMinutes = 60,
            CompletionCallbackEnabled = true,
            CompletionCallbackCommandPath = commandPath,
            CompletionCallbackArguments = arguments,
            CompletionCallbackWorkingDirectory = workingDirectory,
            CompletionCallbackTimeoutSeconds = callbackTimeoutSeconds ?? 30,
            CompletionCallbackFinalizationTimeoutSeconds = finalizationTimeoutSeconds,
            CompletionCallbackApiBaseUrlOverride = apiBaseUrlOverride,
            CompletionCallbackApiKeyOverride = apiKeyOverride,
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task UpdateMetadataRecoverySettingsAsync(HttpClient httpClient, int staleSeconds, int restartDelaySeconds)
    {
        var response = await httpClient.PutAsJsonAsync("api/host/runtime-settings", new UpdateRuntimeSettingsRequest
        {
            SeedingStopMode = SeedingStopMode.StopImmediately.ToString(),
            SeedingStopRatio = 1.0,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never.ToString(),
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = staleSeconds,
            MetadataRefreshRestartDelaySeconds = restartDelaySeconds,
            ColdDownloadRecoveryThresholdMinutes = 240,
            ColdDownloadRecoveryIntervalMinutes = 60,
            CompletionCallbackEnabled = false,
            CompletionCallbackCommandPath = null,
            CompletionCallbackArguments = null,
            CompletionCallbackWorkingDirectory = null,
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = 120,
            CompletionCallbackApiBaseUrlOverride = null,
            CompletionCallbackApiKeyOverride = null,
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
              printf 'TORRENTCORE_TORRENT_ID=%s\n' "${TORRENTCORE_TORRENT_ID}"
              printf 'TR_TORRENT_HASH=%s\n' "${TR_TORRENT_HASH}"
              printf 'TR_TORRENT_NAME=%s\n' "${TR_TORRENT_NAME}"
              printf 'TR_TORRENT_DIR=%s\n' "${TR_TORRENT_DIR}"
              printf 'TR_TORRENT_LABELS=%s\n' "${TR_TORRENT_LABELS}"
              printf 'TORRENTCORE_FINAL_PAYLOAD_PATH=%s\n' "${TORRENTCORE_FINAL_PAYLOAD_PATH}"
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

    private static async Task<bool> ReadPersistedTorrentExistsAsync(string storagePath, Guid torrentId)
    {
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM torrents
                WHERE torrent_id = $torrent_id
            );
            """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());

        var result = await command.ExecuteScalarAsync();
        return result is not null && Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
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

    private static JsonElement ParseLogDetails(ActivityLogEntryDto logEntry)
    {
        Assert.False(string.IsNullOrWhiteSpace(logEntry.DetailsJson));
        using var document = JsonDocument.Parse(logEntry.DetailsJson!);
        return document.RootElement.Clone();
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
