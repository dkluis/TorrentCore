#region

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Host;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Persistence.Sqlite.Configuration;
using TorrentCore.Service.Application;
using TorrentCore.Service.Vpn;

#endregion

namespace TorrentCore.Service.Configuration;

public sealed class RuntimeSettingsService(IOptions<TorrentCoreServiceOptions> serviceOptions,
    SqliteRuntimeSettingsStore runtimeSettingsStore, IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext, AppliedEngineSettingsState appliedEngineSettingsState,
    VpnSettingsChangeSignal vpnSettingsChangeSignal)
        : IRuntimeSettingsService
{
    public async Task<RuntimeSettingsSnapshot> GetEffectiveSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var persistedSettings = await runtimeSettingsStore.GetAsync(cancellationToken);
            return BuildSnapshot(serviceOptions.Value, persistedSettings);
        }
        catch (Exception exception) when (ServiceBoundaryExceptionMapper.IsStorageException(exception))
        {
            throw ServiceBoundaryExceptionMapper.CreateStorageUnavailable(
                "TorrentCore runtime settings storage is unavailable.",
                null
            );
        }
    }

    public async Task<RuntimeSettingsDto> GetRuntimeSettingsDtoAsync(CancellationToken cancellationToken)
    {
        var settings = await GetEffectiveSettingsAsync(cancellationToken);
        return MapDto(serviceOptions.Value, settings);
    }

    public async Task<RuntimeSettingsDto> UpdateAsync(UpdateRuntimeSettingsRequest request,
        CancellationToken                                                          cancellationToken)
    {
        if (!Enum.TryParse<SeedingStopMode>(request.SeedingStopMode, true, out var seedingStopMode) ||
            !Enum.IsDefined(seedingStopMode))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "SeedingStopMode is invalid.", StatusCodes.Status400BadRequest,
                nameof(request.SeedingStopMode)
            );
        }

        if (!Enum.TryParse<CompletedTorrentCleanupMode>(
                request.CompletedTorrentCleanupMode, true, out var completedTorrentCleanupMode
            ) || !Enum.IsDefined(completedTorrentCleanupMode))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "CompletedTorrentCleanupMode is invalid.", StatusCodes.Status400BadRequest,
                nameof(request.CompletedTorrentCleanupMode)
            );
        }

        if (request.SeedingStopRatio <= 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "SeedingStopRatio must be greater than 0.", StatusCodes.Status400BadRequest,
                nameof(request.SeedingStopRatio)
            );
        }

        if (request.SeedingStopMinutes < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "SeedingStopMinutes must be 1 or greater.", StatusCodes.Status400BadRequest,
                nameof(request.SeedingStopMinutes)
            );
        }

        if (request.CompletedTorrentCleanupMinutes < 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "CompletedTorrentCleanupMinutes must be 0 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.CompletedTorrentCleanupMinutes)
            );
        }

        if (request.EngineConnectionFailureLogBurstLimit < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "EngineConnectionFailureLogBurstLimit must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.EngineConnectionFailureLogBurstLimit)
            );
        }

        if (request.EngineConnectionFailureLogWindowSeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "EngineConnectionFailureLogWindowSeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.EngineConnectionFailureLogWindowSeconds)
            );
        }

        if (!Enum.TryParse<TorrentEncryptionMode>(request.EngineEncryptionMode, true, out var engineEncryptionMode) ||
            !Enum.IsDefined(engineEncryptionMode))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "EngineEncryptionMode is invalid.", StatusCodes.Status400BadRequest,
                nameof(request.EngineEncryptionMode)
            );
        }

        if (request.EngineMaximumConnections < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "EngineMaximumConnections must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.EngineMaximumConnections)
            );
        }

        if (request.EngineMaximumHalfOpenConnections < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "EngineMaximumHalfOpenConnections must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.EngineMaximumHalfOpenConnections)
            );
        }

        if (request.EngineMaximumDownloadRateBytesPerSecond < 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "EngineMaximumDownloadRateBytesPerSecond must be 0 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.EngineMaximumDownloadRateBytesPerSecond)
            );
        }

        if (request.EngineMaximumUploadRateBytesPerSecond < 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "EngineMaximumUploadRateBytesPerSecond must be 0 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.EngineMaximumUploadRateBytesPerSecond)
            );
        }

        if (request.MaxActiveMetadataResolutions < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "MaxActiveMetadataResolutions must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.MaxActiveMetadataResolutions)
            );
        }

        if (request.MaxActiveDownloads < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "MaxActiveDownloads must be 1 or greater.", StatusCodes.Status400BadRequest,
                nameof(request.MaxActiveDownloads)
            );
        }

        if (request.MetadataRefreshStaleSeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "MetadataRefreshStaleSeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.MetadataRefreshStaleSeconds)
            );
        }

        if (request.MetadataRefreshRestartDelaySeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "MetadataRefreshRestartDelaySeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.MetadataRefreshRestartDelaySeconds)
            );
        }

        if (request.ColdDownloadRecoveryThresholdMinutes < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "ColdDownloadRecoveryThresholdMinutes must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.ColdDownloadRecoveryThresholdMinutes)
            );
        }

        if (request.ColdDownloadRecoveryIntervalMinutes < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "ColdDownloadRecoveryIntervalMinutes must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.ColdDownloadRecoveryIntervalMinutes)
            );
        }

        if (request.ColdDownloadAbandonAfterHours < 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "ColdDownloadAbandonAfterHours must be 0 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.ColdDownloadAbandonAfterHours)
            );
        }

        var currentSettings = await GetEffectiveSettingsAsync(cancellationToken);
        var automaticMetadataResetStuckThresholdSeconds =
                request.AutomaticMetadataResetStuckThresholdSeconds ??
                currentSettings.AutomaticMetadataResetStuckThresholdSeconds;
        var metadataResolutionTimeSliceMinutes = request.MetadataResolutionTimeSliceMinutes ??
                currentSettings.MetadataResolutionTimeSliceMinutes;
        var engineAllowPeerExchange = request.EngineAllowPeerExchange ?? currentSettings.EngineAllowPeerExchange;
        var completionCallbackEnabled = request.CompletionCallbackEnabled ?? currentSettings.CompletionCallbackEnabled;
        var completionCallbackCommandPath = request.CompletionCallbackCommandPath is null ?
                currentSettings.CompletionCallbackCommandPath :
                NormalizeOptionalText(request.CompletionCallbackCommandPath);
        var completionCallbackArguments = request.CompletionCallbackArguments is null ?
                currentSettings.CompletionCallbackArguments :
                NormalizeOptionalText(request.CompletionCallbackArguments);
        var completionCallbackWorkingDirectory = request.CompletionCallbackWorkingDirectory is null ?
                currentSettings.CompletionCallbackWorkingDirectory :
                NormalizeOptionalText(request.CompletionCallbackWorkingDirectory);
        var completionCallbackTimeoutSeconds = request.CompletionCallbackTimeoutSeconds ??
                currentSettings.CompletionCallbackTimeoutSeconds;
        var completionCallbackFinalizationTimeoutSeconds = request.CompletionCallbackFinalizationTimeoutSeconds ??
                currentSettings.CompletionCallbackFinalizationTimeoutSeconds;
        var completionCallbackApiBaseUrlOverride = request.CompletionCallbackApiBaseUrlOverride is null ?
                currentSettings.CompletionCallbackApiBaseUrlOverride :
                NormalizeOptionalText(request.CompletionCallbackApiBaseUrlOverride);
        var completionCallbackApiKeyOverride = request.CompletionCallbackApiKeyOverride is null ?
                currentSettings.CompletionCallbackApiKeyOverride :
                NormalizeOptionalText(request.CompletionCallbackApiKeyOverride);
        var vpnEgressValidationEnabled = request.VpnEgressValidationEnabled ??
                currentSettings.VpnEgressValidationEnabled;
        var vpnEgressValidationEndpointValue = request.VpnEgressValidationEndpoint ??
                currentSettings.VpnEgressValidationEndpoint;
        var vpnEgressDirectIspCidrValues = request.VpnEgressDirectIspCidrs ??
                currentSettings.VpnEgressDirectIspCidrs;
        var vpnEgressDegradedCheckIntervalSeconds = request.VpnEgressDegradedCheckIntervalSeconds ??
                currentSettings.VpnEgressDegradedCheckIntervalSeconds;
        var vpnEgressReadyCheckIntervalSeconds = request.VpnEgressReadyCheckIntervalSeconds ??
                currentSettings.VpnEgressReadyCheckIntervalSeconds;
        var vpnEgressRequestTimeoutSeconds = request.VpnEgressRequestTimeoutSeconds ??
                currentSettings.VpnEgressRequestTimeoutSeconds;
        var vpnEgressEngineSuspensionTimeoutSeconds = request.VpnEgressEngineSuspensionTimeoutSeconds ??
                currentSettings.VpnEgressEngineSuspensionTimeoutSeconds;

        if (!VpnEgressSettingsValidation.TryNormalizeEndpoint(
                vpnEgressValidationEndpointValue, out var vpnEgressValidationEndpoint, out var vpnEndpointError))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", vpnEndpointError, StatusCodes.Status400BadRequest,
                nameof(request.VpnEgressValidationEndpoint)
            );
        }

        if (!VpnEgressSettingsValidation.TryNormalizeCidrs(
                vpnEgressDirectIspCidrValues, out var vpnEgressDirectIspCidrs, out var vpnCidrsError))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", vpnCidrsError, StatusCodes.Status400BadRequest,
                nameof(request.VpnEgressDirectIspCidrs)
            );
        }

        if (vpnEgressValidationEnabled && vpnEgressDirectIspCidrs.Count == 0)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "VpnEgressDirectIspCidrs requires at least one IPv4 CIDR when VPN egress validation is enabled.",
                StatusCodes.Status400BadRequest,
                nameof(request.VpnEgressDirectIspCidrs)
            );
        }

        if (!VpnEgressSettingsValidation.TryValidateIntervals(
                vpnEgressDegradedCheckIntervalSeconds,
                vpnEgressReadyCheckIntervalSeconds,
                vpnEgressRequestTimeoutSeconds,
                out var vpnIntervalsError,
                out var vpnIntervalsTarget))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", vpnIntervalsError, StatusCodes.Status400BadRequest, vpnIntervalsTarget
            );
        }

        if (vpnEgressEngineSuspensionTimeoutSeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "VpnEgressEngineSuspensionTimeoutSeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.VpnEgressEngineSuspensionTimeoutSeconds)
            );
        }

        if (automaticMetadataResetStuckThresholdSeconds is
            < TorrentCoreServiceOptions.MinimumAutomaticMetadataResetStuckThresholdSeconds or
            > TorrentCoreServiceOptions.MaximumAutomaticMetadataResetStuckThresholdSeconds)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                $"AutomaticMetadataResetStuckThresholdSeconds must be between {TorrentCoreServiceOptions.MinimumAutomaticMetadataResetStuckThresholdSeconds} and {TorrentCoreServiceOptions.MaximumAutomaticMetadataResetStuckThresholdSeconds}.",
                StatusCodes.Status400BadRequest,
                nameof(request.AutomaticMetadataResetStuckThresholdSeconds)
            );
        }

        if (metadataResolutionTimeSliceMinutes is
            < TorrentCoreServiceOptions.MinimumMetadataResolutionTimeSliceMinutes or
            > TorrentCoreServiceOptions.MaximumMetadataResolutionTimeSliceMinutes)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                $"MetadataResolutionTimeSliceMinutes must be between {TorrentCoreServiceOptions.MinimumMetadataResolutionTimeSliceMinutes} and {TorrentCoreServiceOptions.MaximumMetadataResolutionTimeSliceMinutes}.",
                StatusCodes.Status400BadRequest,
                nameof(request.MetadataResolutionTimeSliceMinutes)
            );
        }

        if (completionCallbackTimeoutSeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "CompletionCallbackTimeoutSeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.CompletionCallbackTimeoutSeconds)
            );
        }

        if (completionCallbackFinalizationTimeoutSeconds < 1)
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings", "CompletionCallbackFinalizationTimeoutSeconds must be 1 or greater.",
                StatusCodes.Status400BadRequest, nameof(request.CompletionCallbackFinalizationTimeoutSeconds)
            );
        }

        if (completionCallbackEnabled && string.IsNullOrWhiteSpace(completionCallbackCommandPath))
        {
            throw new Application.ServiceOperationException(
                "invalid_runtime_settings",
                "CompletionCallbackCommandPath is required when CompletionCallbackEnabled is true.",
                StatusCodes.Status400BadRequest, nameof(request.CompletionCallbackCommandPath)
            );
        }

        try
        {
            await runtimeSettingsStore.UpsertAsync(
                new Dictionary<string, string>
                {
                    [RuntimeSettingsKeys.SeedingStopMode] = seedingStopMode.ToString(),
                    [RuntimeSettingsKeys.SeedingStopRatio] =
                            request.SeedingStopRatio.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.SeedingStopMinutes] =
                            request.SeedingStopMinutes.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.CompletedTorrentCleanupMode] = completedTorrentCleanupMode.ToString(),
                    [RuntimeSettingsKeys.CompletedTorrentCleanupMinutes] =
                            request.CompletedTorrentCleanupMinutes.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.DeleteLogsForCompletedTorrents] =
                            request.DeleteLogsForCompletedTorrents.ToString(),
                    [RuntimeSettingsKeys.EngineConnectionFailureLogBurstLimit] =
                            request.EngineConnectionFailureLogBurstLimit.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.EngineConnectionFailureLogWindowSeconds] =
                            request.EngineConnectionFailureLogWindowSeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.EngineAllowPeerExchange] = engineAllowPeerExchange.ToString(),
                    [RuntimeSettingsKeys.EngineEncryptionMode] = engineEncryptionMode.ToString(),
                    [RuntimeSettingsKeys.EngineMaximumConnections] =
                            request.EngineMaximumConnections.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.EngineMaximumHalfOpenConnections] =
                            request.EngineMaximumHalfOpenConnections.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.EngineMaximumDownloadRateBytesPerSecond] =
                            request.EngineMaximumDownloadRateBytesPerSecond.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.EngineMaximumUploadRateBytesPerSecond] =
                            request.EngineMaximumUploadRateBytesPerSecond.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.MaxActiveMetadataResolutions] =
                            request.MaxActiveMetadataResolutions.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.MaxActiveDownloads] =
                            request.MaxActiveDownloads.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.MetadataRefreshStaleSeconds] =
                            request.MetadataRefreshStaleSeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.MetadataRefreshRestartDelaySeconds] =
                            request.MetadataRefreshRestartDelaySeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.MetadataResolutionTimeSliceMinutes] =
                            metadataResolutionTimeSliceMinutes.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.AutomaticMetadataResetStuckThresholdSeconds] =
                            automaticMetadataResetStuckThresholdSeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.ColdDownloadRecoveryThresholdMinutes] =
                            request.ColdDownloadRecoveryThresholdMinutes.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.ColdDownloadRecoveryIntervalMinutes] =
                            request.ColdDownloadRecoveryIntervalMinutes.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.ColdDownloadAbandonAfterHours] =
                            request.ColdDownloadAbandonAfterHours.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.CompletionCallbackEnabled]     = completionCallbackEnabled.ToString(),
                    [RuntimeSettingsKeys.CompletionCallbackCommandPath] = completionCallbackCommandPath ?? string.Empty,
                    [RuntimeSettingsKeys.CompletionCallbackArguments]   = completionCallbackArguments   ?? string.Empty,
                    [RuntimeSettingsKeys.CompletionCallbackWorkingDirectory] =
                            completionCallbackWorkingDirectory ?? string.Empty,
                    [RuntimeSettingsKeys.CompletionCallbackTimeoutSeconds] =
                            completionCallbackTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.CompletionCallbackFinalizationTimeoutSeconds] =
                            completionCallbackFinalizationTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.CompletionCallbackApiBaseUrlOverride] =
                            completionCallbackApiBaseUrlOverride ?? string.Empty,
                    [RuntimeSettingsKeys.CompletionCallbackApiKeyOverride] =
                            completionCallbackApiKeyOverride ?? string.Empty,
                    [RuntimeSettingsKeys.VpnEgressValidationEnabled] = vpnEgressValidationEnabled.ToString(),
                    [RuntimeSettingsKeys.VpnEgressValidationEndpoint] = vpnEgressValidationEndpoint,
                    [RuntimeSettingsKeys.VpnEgressDirectIspCidrs] = JsonSerializer.Serialize(vpnEgressDirectIspCidrs),
                    [RuntimeSettingsKeys.VpnEgressDegradedCheckIntervalSeconds] =
                            vpnEgressDegradedCheckIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.VpnEgressReadyCheckIntervalSeconds] =
                            vpnEgressReadyCheckIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.VpnEgressRequestTimeoutSeconds] =
                            vpnEgressRequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                    [RuntimeSettingsKeys.VpnEgressEngineSuspensionTimeoutSeconds] =
                            vpnEgressEngineSuspensionTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                }, cancellationToken
            );
        }
        catch (Exception exception) when (ServiceBoundaryExceptionMapper.IsStorageException(exception))
        {
            throw ServiceBoundaryExceptionMapper.CreateStorageUnavailable(
                "TorrentCore runtime settings storage is unavailable.",
                null
            );
        }

        vpnSettingsChangeSignal.Notify();

        try
        {
            await activityLogService.WriteAsync(
                new ActivityLogWriteRequest
                {
                    Level             = ActivityLogLevel.Information,
                    Category          = "startup",
                    EventType         = "service.runtime_settings.updated",
                    Message           = "Runtime settings were updated.",
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            seedingStopMode,
                            request.SeedingStopRatio,
                            request.SeedingStopMinutes,
                            completedTorrentCleanupMode,
                            request.CompletedTorrentCleanupMinutes,
                            request.DeleteLogsForCompletedTorrents,
                            request.EngineConnectionFailureLogBurstLimit,
                            request.EngineConnectionFailureLogWindowSeconds,
                            engineAllowPeerExchange,
                            engineEncryptionMode,
                            request.EngineMaximumConnections,
                            request.EngineMaximumHalfOpenConnections,
                            request.EngineMaximumDownloadRateBytesPerSecond,
                            request.EngineMaximumUploadRateBytesPerSecond,
                            request.MaxActiveMetadataResolutions,
                            request.MaxActiveDownloads,
                            request.MetadataRefreshStaleSeconds,
                            request.MetadataRefreshRestartDelaySeconds,
                            automaticMetadataResetStuckThresholdSeconds,
                            request.ColdDownloadRecoveryThresholdMinutes,
                            request.ColdDownloadRecoveryIntervalMinutes,
                            request.ColdDownloadAbandonAfterHours,
                            completionCallbackEnabled,
                            completionCallbackCommandPath,
                            completionCallbackWorkingDirectory,
                            completionCallbackTimeoutSeconds,
                            completionCallbackFinalizationTimeoutSeconds,
                            completionCallbackApiBaseUrlOverride,
                            vpnEgressValidationEnabled,
                            vpnEgressValidationEndpointAuthority =
                                    new Uri(vpnEgressValidationEndpoint).GetLeftPart(UriPartial.Authority),
                            vpnEgressDirectIspCidrs,
                            vpnEgressDegradedCheckIntervalSeconds,
                            vpnEgressReadyCheckIntervalSeconds,
                            vpnEgressRequestTimeoutSeconds,
                            vpnEgressEngineSuspensionTimeoutSeconds,
                        }
                    ),
                }, cancellationToken
            );
        }
        catch (Exception exception) when (ServiceBoundaryExceptionMapper.IsStorageException(exception))
        {
            // The settings change has already been persisted.
        }

        return await GetRuntimeSettingsDtoAsync(cancellationToken);
    }

    private RuntimeSettingsSnapshot BuildSnapshot(TorrentCoreServiceOptions baseOptions,
        PersistedRuntimeSettingsRecord                                      persistedSettings)
    {
        var values = persistedSettings.Values;

        var seedingStopMode = baseOptions.SeedingStopMode;
        if (values.TryGetValue(RuntimeSettingsKeys.SeedingStopMode, out var seedingStopModeValue)     &&
            Enum.TryParse<SeedingStopMode>(seedingStopModeValue, true, out var parsedSeedingStopMode) &&
            Enum.IsDefined(parsedSeedingStopMode))
        {
            seedingStopMode = parsedSeedingStopMode;
        }

        var seedingStopRatio = baseOptions.SeedingStopRatio;
        if (values.TryGetValue(RuntimeSettingsKeys.SeedingStopRatio, out var seedingStopRatioValue) && double.TryParse(
                seedingStopRatioValue, CultureInfo.InvariantCulture, out var parsedSeedingStopRatio
            ) && parsedSeedingStopRatio > 0)
        {
            seedingStopRatio = parsedSeedingStopRatio;
        }

        var seedingStopMinutes = baseOptions.SeedingStopMinutes;
        if (values.TryGetValue(RuntimeSettingsKeys.SeedingStopMinutes, out var seedingStopMinutesValue) && int.TryParse(
                seedingStopMinutesValue, CultureInfo.InvariantCulture, out var parsedSeedingStopMinutes
            ) && parsedSeedingStopMinutes > 0)
        {
            seedingStopMinutes = parsedSeedingStopMinutes;
        }

        var completedTorrentCleanupMode = baseOptions.CompletedTorrentCleanupMode;
        if (values.TryGetValue(
                RuntimeSettingsKeys.CompletedTorrentCleanupMode, out var completedTorrentCleanupModeValue
            ) && Enum.TryParse<CompletedTorrentCleanupMode>(
                completedTorrentCleanupModeValue, true, out var parsedCompletedTorrentCleanupMode
            ) && Enum.IsDefined(parsedCompletedTorrentCleanupMode))
        {
            completedTorrentCleanupMode = parsedCompletedTorrentCleanupMode;
        }

        var completedTorrentCleanupMinutes = baseOptions.CompletedTorrentCleanupMinutes;
        if (values.TryGetValue(
                RuntimeSettingsKeys.CompletedTorrentCleanupMinutes, out var completedTorrentCleanupMinutesValue
            ) && int.TryParse(
                completedTorrentCleanupMinutesValue, CultureInfo.InvariantCulture,
                out var parsedCompletedTorrentCleanupMinutes
            ) && parsedCompletedTorrentCleanupMinutes >= 0)
        {
            completedTorrentCleanupMinutes = parsedCompletedTorrentCleanupMinutes;
        }

        var deleteLogsForCompletedTorrents = baseOptions.DeleteLogsForCompletedTorrents;
        if (values.TryGetValue(
                RuntimeSettingsKeys.DeleteLogsForCompletedTorrents, out var deleteLogsForCompletedTorrentsValue
            ) && bool.TryParse(deleteLogsForCompletedTorrentsValue, out var parsedDeleteLogsForCompletedTorrents))
        {
            deleteLogsForCompletedTorrents = parsedDeleteLogsForCompletedTorrents;
        }

        var burstLimit = baseOptions.EngineConnectionFailureLogBurstLimit;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineConnectionFailureLogBurstLimit, out var burstLimitValue) &&
            int.TryParse(burstLimitValue, CultureInfo.InvariantCulture, out var parsedBurstLimit)                 &&
            parsedBurstLimit > 0)
        {
            burstLimit = parsedBurstLimit;
        }

        var windowSeconds = baseOptions.EngineConnectionFailureLogWindowSeconds;
        if (values.TryGetValue(
                RuntimeSettingsKeys.EngineConnectionFailureLogWindowSeconds, out var windowSecondsValue
            ) && int.TryParse(windowSecondsValue, CultureInfo.InvariantCulture, out var parsedWindowSeconds) &&
            parsedWindowSeconds > 0)
        {
            windowSeconds = parsedWindowSeconds;
        }

        var engineAllowPeerExchange = baseOptions.EngineAllowPeerExchange;
        if (values.TryGetValue(
                RuntimeSettingsKeys.EngineAllowPeerExchange, out var engineAllowPeerExchangeValue
            ) && bool.TryParse(engineAllowPeerExchangeValue, out var parsedEngineAllowPeerExchange))
        {
            engineAllowPeerExchange = parsedEngineAllowPeerExchange;
        }

        var engineEncryptionMode = baseOptions.EngineEncryptionMode;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineEncryptionMode, out var engineEncryptionModeValue) &&
            Enum.TryParse<TorrentEncryptionMode>(engineEncryptionModeValue, true, out var parsedEngineEncryptionMode) &&
            Enum.IsDefined(parsedEngineEncryptionMode))
        {
            engineEncryptionMode = parsedEngineEncryptionMode;
        }

        var engineMaximumConnections = baseOptions.EngineMaximumConnections;
        if (values.TryGetValue(RuntimeSettingsKeys.EngineMaximumConnections, out var engineMaximumConnectionsValue) &&
            int.TryParse(
                engineMaximumConnectionsValue, CultureInfo.InvariantCulture, out var parsedEngineMaximumConnections
            ) && parsedEngineMaximumConnections > 0)
        {
            engineMaximumConnections = parsedEngineMaximumConnections;
        }

        var engineMaximumHalfOpenConnections = baseOptions.EngineMaximumHalfOpenConnections;
        if (values.TryGetValue(
                RuntimeSettingsKeys.EngineMaximumHalfOpenConnections, out var engineMaximumHalfOpenConnectionsValue
            ) && int.TryParse(
                engineMaximumHalfOpenConnectionsValue, CultureInfo.InvariantCulture,
                out var parsedEngineMaximumHalfOpenConnections
            ) && parsedEngineMaximumHalfOpenConnections > 0)
        {
            engineMaximumHalfOpenConnections = parsedEngineMaximumHalfOpenConnections;
        }

        var engineMaximumDownloadRateBytesPerSecond = baseOptions.EngineMaximumDownloadRateBytesPerSecond;
        if (values.TryGetValue(
                RuntimeSettingsKeys.EngineMaximumDownloadRateBytesPerSecond, out var engineMaximumDownloadRateValue
            ) && int.TryParse(
                engineMaximumDownloadRateValue, CultureInfo.InvariantCulture, out var parsedEngineMaximumDownloadRate
            ) && parsedEngineMaximumDownloadRate >= 0)
        {
            engineMaximumDownloadRateBytesPerSecond = parsedEngineMaximumDownloadRate;
        }

        var engineMaximumUploadRateBytesPerSecond = baseOptions.EngineMaximumUploadRateBytesPerSecond;
        if (values.TryGetValue(
                RuntimeSettingsKeys.EngineMaximumUploadRateBytesPerSecond, out var engineMaximumUploadRateValue
            ) && int.TryParse(
                engineMaximumUploadRateValue, CultureInfo.InvariantCulture, out var parsedEngineMaximumUploadRate
            ) && parsedEngineMaximumUploadRate >= 0)
        {
            engineMaximumUploadRateBytesPerSecond = parsedEngineMaximumUploadRate;
        }

        var maxActiveMetadataResolutions = baseOptions.MaxActiveMetadataResolutions;
        if (values.TryGetValue(
                RuntimeSettingsKeys.MaxActiveMetadataResolutions, out var maxActiveMetadataResolutionsValue
            ) && int.TryParse(
                maxActiveMetadataResolutionsValue, CultureInfo.InvariantCulture,
                out var parsedMaxActiveMetadataResolutions
            ) && parsedMaxActiveMetadataResolutions > 0)
        {
            maxActiveMetadataResolutions = parsedMaxActiveMetadataResolutions;
        }

        var maxActiveDownloads = baseOptions.MaxActiveDownloads;
        if (values.TryGetValue(RuntimeSettingsKeys.MaxActiveDownloads, out var maxActiveDownloadsValue) && int.TryParse(
                maxActiveDownloadsValue, CultureInfo.InvariantCulture, out var parsedMaxActiveDownloads
            ) && parsedMaxActiveDownloads > 0)
        {
            maxActiveDownloads = parsedMaxActiveDownloads;
        }

        var metadataRefreshStaleSeconds = baseOptions.MetadataRefreshStaleSeconds;
        if (values.TryGetValue(
                RuntimeSettingsKeys.MetadataRefreshStaleSeconds, out var metadataRefreshStaleSecondsValue
            ) && int.TryParse(
                metadataRefreshStaleSecondsValue, CultureInfo.InvariantCulture,
                out var parsedMetadataRefreshStaleSeconds
            ) && parsedMetadataRefreshStaleSeconds > 0)
        {
            metadataRefreshStaleSeconds = parsedMetadataRefreshStaleSeconds;
        }

        var metadataRefreshRestartDelaySeconds = baseOptions.MetadataRefreshRestartDelaySeconds;
        if (values.TryGetValue(
                RuntimeSettingsKeys.MetadataRefreshRestartDelaySeconds, out var metadataRefreshRestartDelaySecondsValue
            ) && int.TryParse(
                metadataRefreshRestartDelaySecondsValue, CultureInfo.InvariantCulture,
                out var parsedMetadataRefreshRestartDelaySeconds
            ) && parsedMetadataRefreshRestartDelaySeconds > 0)
        {
            metadataRefreshRestartDelaySeconds = parsedMetadataRefreshRestartDelaySeconds;
        }

        var automaticMetadataResetStuckThresholdSeconds =
                baseOptions.AutomaticMetadataResetStuckThresholdSeconds;
        var metadataResolutionTimeSliceMinutes = baseOptions.MetadataResolutionTimeSliceMinutes;
        if (values.TryGetValue(
                RuntimeSettingsKeys.MetadataResolutionTimeSliceMinutes,
                out var metadataResolutionTimeSliceMinutesValue
            ) && int.TryParse(
                metadataResolutionTimeSliceMinutesValue,
                CultureInfo.InvariantCulture,
                out var parsedMetadataResolutionTimeSliceMinutes
            ) && parsedMetadataResolutionTimeSliceMinutes is
                >= TorrentCoreServiceOptions.MinimumMetadataResolutionTimeSliceMinutes and
                <= TorrentCoreServiceOptions.MaximumMetadataResolutionTimeSliceMinutes)
        {
            metadataResolutionTimeSliceMinutes = parsedMetadataResolutionTimeSliceMinutes;
        }
        if (values.TryGetValue(
                RuntimeSettingsKeys.AutomaticMetadataResetStuckThresholdSeconds,
                out var automaticMetadataResetStuckThresholdSecondsValue
            ) && int.TryParse(
                automaticMetadataResetStuckThresholdSecondsValue,
                CultureInfo.InvariantCulture,
                out var parsedAutomaticMetadataResetStuckThresholdSeconds
            ) && parsedAutomaticMetadataResetStuckThresholdSeconds is
                >= TorrentCoreServiceOptions.MinimumAutomaticMetadataResetStuckThresholdSeconds and
                <= TorrentCoreServiceOptions.MaximumAutomaticMetadataResetStuckThresholdSeconds)
        {
            automaticMetadataResetStuckThresholdSeconds = parsedAutomaticMetadataResetStuckThresholdSeconds;
        }

        var coldDownloadRecoveryThresholdMinutes = baseOptions.ColdDownloadRecoveryThresholdMinutes;
        if (values.TryGetValue(
                RuntimeSettingsKeys.ColdDownloadRecoveryThresholdMinutes,
                out var coldDownloadRecoveryThresholdMinutesValue
            ) && int.TryParse(
                coldDownloadRecoveryThresholdMinutesValue, CultureInfo.InvariantCulture,
                out var parsedColdDownloadRecoveryThresholdMinutes
            ) && parsedColdDownloadRecoveryThresholdMinutes > 0)
        {
            coldDownloadRecoveryThresholdMinutes = parsedColdDownloadRecoveryThresholdMinutes;
        }

        var coldDownloadRecoveryIntervalMinutes = baseOptions.ColdDownloadRecoveryIntervalMinutes;
        if (values.TryGetValue(
                RuntimeSettingsKeys.ColdDownloadRecoveryIntervalMinutes,
                out var coldDownloadRecoveryIntervalMinutesValue
            ) && int.TryParse(
                coldDownloadRecoveryIntervalMinutesValue, CultureInfo.InvariantCulture,
                out var parsedColdDownloadRecoveryIntervalMinutes
            ) && parsedColdDownloadRecoveryIntervalMinutes > 0)
        {
            coldDownloadRecoveryIntervalMinutes = parsedColdDownloadRecoveryIntervalMinutes;
        }

        var coldDownloadAbandonAfterHours = baseOptions.ColdDownloadAbandonAfterHours;
        if (values.TryGetValue(
                RuntimeSettingsKeys.ColdDownloadAbandonAfterHours,
                out var coldDownloadAbandonAfterHoursValue
            ) && int.TryParse(
                coldDownloadAbandonAfterHoursValue, CultureInfo.InvariantCulture,
                out var parsedColdDownloadAbandonAfterHours
            ) && parsedColdDownloadAbandonAfterHours >= 0)
        {
            coldDownloadAbandonAfterHours = parsedColdDownloadAbandonAfterHours;
        }

        var completionCallbackEnabled = baseOptions.CompletionCallbackEnabled;
        if (values.TryGetValue(RuntimeSettingsKeys.CompletionCallbackEnabled, out var completionCallbackEnabledValue) &&
            bool.TryParse(completionCallbackEnabledValue, out var parsedCompletionCallbackEnabled))
        {
            completionCallbackEnabled = parsedCompletionCallbackEnabled;
        }

        var completionCallbackCommandPath = NormalizePersistedText(
            values.TryGetValue(
                RuntimeSettingsKeys.CompletionCallbackCommandPath, out var completionCallbackCommandPathValue
            ) ? completionCallbackCommandPathValue : baseOptions.CompletionCallbackCommandPath
        );
        var completionCallbackArguments = NormalizePersistedText(
            values.TryGetValue(
                RuntimeSettingsKeys.CompletionCallbackArguments, out var completionCallbackArgumentsValue
            ) ? completionCallbackArgumentsValue : baseOptions.CompletionCallbackArguments
        );
        var completionCallbackWorkingDirectory = NormalizePersistedText(
            values.TryGetValue(
                RuntimeSettingsKeys.CompletionCallbackWorkingDirectory, out var completionCallbackWorkingDirectoryValue
            ) ? completionCallbackWorkingDirectoryValue : baseOptions.CompletionCallbackWorkingDirectory
        );

        var completionCallbackTimeoutSeconds = baseOptions.CompletionCallbackTimeoutSeconds;
        if (values.TryGetValue(
                RuntimeSettingsKeys.CompletionCallbackTimeoutSeconds, out var completionCallbackTimeoutValue
            ) && int.TryParse(
                completionCallbackTimeoutValue, CultureInfo.InvariantCulture, out var parsedCompletionCallbackTimeout
            ) && parsedCompletionCallbackTimeout > 0)
        {
            completionCallbackTimeoutSeconds = parsedCompletionCallbackTimeout;
        }

        var completionCallbackFinalizationTimeoutSeconds = baseOptions.CompletionCallbackFinalizationTimeoutSeconds;
        if (values.TryGetValue(
                RuntimeSettingsKeys.CompletionCallbackFinalizationTimeoutSeconds,
                out var completionCallbackFinalizationTimeoutValue
            ) && int.TryParse(
                completionCallbackFinalizationTimeoutValue, CultureInfo.InvariantCulture,
                out var parsedCompletionCallbackFinalizationTimeout
            ) && parsedCompletionCallbackFinalizationTimeout > 0)
        {
            completionCallbackFinalizationTimeoutSeconds = parsedCompletionCallbackFinalizationTimeout;
        }

        var completionCallbackApiBaseUrlOverride = NormalizePersistedText(
            values.TryGetValue(
                RuntimeSettingsKeys.CompletionCallbackApiBaseUrlOverride,
                out var completionCallbackApiBaseUrlOverrideValue
            ) ? completionCallbackApiBaseUrlOverrideValue : baseOptions.CompletionCallbackApiBaseUrlOverride
        );
        var completionCallbackApiKeyOverride = NormalizePersistedText(
            values.TryGetValue(
                RuntimeSettingsKeys.CompletionCallbackApiKeyOverride, out var completionCallbackApiKeyOverrideValue
            ) ? completionCallbackApiKeyOverrideValue : baseOptions.CompletionCallbackApiKeyOverride
        );

        var vpnEgressValidationEnabled = baseOptions.VpnEgressValidationEnabled;
        if (values.TryGetValue(
                RuntimeSettingsKeys.VpnEgressValidationEnabled, out var vpnEgressValidationEnabledValue
            ) && bool.TryParse(vpnEgressValidationEnabledValue, out var parsedVpnEgressValidationEnabled))
        {
            vpnEgressValidationEnabled = parsedVpnEgressValidationEnabled;
        }

        VpnEgressSettingsValidation.TryNormalizeEndpoint(
            baseOptions.VpnEgressValidationEndpoint, out var vpnEgressValidationEndpoint, out _
        );
        if (values.TryGetValue(
                RuntimeSettingsKeys.VpnEgressValidationEndpoint, out var vpnEgressValidationEndpointValue
            ) && VpnEgressSettingsValidation.TryNormalizeEndpoint(
                vpnEgressValidationEndpointValue, out var parsedVpnEgressValidationEndpoint, out _))
        {
            vpnEgressValidationEndpoint = parsedVpnEgressValidationEndpoint;
        }

        VpnEgressSettingsValidation.TryNormalizeCidrs(
            baseOptions.VpnEgressDirectIspCidrs, out var vpnEgressDirectIspCidrs, out _
        );
        if (values.TryGetValue(RuntimeSettingsKeys.VpnEgressDirectIspCidrs, out var vpnEgressDirectIspCidrsValue))
        {
            try
            {
                var persistedCidrs = JsonSerializer.Deserialize<string[]>(vpnEgressDirectIspCidrsValue);
                if (VpnEgressSettingsValidation.TryNormalizeCidrs(
                        persistedCidrs, out var parsedVpnEgressDirectIspCidrs, out _))
                {
                    vpnEgressDirectIspCidrs = parsedVpnEgressDirectIspCidrs;
                }
            }
            catch (JsonException)
            {
                // Invalid manually edited persistence falls back to validated host defaults.
            }
        }

        var vpnEgressDegradedCheckIntervalSeconds = ReadPositiveInteger(
            values,
            RuntimeSettingsKeys.VpnEgressDegradedCheckIntervalSeconds,
            baseOptions.VpnEgressDegradedCheckIntervalSeconds
        );
        var vpnEgressReadyCheckIntervalSeconds = ReadPositiveInteger(
            values,
            RuntimeSettingsKeys.VpnEgressReadyCheckIntervalSeconds,
            baseOptions.VpnEgressReadyCheckIntervalSeconds
        );
        var vpnEgressRequestTimeoutSeconds = ReadPositiveInteger(
            values,
            RuntimeSettingsKeys.VpnEgressRequestTimeoutSeconds,
            baseOptions.VpnEgressRequestTimeoutSeconds
        );
        var vpnEgressEngineSuspensionTimeoutSeconds = ReadPositiveInteger(
            values,
            RuntimeSettingsKeys.VpnEgressEngineSuspensionTimeoutSeconds,
            baseOptions.VpnEgressEngineSuspensionTimeoutSeconds
        );
        if (!VpnEgressSettingsValidation.TryValidateIntervals(
                vpnEgressDegradedCheckIntervalSeconds,
                vpnEgressReadyCheckIntervalSeconds,
                vpnEgressRequestTimeoutSeconds,
                out _,
                out _))
        {
            vpnEgressDegradedCheckIntervalSeconds = baseOptions.VpnEgressDegradedCheckIntervalSeconds;
            vpnEgressReadyCheckIntervalSeconds = baseOptions.VpnEgressReadyCheckIntervalSeconds;
            vpnEgressRequestTimeoutSeconds = baseOptions.VpnEgressRequestTimeoutSeconds;
        }

        return new RuntimeSettingsSnapshot
        {
            UsesPersistedOverrides                       = persistedSettings.Values.Count > 0,
            PartialFilesEnabled                          = false,
            PartialFileSuffix                            = string.Empty,
            SeedingStopMode                              = seedingStopMode,
            SeedingStopRatio                             = seedingStopRatio,
            SeedingStopMinutes                           = seedingStopMinutes,
            CompletedTorrentCleanupMode                  = completedTorrentCleanupMode,
            CompletedTorrentCleanupMinutes               = completedTorrentCleanupMinutes,
            DeleteLogsForCompletedTorrents              = deleteLogsForCompletedTorrents,
            EngineConnectionFailureLogBurstLimit         = burstLimit,
            EngineConnectionFailureLogWindowSeconds      = windowSeconds,
            EngineAllowPeerExchange                       = engineAllowPeerExchange,
            EngineEncryptionMode                         = engineEncryptionMode,
            EngineMaximumConnections                     = engineMaximumConnections,
            EngineMaximumHalfOpenConnections             = engineMaximumHalfOpenConnections,
            EngineMaximumDownloadRateBytesPerSecond      = engineMaximumDownloadRateBytesPerSecond,
            EngineMaximumUploadRateBytesPerSecond        = engineMaximumUploadRateBytesPerSecond,
            MaxActiveMetadataResolutions                 = maxActiveMetadataResolutions,
            MaxActiveDownloads                           = maxActiveDownloads,
            MetadataRefreshStaleSeconds                  = metadataRefreshStaleSeconds,
            MetadataRefreshRestartDelaySeconds           = metadataRefreshRestartDelaySeconds,
            MetadataResolutionTimeSliceMinutes           = metadataResolutionTimeSliceMinutes,
            AutomaticMetadataResetStuckThresholdSeconds  = automaticMetadataResetStuckThresholdSeconds,
            ColdDownloadRecoveryThresholdMinutes         = coldDownloadRecoveryThresholdMinutes,
            ColdDownloadRecoveryIntervalMinutes          = coldDownloadRecoveryIntervalMinutes,
            ColdDownloadAbandonAfterHours                 = coldDownloadAbandonAfterHours,
            CompletionCallbackEnabled                    = completionCallbackEnabled,
            CompletionCallbackCommandPath                = completionCallbackCommandPath,
            CompletionCallbackArguments                  = completionCallbackArguments,
            CompletionCallbackWorkingDirectory           = completionCallbackWorkingDirectory,
            CompletionCallbackTimeoutSeconds             = completionCallbackTimeoutSeconds,
            CompletionCallbackFinalizationTimeoutSeconds = completionCallbackFinalizationTimeoutSeconds,
            CompletionCallbackApiBaseUrlOverride         = completionCallbackApiBaseUrlOverride,
            CompletionCallbackApiKeyOverride             = completionCallbackApiKeyOverride,
            VpnEgressValidationEnabled                   = vpnEgressValidationEnabled,
            VpnEgressValidationEndpoint                  = vpnEgressValidationEndpoint,
            VpnEgressDirectIspCidrs                      = vpnEgressDirectIspCidrs,
            VpnEgressDegradedCheckIntervalSeconds        = vpnEgressDegradedCheckIntervalSeconds,
            VpnEgressReadyCheckIntervalSeconds           = vpnEgressReadyCheckIntervalSeconds,
            VpnEgressRequestTimeoutSeconds               = vpnEgressRequestTimeoutSeconds,
            VpnEgressEngineSuspensionTimeoutSeconds      = vpnEgressEngineSuspensionTimeoutSeconds,
            EngineSettingsRequireRestart =
                    engineAllowPeerExchange          != appliedEngineSettingsState.EngineAllowPeerExchange          ||
                    engineEncryptionMode             != appliedEngineSettingsState.EngineEncryptionMode             ||
                    engineMaximumConnections         != appliedEngineSettingsState.EngineMaximumConnections         ||
                    engineMaximumHalfOpenConnections != appliedEngineSettingsState.EngineMaximumHalfOpenConnections ||
                    engineMaximumDownloadRateBytesPerSecond !=
                    appliedEngineSettingsState.EngineMaximumDownloadRateBytesPerSecond ||
                    engineMaximumUploadRateBytesPerSecond !=
                    appliedEngineSettingsState.EngineMaximumUploadRateBytesPerSecond,
            UpdatedAtUtc = persistedSettings.UpdatedAtUtc,
        };
    }

    private RuntimeSettingsDto MapDto(TorrentCoreServiceOptions baseOptions, RuntimeSettingsSnapshot settings)
    {
        return new RuntimeSettingsDto
        {
            EngineRuntime                                = baseOptions.EngineMode.ToString(),
            SupportsLiveUpdates                          = true,
            UsesPersistedOverrides                       = settings.UsesPersistedOverrides,
            PartialFilesEnabled                          = settings.PartialFilesEnabled,
            PartialFileSuffix                            = settings.PartialFileSuffix,
            SeedingStopMode                              = settings.SeedingStopMode.ToString(),
            SeedingStopRatio                             = settings.SeedingStopRatio,
            SeedingStopMinutes                           = settings.SeedingStopMinutes,
            CompletedTorrentCleanupMode                  = settings.CompletedTorrentCleanupMode.ToString(),
            CompletedTorrentCleanupMinutes               = settings.CompletedTorrentCleanupMinutes,
            DeleteLogsForCompletedTorrents              = settings.DeleteLogsForCompletedTorrents,
            EngineConnectionFailureLogBurstLimit         = settings.EngineConnectionFailureLogBurstLimit,
            EngineConnectionFailureLogWindowSeconds      = settings.EngineConnectionFailureLogWindowSeconds,
            EngineAllowPeerExchange                       = settings.EngineAllowPeerExchange,
            EngineEncryptionMode                         = settings.EngineEncryptionMode.ToString(),
            EngineMaximumConnections                     = settings.EngineMaximumConnections,
            EngineMaximumHalfOpenConnections             = settings.EngineMaximumHalfOpenConnections,
            EngineMaximumDownloadRateBytesPerSecond      = settings.EngineMaximumDownloadRateBytesPerSecond,
            EngineMaximumUploadRateBytesPerSecond        = settings.EngineMaximumUploadRateBytesPerSecond,
            MaxActiveMetadataResolutions                 = settings.MaxActiveMetadataResolutions,
            MaxActiveDownloads                           = settings.MaxActiveDownloads,
            MetadataRefreshStaleSeconds                  = settings.MetadataRefreshStaleSeconds,
            MetadataRefreshRestartDelaySeconds           = settings.MetadataRefreshRestartDelaySeconds,
            MetadataResolutionTimeSliceMinutes           = settings.MetadataResolutionTimeSliceMinutes,
            AutomaticMetadataResetStuckThresholdSeconds  = settings.AutomaticMetadataResetStuckThresholdSeconds,
            ColdDownloadRecoveryThresholdMinutes         = settings.ColdDownloadRecoveryThresholdMinutes,
            ColdDownloadRecoveryIntervalMinutes          = settings.ColdDownloadRecoveryIntervalMinutes,
            ColdDownloadAbandonAfterHours                 = settings.ColdDownloadAbandonAfterHours,
            CompletionCallbackEnabled                    = settings.CompletionCallbackEnabled,
            CompletionCallbackCommandPath                = settings.CompletionCallbackCommandPath,
            CompletionCallbackArguments                  = settings.CompletionCallbackArguments,
            CompletionCallbackWorkingDirectory           = settings.CompletionCallbackWorkingDirectory,
            CompletionCallbackTimeoutSeconds             = settings.CompletionCallbackTimeoutSeconds,
            CompletionCallbackFinalizationTimeoutSeconds = settings.CompletionCallbackFinalizationTimeoutSeconds,
            CompletionCallbackApiBaseUrlOverride         = settings.CompletionCallbackApiBaseUrlOverride,
            CompletionCallbackApiKeyOverride             = settings.CompletionCallbackApiKeyOverride,
            VpnEgressValidationEnabled                   = settings.VpnEgressValidationEnabled,
            VpnEgressValidationEndpoint                  = settings.VpnEgressValidationEndpoint,
            VpnEgressDirectIspCidrs                     = settings.VpnEgressDirectIspCidrs,
            VpnEgressDegradedCheckIntervalSeconds        = settings.VpnEgressDegradedCheckIntervalSeconds,
            VpnEgressReadyCheckIntervalSeconds           = settings.VpnEgressReadyCheckIntervalSeconds,
            VpnEgressRequestTimeoutSeconds               = settings.VpnEgressRequestTimeoutSeconds,
            VpnEgressEngineSuspensionTimeoutSeconds      = settings.VpnEgressEngineSuspensionTimeoutSeconds,
            AppliedEngineMaximumConnections              = appliedEngineSettingsState.EngineMaximumConnections,
            AppliedEngineMaximumHalfOpenConnections      = appliedEngineSettingsState.EngineMaximumHalfOpenConnections,
            AppliedEngineAllowPeerExchange                = appliedEngineSettingsState.EngineAllowPeerExchange,
            AppliedEngineEncryptionMode                  = appliedEngineSettingsState.EngineEncryptionMode.ToString(),
            AppliedEngineMaximumDownloadRateBytesPerSecond =
                    appliedEngineSettingsState.EngineMaximumDownloadRateBytesPerSecond,
            AppliedEngineMaximumUploadRateBytesPerSecond =
                    appliedEngineSettingsState.EngineMaximumUploadRateBytesPerSecond,
            EngineSettingsRequireRestart = settings.EngineSettingsRequireRestart,
            UpdatedAtUtc                 = settings.UpdatedAtUtc,
            RetrievedAtUtc               = DateTimeOffset.UtcNow,
        };
    }

    private static string? NormalizePersistedText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int ReadPositiveInteger(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback)
    {
        return values.TryGetValue(key, out var value) &&
               int.TryParse(value, CultureInfo.InvariantCulture, out var parsedValue) &&
               parsedValue > 0
            ? parsedValue
            : fallback;
    }
}
