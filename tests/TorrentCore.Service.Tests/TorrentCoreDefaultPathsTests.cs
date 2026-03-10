using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class TorrentCoreDefaultPathsTests
{
    [Fact]
    public void GetDefaultDownloadRootPath_DoesNotUseUserDownloadsFolder()
    {
        var result = TorrentCoreDefaultPaths.GetDefaultDownloadRootPath();

        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}Downloads{Path.DirectorySeparatorChar}", result, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("TorrentCore", "downloads"), result, StringComparison.OrdinalIgnoreCase);
    }
}
