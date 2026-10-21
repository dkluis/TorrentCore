using System.Text.Json;
using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Vpn;

namespace TorrentCore.Service.Tests;

// Captures process requests only. Never launches the callback or accesses configured storage/services.
public sealed class TorrentPayloadRejectionCallbackTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static TorrentPayloadRejection Row(string phase = "Deleted") =>
        new(Guid.NewGuid(), Hash, "/downloads", ["/downloads/payload"], ["setup.exe"], phase);

    [Fact]
    public async Task BlankOverridesUseConfiguredCallbackAndSendOnlyConfirmedDeletionWithStableIdentity()
    {
        var row = Row(); var requests = new List<ExternalProcessRequest>();
        var effects = Effects(request =>
        {
            requests.Add(request);
            Assert.Equal("/configured/DownloadCompleted.TorrentCore.zsh", request.FileName);
            Assert.Equal("/configured", request.WorkingDirectory);
            Assert.Equal("--existing-option \"two words\" --payload-rejection", request.ArgumentString);
            Assert.False(request.Environment!.ContainsKey("TVMAZE_API_COMPLETE_URL"));
            Assert.False(request.Environment.ContainsKey("TVMAZE_API_COMPLETE_API_KEY"));
            Assert.Equal("", request.Environment["TR_TORRENT_NAME"]);
            using var json = JsonDocument.Parse(request.StandardInput!);
            Assert.Equal(row.TorrentId, json.RootElement.GetProperty("EventId").GetGuid());
            Assert.Equal(Hash, json.RootElement.GetProperty("InfoHash").GetString());
            Assert.Equal("DeletedIncludingData", json.RootElement.GetProperty("CleanupOutcome").GetString());
            Assert.Equal("TorrentCore", json.RootElement.GetProperty("Source").GetString());
            return Success(row.InfoHash);
        });
        Assert.True((await effects.ReportAsync(row, default)).Confirmed);
        Assert.True((await effects.ReportAsync(row, default)).Confirmed);
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].StandardInput, requests[1].StandardInput);
    }

    [Theory]
    [InlineData("Removing")]
    [InlineData("Removed")]
    public async Task UnconfirmedDeletionCannotLaunchCallback(string phase)
    {
        var calls = 0;
        var effects = Effects(_ => { calls++; return Success(Hash); });
        Assert.False((await effects.ReportAsync(Row(phase), default)).Confirmed);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 1)]
    public async Task ProcessFailureOrTimeoutCannotAcknowledgeDeletion(bool started, bool timedOut, int exitCode)
    {
        var effects = Effects(_ => new() { Started = started, TimedOut = timedOut, ExitCode = exitCode });
        Assert.False((await effects.ReportAsync(Row(), default)).Confirmed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TORRENTCORE_REJECTION_ACK:invalid")]
    [InlineData("TORRENTCORE_REJECTION_ACK:{}")]
    public async Task MissingOrInvalidAcknowledgementRemainsPending(string output)
    {
        var effects = Effects(_ => new() { Started = true, TimedOut = false, ExitCode = 0, StandardOutput = output });
        Assert.False((await effects.ReportAsync(Row(), default)).Confirmed);
    }

    [Fact]
    public async Task WrongHashAndDisabledCallbackCannotAcknowledgeDeletion()
    {
        Assert.False((await Effects(_ => Success(new string('B', 40))).ReportAsync(Row(), default)).Confirmed);
        var effects = new TorrentPayloadRejectionEffects(_ => Task.FromResult(DisabledSettings()),
            _ => Task.FromResult<IReadOnlyList<TorrentSnapshot>>([]),
            (_, _) => throw new Exception("Unexpected callback"), "/storage");
        Assert.False((await effects.ReportAsync(Row(), default)).Confirmed);
    }

    [Fact]
    public async Task SettingsAndLaunchFailuresAreContainedWithoutEchoingCredentials()
    {
        var effects = Effects(_ => throw new IOException("secret must not appear"));
        var reply = await effects.ReportAsync(Row(), default);
        Assert.False(reply.Confirmed); Assert.DoesNotContain("secret", reply.Message);
        effects = new TorrentPayloadRejectionEffects(_ => throw new IOException("secret"),
            _ => Task.FromResult<IReadOnlyList<TorrentSnapshot>>([]), (_, _) => throw new Exception("Unexpected callback"), "/storage");
        reply = await effects.ReportAsync(Row(), default);
        Assert.False(reply.Confirmed); Assert.DoesNotContain("secret", reply.Message);
    }

    private static TorrentPayloadRejectionEffects Effects(Func<ExternalProcessRequest, ExternalProcessResult> run) =>
        new(_ => Task.FromResult(Settings()), _ => Task.FromResult<IReadOnlyList<TorrentSnapshot>>([]),
            (request, _) => Task.FromResult(run(request)), "/storage");
    private static ExternalProcessResult Success(string hash) => new()
    {
        Started = true, TimedOut = false, ExitCode = 0,
        StandardOutput = "ordinary diagnostic\n" + TorrentPayloadRejectionEffects.AcknowledgementPrefix +
            JsonSerializer.Serialize(new { RecoveryId = Guid.NewGuid(), InfoHash = hash }) + "\n",
    };
    private static RuntimeSettingsSnapshot DisabledSettings() => Settings(false);
    private static RuntimeSettingsSnapshot Settings(bool enabled = true)
    {
        return new RuntimeSettingsSnapshot
        {
            UsesPersistedOverrides = false,
            PartialFilesEnabled = false,
            PartialFileSuffix = string.Empty,
            SeedingStopMode = SeedingStopMode.Unlimited,
            SeedingStopRatio = 1,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never,
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineEncryptionMode = TorrentEncryptionMode.EncryptedPreferred,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            CompletionCallbackEnabled = enabled,
            CompletionCallbackCommandPath = "/configured/DownloadCompleted.TorrentCore.zsh",
            CompletionCallbackArguments = "--existing-option \"two words\"",
            CompletionCallbackWorkingDirectory = "/configured",
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = 120,
            CompletionCallbackApiBaseUrlOverride = null,
            CompletionCallbackApiKeyOverride = null,
            EngineSettingsRequireRestart = false,
            UpdatedAtUtc = null,
        };
    }
}
