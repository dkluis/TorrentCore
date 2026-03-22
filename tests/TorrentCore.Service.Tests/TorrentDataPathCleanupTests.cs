using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentDataPathCleanupTests
{
    [Fact]
    public void DeletePayloadArtifacts_RemovesFileWithinDownloadRoot()
    {
        var rootPath = CreateTempRootPath("torrentcore-cleanup-file");
        var downloadRootPath = Path.Combine(rootPath, "downloads");
        var payloadDirectory = Path.Combine(downloadRootPath, "Show", "Season 01");
        var payloadFile = Path.Combine(payloadDirectory, "Episode 01.mkv");
        Directory.CreateDirectory(payloadDirectory);
        File.WriteAllText(payloadFile, "payload");

        TorrentDataPathCleanup.DeletePayloadArtifacts(downloadRootPath, [payloadFile]);
        TorrentDataPathCleanup.DeleteEmptyDirectories(downloadRootPath, [payloadFile]);

        Assert.False(File.Exists(payloadFile));
        Assert.False(Directory.Exists(payloadDirectory));
        Assert.False(Directory.Exists(Path.Combine(downloadRootPath, "Show")));
        Assert.True(Directory.Exists(downloadRootPath));
    }

    [Fact]
    public void DeletePayloadArtifacts_RemovesDirectoryWithinDownloadRoot()
    {
        var rootPath = CreateTempRootPath("torrentcore-cleanup-directory");
        var downloadRootPath = Path.Combine(rootPath, "downloads");
        var payloadDirectory = Path.Combine(downloadRootPath, "Show", "Season 01");
        Directory.CreateDirectory(payloadDirectory);
        File.WriteAllText(Path.Combine(payloadDirectory, "Episode 01.mkv"), "payload");

        TorrentDataPathCleanup.DeletePayloadArtifacts(downloadRootPath, [payloadDirectory]);
        TorrentDataPathCleanup.DeleteEmptyDirectories(downloadRootPath, [payloadDirectory]);

        Assert.False(Directory.Exists(payloadDirectory));
        Assert.False(Directory.Exists(Path.Combine(downloadRootPath, "Show")));
        Assert.True(Directory.Exists(downloadRootPath));
    }

    [Fact]
    public void DeletePayloadArtifacts_DoesNotDeleteOutsideDownloadRoot()
    {
        var rootPath = CreateTempRootPath("torrentcore-cleanup-outside");
        var downloadRootPath = Path.Combine(rootPath, "downloads");
        var outsideDirectory = Path.Combine(rootPath, "outside");
        var outsideFile = Path.Combine(outsideDirectory, "keep.txt");
        Directory.CreateDirectory(downloadRootPath);
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(outsideFile, "keep");

        TorrentDataPathCleanup.DeletePayloadArtifacts(downloadRootPath, [outsideFile, outsideDirectory]);
        TorrentDataPathCleanup.DeleteEmptyDirectories(downloadRootPath, [outsideFile, outsideDirectory]);

        Assert.True(Directory.Exists(downloadRootPath));
        Assert.True(Directory.Exists(outsideDirectory));
        Assert.True(File.Exists(outsideFile));
    }

    [Fact]
    public void DeleteEmptyDirectories_PrunesNestedEmptyDirectories_ButKeepsDownloadRoot()
    {
        var rootPath = CreateTempRootPath("torrentcore-cleanup");
        var downloadRootPath = Path.Combine(rootPath, "downloads");
        var nestedDirectory = Path.Combine(downloadRootPath, "Show", "Season 01");
        Directory.CreateDirectory(nestedDirectory);

        TorrentDataPathCleanup.DeleteEmptyDirectories(downloadRootPath, [nestedDirectory]);

        Assert.False(Directory.Exists(nestedDirectory));
        Assert.False(Directory.Exists(Path.Combine(downloadRootPath, "Show")));
        Assert.True(Directory.Exists(downloadRootPath));
    }

    [Fact]
    public void DeleteEmptyDirectories_DoesNotDeleteDirectoryContainingOtherFiles()
    {
        var rootPath = CreateTempRootPath("torrentcore-cleanup-nonempty");
        var downloadRootPath = Path.Combine(rootPath, "downloads");
        var nestedDirectory = Path.Combine(downloadRootPath, "Show", "Season 01");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "keep.txt"), "keep");

        TorrentDataPathCleanup.DeleteEmptyDirectories(downloadRootPath, [nestedDirectory]);

        Assert.True(Directory.Exists(nestedDirectory));
        Assert.True(File.Exists(Path.Combine(nestedDirectory, "keep.txt")));
    }

    [Fact]
    public void DeleteEmptyDirectories_DoesNotDeleteOutsideDownloadRoot()
    {
        var rootPath = CreateTempRootPath("torrentcore-cleanup-boundary");
        var downloadRootPath = Path.Combine(rootPath, "downloads");
        var outsideDirectory = Path.Combine(rootPath, "outside");
        Directory.CreateDirectory(downloadRootPath);
        Directory.CreateDirectory(outsideDirectory);

        TorrentDataPathCleanup.DeleteEmptyDirectories(downloadRootPath, [outsideDirectory]);

        Assert.True(Directory.Exists(downloadRootPath));
        Assert.True(Directory.Exists(outsideDirectory));
    }

    private static string CreateTempRootPath(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
