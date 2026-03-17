using Microsoft.Extensions.Options;

namespace TorrentCore.Service.Configuration;

public sealed class TorrentCoreServiceOptionsValidator(IHostEnvironment hostEnvironment) : IValidateOptions<TorrentCoreServiceOptions>
{
    public ValidateOptionsResult Validate(string? name, TorrentCoreServiceOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DownloadRootPath))
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath is required.");
        }

        if (string.IsNullOrWhiteSpace(options.StorageRootPath))
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:StorageRootPath is required.");
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        if (options.MaxActivityLogEntries < 100)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:MaxActivityLogEntries must be 100 or greater.");
        }

        if (!Enum.IsDefined(options.EngineMode))
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineMode is invalid.");
        }

        if (!Enum.IsDefined(options.SeedingStopMode))
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:SeedingStopMode is invalid.");
        }

        if (!Enum.IsDefined(options.CompletedTorrentCleanupMode))
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:CompletedTorrentCleanupMode is invalid.");
        }

        if (options.EngineListenPort is < 0 or > 65_535)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineListenPort must be between 0 and 65535.");
        }

        if (options.EngineDhtPort is < 0 or > 65_535)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineDhtPort must be between 0 and 65535.");
        }

        if (options.EngineConnectionFailureLogBurstLimit < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineConnectionFailureLogBurstLimit must be 1 or greater.");
        }

        if (options.EngineConnectionFailureLogWindowSeconds < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineConnectionFailureLogWindowSeconds must be 1 or greater.");
        }

        if (options.EngineMaximumConnections < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineMaximumConnections must be 1 or greater.");
        }

        if (options.EngineMaximumHalfOpenConnections < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineMaximumHalfOpenConnections must be 1 or greater.");
        }

        if (options.EngineMaximumDownloadRateBytesPerSecond < 0)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineMaximumDownloadRateBytesPerSecond must be 0 or greater.");
        }

        if (options.EngineMaximumUploadRateBytesPerSecond < 0)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:EngineMaximumUploadRateBytesPerSecond must be 0 or greater.");
        }

        if (options.SeedingStopRatio <= 0)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:SeedingStopRatio must be greater than 0.");
        }

        if (options.SeedingStopMinutes < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:SeedingStopMinutes must be 1 or greater.");
        }

        if (options.CompletedTorrentCleanupMinutes < 0)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:CompletedTorrentCleanupMinutes must be 0 or greater.");
        }

        if (options.MaxActiveMetadataResolutions < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:MaxActiveMetadataResolutions must be 1 or greater.");
        }

        if (options.MaxActiveDownloads < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:MaxActiveDownloads must be 1 or greater.");
        }

        if (options.CompletionCallbackTimeoutSeconds < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:CompletionCallbackTimeoutSeconds must be 1 or greater.");
        }

        if (options.CompletionCallbackFinalizationTimeoutSeconds < 1)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:CompletionCallbackFinalizationTimeoutSeconds must be 1 or greater.");
        }

        if (options.CompletionCallbackEnabled && string.IsNullOrWhiteSpace(options.CompletionCallbackCommandPath))
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:CompletionCallbackCommandPath is required when CompletionCallbackEnabled is true.");
        }

        if (options.RuntimeTickIntervalMilliseconds < 50)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:RuntimeTickIntervalMilliseconds must be 50 or greater.");
        }

        if (options.MetadataResolutionDelayMilliseconds < 0)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:MetadataResolutionDelayMilliseconds must be 0 or greater.");
        }

        if (options.DownloadProgressPercentPerTick <= 0 || options.DownloadProgressPercentPerTick > 100)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName}:DownloadProgressPercentPerTick must be greater than 0 and less than or equal to 100.");
        }

        try
        {
            var resolvedPaths = TorrentCoreServicePathResolver.Resolve(hostEnvironment.ContentRootPath, options);

            if (string.Equals(
                    resolvedPaths.DownloadRootPath,
                    resolvedPaths.StorageRootPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath and {TorrentCoreServiceOptions.SectionName}:StorageRootPath must resolve to different directories.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failures.Add($"{TorrentCoreServiceOptions.SectionName} contains an invalid path value: {exception.Message}");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
