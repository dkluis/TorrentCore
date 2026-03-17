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
    ServiceInstanceContext serviceInstanceContext,
    AppliedEngineSettingsState appliedEngineSettingsState) : IRuntimeSettingsService
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

        if (request.EngineMaximumConnections < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "EngineMaximumConnections must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.EngineMaximumConnections));
        }

        if (request.EngineMaximumHalfOpenConnections < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "EngineMaximumHalfOpenConnections must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.EngineMaximumHalfOpenConnections));
        }

        if (request.EngineMaximumDownloadRateBytesPerSecond < 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "EngineMaximumDownloadRateBytesPerSecond must be 0 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.EngineMaximumDownloadRateBytesPerSecond));
        }

        if (request.EngineMaximumUploadRateBytesPerSecond < 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "EngineMaximumUploadRateBytesPerSecond must be 0 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.EngineMaximumUploadRateBytesPerSecond));
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

        var currentSettings = await GetEffectiveSettingsAsync(cancellationToken);
        var completionCallbackEnabled = request.CompletionCallbackEnabled ?? currentSettings.CompletionCallbackEnabled;
        var completionCallbackCommandPath = request.CompletionCallbackCommandPath is null
            ? currentSettings.CompletionCallbackCommandPath
            : NormalizeOptionalText(request.CompletionCallbackCommandPath);
        var completionCallbackArguments = request.CompletionCallbackArguments is null
            ? currentSettings.CompletionCallbackArguments
            : NormalizeOptionalText(request.CompletionCallbackArguments);
        var completionCallbackWorkingDirectory = request.CompletionCallbackWorkingDirectory is null
            ? currentSettings.CompletionCallbackWorkingDirectory
            : NormalizeOptionalText(request.CompletionCallbackWorkingDirectory);
        var completionCallbackTimeoutSeconds = request.CompletionCallbackTimeoutSeconds ?? currentSettings.CompletionCallbackTimeoutSeconds;
        var completionCallbackFinalizationTimeoutSeconds = request.CompletionCallbackFinalizationTimeoutSeconds ?? currentSettings.CompletionCallbackFinalizationTimeoutSeconds;
        var completionCallbackApiBaseUrlOverride = request.CompletionCallbackApiBaseUrlOverride is null
            ? currentSettings.CompletionCallbackApiBaseUrlOverride
            : NormalizeOptionalText(request.CompletionCallbackApiBaseUrlOverride);
        var completionCallbackApiKeyOverride = request.CompletionCallbackApiKeyOverride is null
            ? currentSettings.CompletionCallbackApiKeyOverride
            : NormalizeOptionalText(request.CompletionCallbackApiKeyOverride);

        if (completionCallbackTimeoutSeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "CompletionCallbackTimeoutSeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.CompletionCallbackTimeoutSeconds));
        }

        if (completionCallbackFinalizationTimeoutSeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "CompletionCallbackFinalizationTimeoutSeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.CompletionCallbackFinalizationTimeoutSeconds));
        }

        if (completionCallbackEnabled && string.IsNullOrWhiteSpace(completionCallbackCommandPath))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "CompletionCallbackCommandPath is required when CompletionCallbackEnabled is true.",
                StatusCodes.Status400BadRequest,
                nameof(request.CompletionCallbackCommandPath));
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
            [RuntimeSettingsKeys.EngineMaximumConnections] = request.EngineMaximumConnections.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.EngineMaximumHalfOpenConnections] = request.EngineMaximumHalfOpenConnections.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.EngineMaximumDownloadRateBytesPerSecond] = request.EngineMaximumDownloadRateBytesPerSecond.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.EngineMaximumUploadRateBytesPerSecond] = request.EngineMaximumUploadRateBytesPerSecond.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.MaxActiveMetadataResolutions] = request.MaxActiveMetadataResolutions.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.MaxActiveDownloads] = request.MaxActiveDownloads.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.CompletionCallbackEnabled] = completionCallbackEnabled.ToString(),
            [RuntimeSettingsKeys.CompletionCallbackCommandPath] = completionCallbackCommandPath ?? string.Empty,
            [RuntimeSettingsKeys.CompletionCallbackArguments] = completionCallbackArguments ?? string.Empty,
            [RuntimeSettingsKeys.CompletionCallbackWorkingDirectory] = completionCallbackWorkingDirectory ?? string.Empty,
            [RuntimeSettingsKeys.CompletionCallbackTimeoutSeconds] = completionCallbackTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.CompletionCallbackFinalizationTimeoutSeconds] = completionCallbackFinalizationTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            [RuntimeSettingsKeys.CompletionCallbackApiBaseUrlOverride] = completionCallbackApiBaseUrlOverride ?? string.Empty,
            [RuntimeSettingsKeys.CompletionCallbackApiKeyOverride] = completionCallbackApiKeyOverride ?? string.Empty,
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
                request.EngineMaximumConnections,
                request.EngineMaximumHalfOpenConnections,
                request.EngineMaximumDownloadRateBytesPerSecond,
                request.EngineMaximumUploadRateBytesPerSecond,
                request.MaxActiveMetadataResolutions,
                request.MaxActiveDownloads,
                completionCallbackEnabled,
                completionCallbackCommandPath,
                completionCallbackWorkingDirectory,
                completionCallbackTimeoutSeconds,
                completionCallbackFinalizationTimeoutSeconds,
                completionCallbackApiBaseUrlOverride,
            }),
        }, cancellationToken);

        return await GetRuntimeSettingsDtoAsync(cancellationToken);
    }

    private RuntimeSettingsSnapshot BuildSnapshot(TorrentCoreServiceOptions baseOptions, PersistedRuntimeSettingsRecord persistedSettings)
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

        var engineMaximumConnections = baseOptions.EngineMaximumConnections;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineMaximumConnections, out var engineMaximumConnectionsValue) &&
            int.TryParse(engineMaximumConnectionsValue, CultureInfo.InvariantCulture, out var parsedEngineMaximumConnections) &&
            parsedEngineMaximumConnections > 0)
        {
            engineMaximumConnections = parsedEngineMaximumConnections;
        }

        var engineMaximumHalfOpenConnections = baseOptions.EngineMaximumHalfOpenConnections;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineMaximumHalfOpenConnections, out var engineMaximumHalfOpenConnectionsValue) &&
            int.TryParse(engineMaximumHalfOpenConnectionsValue, CultureInfo.InvariantCulture, out var parsedEngineMaximumHalfOpenConnections) &&
            parsedEngineMaximumHalfOpenConnections > 0)
        {
            engineMaximumHalfOpenConnections = parsedEngineMaximumHalfOpenConnections;
        }

        var engineMaximumDownloadRateBytesPerSecond = baseOptions.EngineMaximumDownloadRateBytesPerSecond;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineMaximumDownloadRateBytesPerSecond, out var engineMaximumDownloadRateValue) &&
            int.TryParse(engineMaximumDownloadRateValue, CultureInfo.InvariantCulture, out var parsedEngineMaximumDownloadRate) &&
            parsedEngineMaximumDownloadRate >= 0)
        {
            engineMaximumDownloadRateBytesPerSecond = parsedEngineMaximumDownloadRate;
        }

        var engineMaximumUploadRateBytesPerSecond = baseOptions.EngineMaximumUploadRateBytesPerSecond;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineMaximumUploadRateBytesPerSecond, out var engineMaximumUploadRateValue) &&
            int.TryParse(engineMaximumUploadRateValue, CultureInfo.InvariantCulture, out var parsedEngineMaximumUploadRate) &&
            parsedEngineMaximumUploadRate >= 0)
        {
            engineMaximumUploadRateBytesPerSecond = parsedEngineMaximumUploadRate;
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

        var completionCallbackEnabled = baseOptions.CompletionCallbackEnabled;
        if (values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackEnabled, out var completionCallbackEnabledValue) &&
            bool.TryParse(completionCallbackEnabledValue, out var parsedCompletionCallbackEnabled))
        {
            completionCallbackEnabled = parsedCompletionCallbackEnabled;
        }

        var completionCallbackCommandPath = NormalizePersistedText(
            values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackCommandPath, out var completionCallbackCommandPathValue)
                ? completionCallbackCommandPathValue
                : baseOptions.CompletionCallbackCommandPath);
        var completionCallbackArguments = NormalizePersistedText(
            values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackArguments, out var completionCallbackArgumentsValue)
                ? completionCallbackArgumentsValue
                : baseOptions.CompletionCallbackArguments);
        var completionCallbackWorkingDirectory = NormalizePersistedText(
            values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackWorkingDirectory, out var completionCallbackWorkingDirectoryValue)
                ? completionCallbackWorkingDirectoryValue
                : baseOptions.CompletionCallbackWorkingDirectory);

        var completionCallbackTimeoutSeconds = baseOptions.CompletionCallbackTimeoutSeconds;
        if (values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackTimeoutSeconds, out var completionCallbackTimeoutValue) &&
            int.TryParse(completionCallbackTimeoutValue, CultureInfo.InvariantCulture, out var parsedCompletionCallbackTimeout) &&
            parsedCompletionCallbackTimeout > 0)
        {
            completionCallbackTimeoutSeconds = parsedCompletionCallbackTimeout;
        }

        var completionCallbackFinalizationTimeoutSeconds = baseOptions.CompletionCallbackFinalizationTimeoutSeconds;
        if (values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackFinalizationTimeoutSeconds, out var completionCallbackFinalizationTimeoutValue) &&
            int.TryParse(completionCallbackFinalizationTimeoutValue, CultureInfo.InvariantCulture, out var parsedCompletionCallbackFinalizationTimeout) &&
            parsedCompletionCallbackFinalizationTimeout > 0)
        {
            completionCallbackFinalizationTimeoutSeconds = parsedCompletionCallbackFinalizationTimeout;
        }

        var completionCallbackApiBaseUrlOverride = NormalizePersistedText(
            values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackApiBaseUrlOverride, out var completionCallbackApiBaseUrlOverrideValue)
                ? completionCallbackApiBaseUrlOverrideValue
                : baseOptions.CompletionCallbackApiBaseUrlOverride);
        var completionCallbackApiKeyOverride = NormalizePersistedText(
            values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackApiKeyOverride, out var completionCallbackApiKeyOverrideValue)
                ? completionCallbackApiKeyOverrideValue
                : baseOptions.CompletionCallbackApiKeyOverride);

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
            EngineMaximumConnections = engineMaximumConnections,
            EngineMaximumHalfOpenConnections = engineMaximumHalfOpenConnections,
            EngineMaximumDownloadRateBytesPerSecond = engineMaximumDownloadRateBytesPerSecond,
            EngineMaximumUploadRateBytesPerSecond = engineMaximumUploadRateBytesPerSecond,
            MaxActiveMetadataResolutions = maxActiveMetadataResolutions,
            MaxActiveDownloads = maxActiveDownloads,
            CompletionCallbackEnabled = completionCallbackEnabled,
            CompletionCallbackCommandPath = completionCallbackCommandPath,
            CompletionCallbackArguments = completionCallbackArguments,
            CompletionCallbackWorkingDirectory = completionCallbackWorkingDirectory,
            CompletionCallbackTimeoutSeconds = completionCallbackTimeoutSeconds,
            CompletionCallbackFinalizationTimeoutSeconds = completionCallbackFinalizationTimeoutSeconds,
            CompletionCallbackApiBaseUrlOverride = completionCallbackApiBaseUrlOverride,
            CompletionCallbackApiKeyOverride = completionCallbackApiKeyOverride,
            EngineSettingsRequireRestart =
                engineMaximumConnections != appliedEngineSettingsState.EngineMaximumConnections ||
                engineMaximumHalfOpenConnections != appliedEngineSettingsState.EngineMaximumHalfOpenConnections ||
                engineMaximumDownloadRateBytesPerSecond != appliedEngineSettingsState.EngineMaximumDownloadRateBytesPerSecond ||
                engineMaximumUploadRateBytesPerSecond != appliedEngineSettingsState.EngineMaximumUploadRateBytesPerSecond,
            UpdatedAtUtc = persistedSettings.UpdatedAtUtc,
        };
    }

    private RuntimeSettingsDto MapDto(TorrentCoreServiceOptions baseOptions, RuntimeSettingsSnapshot settings)
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
            EngineMaximumConnections = settings.EngineMaximumConnections,
            EngineMaximumHalfOpenConnections = settings.EngineMaximumHalfOpenConnections,
            EngineMaximumDownloadRateBytesPerSecond = settings.EngineMaximumDownloadRateBytesPerSecond,
            EngineMaximumUploadRateBytesPerSecond = settings.EngineMaximumUploadRateBytesPerSecond,
            MaxActiveMetadataResolutions = settings.MaxActiveMetadataResolutions,
            MaxActiveDownloads = settings.MaxActiveDownloads,
            CompletionCallbackEnabled = settings.CompletionCallbackEnabled,
            CompletionCallbackCommandPath = settings.CompletionCallbackCommandPath,
            CompletionCallbackArguments = settings.CompletionCallbackArguments,
            CompletionCallbackWorkingDirectory = settings.CompletionCallbackWorkingDirectory,
            CompletionCallbackTimeoutSeconds = settings.CompletionCallbackTimeoutSeconds,
            CompletionCallbackFinalizationTimeoutSeconds = settings.CompletionCallbackFinalizationTimeoutSeconds,
            CompletionCallbackApiBaseUrlOverride = settings.CompletionCallbackApiBaseUrlOverride,
            CompletionCallbackApiKeyOverride = settings.CompletionCallbackApiKeyOverride,
            AppliedEngineMaximumConnections = appliedEngineSettingsState.EngineMaximumConnections,
            AppliedEngineMaximumHalfOpenConnections = appliedEngineSettingsState.EngineMaximumHalfOpenConnections,
            AppliedEngineMaximumDownloadRateBytesPerSecond = appliedEngineSettingsState.EngineMaximumDownloadRateBytesPerSecond,
            AppliedEngineMaximumUploadRateBytesPerSecond = appliedEngineSettingsState.EngineMaximumUploadRateBytesPerSecond,
            EngineSettingsRequireRestart = settings.EngineSettingsRequireRestart,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static string? NormalizePersistedText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
