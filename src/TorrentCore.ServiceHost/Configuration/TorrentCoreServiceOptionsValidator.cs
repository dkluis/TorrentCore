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
