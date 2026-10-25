using TorrentCore.Contracts.Host;
using TorrentCore.WebUI2.Client.Services;

namespace TorrentCore.WebUI2.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void SavingOneGroupPreservesOtherRuntimeFieldsAndPiaProvider()
    {
        var current = CurrentSettings();
        var draft = RuntimeSettingsDraft.From(current);
        draft.MaxActiveDownloads = 5;

        var request = draft.ToRequest();

        Assert.Equal(5, request.MaxActiveDownloads);
        Assert.Equal(current.RequiredVideoExtensions, request.RequiredVideoExtensions);
        Assert.Equal(current.ProhibitedPayloadExtensions, request.ProhibitedPayloadExtensions);
        Assert.Equal("PIA", request.VpnRecoveryProvider);
        Assert.Equal("EncryptedPreferred", request.EngineEncryptionMode);
        Assert.Equal(150, request.EngineMaximumConnections);
        Assert.Equal(true, request.CompletionCallbackEnabled);
        Assert.Equal("/usr/local/bin/callback", request.CompletionCallbackCommandPath);
        Assert.Equal("secret", request.CompletionCallbackApiKeyOverride);
        Assert.Equal(new[] { "192.0.2.0/24", "198.51.100.0/24" }, request.VpnEgressDirectIspCidrs);
    }

    [Fact]
    public void InvalidVpnTimeoutBlocksSaveWithoutChangingOtherValues()
    {
        var draft = RuntimeSettingsDraft.From(CurrentSettings());
        draft.VpnEgressRequestTimeoutSeconds = draft.VpnEgressDegradedCheckIntervalSeconds;

        Assert.Equal("VPN request timeout must be shorter than both check intervals.", RuntimeSettingsValidation.Error(draft));
        Assert.Equal(150, draft.ToRequest().EngineMaximumConnections);
    }

    [Fact]
    public void RecreatingDraftFromServiceSnapshotDiscardsUnsavedEdit()
    {
        var current = CurrentSettings();
        var draft = RuntimeSettingsDraft.From(current);
        draft.EngineAllowPeerExchange = true;

        draft = RuntimeSettingsDraft.From(current);

        Assert.False(draft.EngineAllowPeerExchange);
        Assert.Null(RuntimeSettingsValidation.Error(draft));
    }

    private static RuntimeSettingsDto CurrentSettings() => new()
    {
        RequiredVideoExtensions = [".mkv", ".mp4", ".avi"],
        ProhibitedPayloadExtensions = [".exe", ".iso", ".7z", ".zip", ".rar"],
        VpnRecoveryProvider = "PIA",
        EngineRuntime = "MonoTorrent",
        SupportsLiveUpdates = true,
        UsesPersistedOverrides = true,
        PartialFilesEnabled = false,
        PartialFileSuffix = "",
        SeedingStopMode = "Unlimited",
        SeedingStopRatio = 1,
        SeedingStopMinutes = 60,
        CompletedTorrentCleanupMode = "Never",
        CompletedTorrentCleanupMinutes = 60,
        DeleteLogsForCompletedTorrents = false,
        EngineConnectionFailureLogBurstLimit = 5,
        EngineConnectionFailureLogWindowSeconds = 60,
        EngineAllowPeerExchange = false,
        EngineEncryptionMode = "EncryptedPreferred",
        EngineMaximumConnections = 150,
        EngineMaximumHalfOpenConnections = 8,
        EngineMaximumDownloadRateBytesPerSecond = 0,
        EngineMaximumUploadRateBytesPerSecond = 0,
        MaxActiveMetadataResolutions = 4,
        MaxActiveDownloads = 4,
        MetadataRefreshStaleSeconds = 90,
        MetadataRefreshRestartDelaySeconds = 30,
        MetadataResolutionTimeSliceMinutes = 15,
        PriorityMetadataAttempts = 3,
        DownloadNoProgressTimeSliceMinutes = 30,
        AutomaticMetadataResetStuckThresholdSeconds = 30,
        ColdDownloadRecoveryThresholdMinutes = 120,
        ColdDownloadRecoveryIntervalMinutes = 60,
        ColdDownloadAbandonAfterHours = 72,
        CompletionCallbackEnabled = true,
        CompletionCallbackCommandPath = "/usr/local/bin/callback",
        CompletionCallbackTimeoutSeconds = 30,
        CompletionCallbackFinalizationTimeoutSeconds = 120,
        CompletionCallbackApiKeyOverride = "secret",
        VpnEgressValidationEnabled = true,
        VpnEgressValidationEndpoint = "https://example.org/ip",
        VpnEgressDirectIspCidrs = ["192.0.2.0/24", "198.51.100.0/24"],
        VpnEgressDegradedCheckIntervalSeconds = 60,
        VpnEgressReadyCheckIntervalSeconds = 240,
        VpnEgressRequestTimeoutSeconds = 10,
        VpnEgressEngineSuspensionTimeoutSeconds = 10,
        ExpressVpnAutomaticRecoveryMode = "Disabled",
        ExpressVpnRecoveryDelaySeconds = 180,
        ExpressVpnUnavailableLaunchDelaySeconds = 300,
        RuntimeTickDurationSummaryEnabled = false,
        AppliedEngineMaximumConnections = 150,
        AppliedEngineMaximumHalfOpenConnections = 8,
        AppliedEngineAllowPeerExchange = false,
        AppliedEngineMaximumDownloadRateBytesPerSecond = 0,
        AppliedEngineMaximumUploadRateBytesPerSecond = 0,
        EngineSettingsRequireRestart = false,
        RetrievedAtUtc = DateTimeOffset.UtcNow,
    };
}
