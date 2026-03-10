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

        await runtimeSettingsStore.UpsertAsync(new Dictionary<string, string>
        {
            [RuntimeSettingsKeys.SeedingStopMode] = seedingStopMode.ToString(),
            [RuntimeSettingsKeys.SeedingStopRatio] = request.SeedingStopRatio.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.SeedingStopMinutes] = request.SeedingStopMinutes.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.EngineConnectionFailureLogBurstLimit] = request.EngineConnectionFailureLogBurstLimit.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.EngineConnectionFailureLogWindowSeconds] = request.EngineConnectionFailureLogWindowSeconds.ToString(CultureInfo.InvariantCulture),
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
                request.EngineConnectionFailureLogBurstLimit,
                request.EngineConnectionFailureLogWindowSeconds,
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

        return new RuntimeSettingsSnapshot
        {
            UsesPersistedOverrides = persistedSettings.Values.Count > 0,
            PartialFilesEnabled = baseOptions.UsePartialFiles,
            PartialFileSuffix = baseOptions.UsePartialFiles ? ".!mt" : string.Empty,
            SeedingStopMode = seedingStopMode,
            SeedingStopRatio = seedingStopRatio,
            SeedingStopMinutes = seedingStopMinutes,
            EngineConnectionFailureLogBurstLimit = burstLimit,
            EngineConnectionFailureLogWindowSeconds = windowSeconds,
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
            EngineConnectionFailureLogBurstLimit = settings.EngineConnectionFailureLogBurstLimit,
            EngineConnectionFailureLogWindowSeconds = settings.EngineConnectionFailureLogWindowSeconds,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
