using Microsoft.Extensions.Hosting;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class TorrentCoreServiceOptionsValidatorTests
{
    private readonly TorrentCoreServiceOptionsValidator _validator = new(new TestHostEnvironment());

    [Fact]
    public void Validate_Fails_ForBlankDownloadRootPath()
    {
        var options = new TorrentCoreServiceOptions
        {
            DownloadRootPath = string.Empty,
            StorageRootPath = "Runtime/storage",
        };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("DownloadRootPath", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Fails_WhenResolvedPathsMatch()
    {
        var options = new TorrentCoreServiceOptions
        {
            DownloadRootPath = "Runtime/shared",
            StorageRootPath = "./Runtime/shared",
        };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("must resolve to different directories", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Succeeds_ForDistinctRelativePaths()
    {
        var options = new TorrentCoreServiceOptions
        {
            DownloadRootPath = "Runtime/downloads",
            StorageRootPath = "Runtime/storage",
        };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_Fails_ForLowMaxActivityLogEntries()
    {
        var options = new TorrentCoreServiceOptions
        {
            DownloadRootPath = "Runtime/downloads",
            StorageRootPath = "Runtime/storage",
            MaxActivityLogEntries = 99,
        };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("MaxActivityLogEntries", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Fails_ForInvalidRuntimeSettings()
    {
        var options = new TorrentCoreServiceOptions
        {
            DownloadRootPath = "Runtime/downloads",
            StorageRootPath = "Runtime/storage",
            MaxActiveDownloads = 0,
            RuntimeTickIntervalMilliseconds = 49,
            MetadataResolutionDelayMilliseconds = -1,
            DownloadProgressPercentPerTick = 0,
        };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("MaxActiveDownloads", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("RuntimeTickIntervalMilliseconds", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("MetadataResolutionDelayMilliseconds", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("DownloadProgressPercentPerTick", StringComparison.Ordinal));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TorrentCore.Service.Tests";
        public string ContentRootPath { get; set; } = "/tmp/torrentcore-tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
