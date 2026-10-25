using System.Text.Json;
using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Tests;

// Direct manifest/configuration inputs only; no engine, filesystem, database or network.
public sealed class MediaPayloadPolicyTests
{
    private static MediaPayloadPolicy Policy() => new MediaPayloadPolicy(["mkv", ".MP4"], ["exe", "iso", "7z", "zip"]).Validated();

    [Theory]
    [InlineData("episode.mkv", null)]
    [InlineData("episode.MP4", null)]
    [InlineData("episode.srt", "MissingVideo")]
    [InlineData("episode.rar", "MissingVideo")]
    [InlineData("episode.7z", "ProhibitedExtension")]
    [InlineData("episode.iso", "ProhibitedExtension")]
    [InlineData("episode.ZIP", "ProhibitedExtension")]
    [InlineData("episode.mkv.exe", "Executable")]
    public void ActualFinalExtensionDeterminesRejection(string path, string? reason)
    {
        var result = Policy().Inspect([path]);
        Assert.Equal(reason, result.RejectionReason);
        Assert.Equal(reason is not null, result.Rejected);
        if (reason == "MissingVideo") { Assert.Empty(result.OffendingFiles); Assert.Equal([path], result.ObservedFiles); }
    }

    [Theory]
    [InlineData("nested/archive.7z")]
    [InlineData("nested/image.ISO")]
    [InlineData("nested/package.zip")]
    public void ProhibitedFileRejectsMixedPayload(string prohibited)
    { Assert.True(Policy().Inspect(["episode.mkv", "episode.srt", prohibited]).Rejected); }

    [Fact]
    public void CurrentPolicyCanBeAppliedToAnExistingManifestWithoutPerTorrentState()
    {
        string[] files = ["episode.mkv", "extra.zip"];
        Assert.False(new MediaPayloadPolicy(["mkv"], ["exe"]).Validated().Inspect(files).Rejected);
        Assert.True(Policy().Inspect(files).Rejected);
    }

    [Fact]
    public void InvalidOrOverlappingConfigurationIsNotARejectionDecision()
    {
        Assert.Throws<ArgumentException>(() => new MediaPayloadPolicy([], ["exe"]).Validated());
        Assert.Throws<ArgumentException>(() => new MediaPayloadPolicy(["mkv"], ["MKV"]).Validated());
        Assert.Throws<ArgumentException>(() => new MediaPayloadPolicy(["../mkv"], ["exe"]).Validated());
    }

    [Fact]
    public void LegacyHistoryKeepsExecutableReasonAndNewEvidenceSurvivesRestartSerialization()
    {
        var old = new TorrentPayloadRejection(Guid.NewGuid(), "A", "/downloads", ["/downloads/item"], ["item.exe"]);
        var json = JsonSerializer.Serialize(old);
        var legacy = JsonSerializer.Deserialize<TorrentPayloadRejection>(json.Replace(",\"RejectionReason\":\"Executable\"", ""))!;
        Assert.Equal("Executable", legacy.RejectionReason);
        var current = old with { RejectionReason = "MissingVideo", OffendingFiles = [], ObservedFiles = ["item.srt"] };
        var resumed = JsonSerializer.Deserialize<TorrentPayloadRejection>(JsonSerializer.Serialize(current))!;
        Assert.Equal("MissingVideo", resumed.RejectionReason); Assert.Empty(resumed.OffendingFiles); Assert.Equal(["item.srt"], resumed.ObservedFiles);
    }
}
