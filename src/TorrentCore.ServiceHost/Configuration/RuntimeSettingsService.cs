using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Host;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Persistence.Sqlite.Configuration;

namespace TorrentCore.Service.Configuration;

public sealed class RuntimeSettingsService(
    IOptions<TorrentCoreServiceOptions> serviceOptions,
    SqliteRuntimeSettingsStore runtimeSettingsStore,
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext) : IRuntimeSettingsService
{
    public async Task<RuntimeSettingsSnapshot> GetEffectiveSettingsAsync(CancellationToken cancellationToken)
    {
        var persistedSettings = await runtimeSettingsStore.GetAsync(cancellationToken);
        return BuildSnapshot(serviceOptions.Value, persistedSettings);
    }

    public async Task<RuntimeSettingsDto> GetRuntimeSettingsDtoAsync(CancellationToken cancellationToken)
    {
        var settings = await GetEffectiveSettingsAsync(cancellationToken);
        return MapDto(serviceOptions.Value, settings);
    }

    public async Task<RuntimeSettingsDto> UpdateAsync(UpdateRuntimeSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SeedingStopMode>(request.SeedingStopMode, ignoreCase: true, out var seedingStopMode) ||
            !Enum.IsDefined(seedingStopMode))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "SeedingStopMode is invalid.",
                StatusCodes.Status400BadRequest,
                nameof(request.SeedingStopMode));
        }

        if (!Enum.TryParse<CompletedTorrentCleanupMode>(request.CompletedTorrentCleanupMode, ignoreCase: true, out var completedTorrentCleanupMode) ||
            !Enum.IsDefined(completedTorrentCleanupMode))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "CompletedTorrentCleanupMode is invalid.",
                StatusCodes.Status400BadRequest,
                nameof(request.CompletedTorrentCleanupMode));
        }

        if (request.SeedingStopRatio <= 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "SeedingStopRatio must be greater than 0.",
                StatusCodes.Status400BadRequest,
                nameof(request.SeedingStopRatio));
        }

        if (request.SeedingStopMinutes < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "SeedingStopMinutes must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.SeedingStopMinutes));
        }

        if (request.CompletedTorrentCleanupMinutes < 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "CompletedTorrentCleanupMinutes must be 0 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.CompletedTorrentCleanupMinutes));
        }

        if (request.EngineConnectionFailureLogBurstLimit < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "EngineConnectionFailureLogBurstLimit must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.EngineConnectionFailureLogBurstLimit));
        }

        if (request.EngineConnectionFailureLogWindowSeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "EngineConnectionFailureLogWindowSeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.EngineConnectionFailureLogWindowSeconds));
        }

        if (request.MaxActiveMetadataResolutions < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "MaxActiveMetadataResolutions must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.MaxActiveMetadataResolutions));
        }

        if (request.MaxActiveDownloads < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "MaxActiveDownloads must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.MaxActiveDownloads));
        }

        await runtimeSettingsStore.UpsertAsync(new Dictionary<string, string>
        {
            [RuntimeSettingsKeys.SeedingStopMode] = seedingStopMode.ToString(),
            [RuntimeSettingsKeys.SeedingStopRatio] = request.SeedingStopRatio.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.SeedingStopMinutes] = request.SeedingStopMinutes.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.CompletedTorrentCleanupMode] = completedTorrentCleanupMode.ToString(),
            [RuntimeSettingsKeys.CompletedTorrentCleanupMinutes] = request.CompletedTorrentCleanupMinutes.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.EngineConnectionFailureLogBurstLimit] = request.EngineConnectionFailureLogBurstLimit.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.EngineConnectionFailureLogWindowSeconds] = request.EngineConnectionFailureLogWindowSeconds.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.MaxActiveMetadataResolutions] = request.MaxActiveMetadataResolutions.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.MaxActiveDownloads] = request.MaxActiveDownloads.ToString(CultureInfo.InvariantCulture),
        }, cancellationToken);

        await activityLogService.WriteAsync(new ActivityLogWriteRequest
        {
            Level = ActivityLogLevel.Information,
            Category = "startup",
            EventType = "service.runtime_settings.updated",
            Message = "Runtime settings were updated.",
            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                seedingStopMode,
                request.SeedingStopRatio,
                request.SeedingStopMinutes,
                completedTorrentCleanupMode,
                request.CompletedTorrentCleanupMinutes,
                request.EngineConnectionFailureLogBurstLimit,
                request.EngineConnectionFailureLogWindowSeconds,
                request.MaxActiveMetadataResolutions,
                request.MaxActiveDownloads,
            }),
        }, cancellationToken);

        return await GetRuntimeSettingsDtoAsync(cancellationToken);
    }

    private static RuntimeSettingsSnapshot BuildSnapshot(TorrentCoreServiceOptions baseOptions, PersistedRuntimeSettingsRecord persistedSettings)
    {
        var values = persistedSettings.Values;

        var seedingStopMode = baseOptions.SeedingStopMode;
        if (values.TryGetValue(RuntimeSettingsKeys.SeedingStopMode, out var seedingStopModeValue) &&
            Enum.TryParse<SeedingStopMode>(seedingStopModeValue, ignoreCase: true, out var parsedSeedingStopMode) &&
            Enum.IsDefined(parsedSeedingStopMode))
        {
            seedingStopMode = parsedSeedingStopMode;
        }

        var seedingStopRatio = baseOptions.SeedingStopRatio;
        if (values.TryGetValue(RuntimeSettingsKeys.SeedingStopRatio, out var seedingStopRatioValue) &&
            double.TryParse(seedingStopRatioValue, CultureInfo.InvariantCulture, out var parsedSeedingStopRatio) &&
            parsedSeedingStopRatio > 0)
        {
            seedingStopRatio = parsedSeedingStopRatio;
        }

        var seedingStopMinutes = baseOptions.SeedingStopMinutes;
        if (values.TryGetValue(RuntimeSettingsKeys.SeedingStopMinutes, out var seedingStopMinutesValue) &&
            int.TryParse(seedingStopMinutesValue, CultureInfo.InvariantCulture, out var parsedSeedingStopMinutes) &&
            parsedSeedingStopMinutes > 0)
        {
            seedingStopMinutes = parsedSeedingStopMinutes;
        }

        var completedTorrentCleanupMode = baseOptions.CompletedTorrentCleanupMode;
        if (values.TryGetValue(RuntimeSettingsKeys.CompletedTorrentCleanupMode, out var completedTorrentCleanupModeValue) &&
            Enum.TryParse<CompletedTorrentCleanupMode>(completedTorrentCleanupModeValue, ignoreCase: true, out var parsedCompletedTorrentCleanupMode) &&
            Enum.IsDefined(parsedCompletedTorrentCleanupMode))
        {
            completedTorrentCleanupMode = parsedCompletedTorrentCleanupMode;
        }

        var completedTorrentCleanupMinutes = baseOptions.CompletedTorrentCleanupMinutes;
        if (values.TryGetValue(RuntimeSettingsKeys.CompletedTorrentCleanupMinutes, out var completedTorrentCleanupMinutesValue) &&
            int.TryParse(completedTorrentCleanupMinutesValue, CultureInfo.InvariantCulture, out var parsedCompletedTorrentCleanupMinutes) &&
            parsedCompletedTorrentCleanupMinutes >= 0)
        {
            completedTorrentCleanupMinutes = parsedCompletedTorrentCleanupMinutes;
        }

        var burstLimit = baseOptions.EngineConnectionFailureLogBurstLimit;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineConnectionFailureLogBurstLimit, out var burstLimitValue) &&
            int.TryParse(burstLimitValue, CultureInfo.InvariantCulture, out var parsedBurstLimit) &&
            parsedBurstLimit > 0)
        {
            burstLimit = parsedBurstLimit;
        }

        var windowSeconds = baseOptions.EngineConnectionFailureLogWindowSeconds;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineConnectionFailureLogWindowSeconds, out var windowSecondsValue) &&
            int.TryParse(windowSecondsValue, CultureInfo.InvariantCulture, out var parsedWindowSeconds) &&
            parsedWindowSeconds > 0)
        {
            windowSeconds = parsedWindowSeconds;
        }

        var maxActiveMetadataResolutions = baseOptions.MaxActiveMetadataResolutions;
        if (values.TryGetValue(RuntimeSettingsKeys.MaxActiveMetadataResolutions, out var maxActiveMetadataResolutionsValue) &&
            int.TryParse(maxActiveMetadataResolutionsValue, CultureInfo.InvariantCulture, out var parsedMaxActiveMetadataResolutions) &&
            parsedMaxActiveMetadataResolutions > 0)
        {
            maxActiveMetadataResolutions = parsedMaxActiveMetadataResolutions;
        }

        var maxActiveDownloads = baseOptions.MaxActiveDownloads;
        if (values.TryGetValue(RuntimeSettingsKeys.MaxActiveDownloads, out var maxActiveDownloadsValue) &&
            int.TryParse(maxActiveDownloadsValue, CultureInfo.InvariantCulture, out var parsedMaxActiveDownloads) &&
            parsedMaxActiveDownloads > 0)
        {
            maxActiveDownloads = parsedMaxActiveDownloads;
        }

        return new RuntimeSettingsSnapshot
        {
            UsesPersistedOverrides = persistedSettings.Values.Count > 0,
            PartialFilesEnabled = baseOptions.UsePartialFiles,
            PartialFileSuffix = baseOptions.UsePartialFiles ? ".!mt" : string.Empty,
            SeedingStopMode = seedingStopMode,
            SeedingStopRatio = seedingStopRatio,
            SeedingStopMinutes = seedingStopMinutes,
            CompletedTorrentCleanupMode = completedTorrentCleanupMode,
            CompletedTorrentCleanupMinutes = completedTorrentCleanupMinutes,
            EngineConnectionFailureLogBurstLimit = burstLimit,
            EngineConnectionFailureLogWindowSeconds = windowSeconds,
            MaxActiveMetadataResolutions = maxActiveMetadataResolutions,
            MaxActiveDownloads = maxActiveDownloads,
            UpdatedAtUtc = persistedSettings.UpdatedAtUtc,
        };
    }

    private static RuntimeSettingsDto MapDto(TorrentCoreServiceOptions baseOptions, RuntimeSettingsSnapshot settings)
    {
        return new RuntimeSettingsDto
        {
            EngineRuntime = baseOptions.EngineMode.ToString(),
            SupportsLiveUpdates = true,
            UsesPersistedOverrides = settings.UsesPersistedOverrides,
            PartialFilesEnabled = settings.PartialFilesEnabled,
            PartialFileSuffix = settings.PartialFileSuffix,
            SeedingStopMode = settings.SeedingStopMode.ToString(),
            SeedingStopRatio = settings.SeedingStopRatio,
            SeedingStopMinutes = settings.SeedingStopMinutes,
            CompletedTorrentCleanupMode = settings.CompletedTorrentCleanupMode.ToString(),
            CompletedTorrentCleanupMinutes = settings.CompletedTorrentCleanupMinutes,
            EngineConnectionFailureLogBurstLimit = settings.EngineConnectionFailureLogBurstLimit,
            EngineConnectionFailureLogWindowSeconds = settings.EngineConnectionFailureLogWindowSeconds,
            MaxActiveMetadataResolutions = settings.MaxActiveMetadataResolutions,
            MaxActiveDownloads = settings.MaxActiveDownloads,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
