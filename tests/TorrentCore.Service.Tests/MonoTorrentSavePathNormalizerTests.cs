using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class MonoTorrentSavePathNormalizerTests
{
    [Fact]
    public void Normalize_CollapsesDuplicatedTrailingTorrentDirectory()
    {
        var normalized = MonoTorrentSavePathNormalizer.Normalize(
            "/Users/example/TorrentCore/downloads/TorrentName/TorrentName",
            "TorrentName");

        Assert.Equal("/Users/example/TorrentCore/downloads/TorrentName", normalized);
    }

    [Fact]
    public void Normalize_LeavesNormalSavePathUnchanged()
    {
        var normalized = MonoTorrentSavePathNormalizer.Normalize(
            "/Users/example/TorrentCore/downloads/TorrentName",
            "TorrentName");

        Assert.Equal("/Users/example/TorrentCore/downloads/TorrentName", normalized);
    }
}
