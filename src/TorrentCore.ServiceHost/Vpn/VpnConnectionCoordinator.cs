using System.Text.Json;
using Microsoft.Extensions.Options;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Application;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Vpn;

internal sealed class VpnConnectionCoordinator(
    IOptions<TorrentCoreServiceOptions> serviceOptions,
    IRuntimeSettingsService runtimeSettingsService,
    IVpnEgressProbe vpnEgressProbe,
    IMonoTorrentLifecycle monoTorrentLifecycle,
    ITorrentEngineAdapter torrentEngineAdapter,
    TorrentExecutionGate executionGate,
    TorrentStartupRecoveryService startupRecoveryService,
    StartupRecoveryState startupRecoveryState,
    VpnConnectionRuntimeState runtimeState,
    VpnSettingsChangeSignal settingsChangeSignal,
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext,
    TimeProvider timeProvider) : IHostedService
{
    private readonly CancellationTokenSource _stopSource = new();
    private Task? _backgroundTask;
    private bool _retryActivationWithoutProbe;
    private bool _validationEnabled;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (serviceOptions.Value.EngineMode != TorrentEngineMode.MonoTorrent)
        {
            await startupRecoveryService.StartAsync(cancellationToken);
            await SetStateAsync(false, VpnConnectionPhase.Disabled, null, null);
            return;
        }

        var settings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        _validationEnabled = settings.VpnEgressValidationEnabled;
        if (_validationEnabled)
        {
            await executionGate.CloseAsync(cancellationToken);
            await SetStateAsync(
                true,
                VpnConnectionPhase.Checking,
                null,
                "VPN connection is being confirmed. Torrent processing is paused."
            );
        }
        else
        {
            ClearLiveVpnDiagnostics();
            var result = await torrentEngineAdapter.RecoverAsync(cancellationToken);
            await startupRecoveryService.CompleteAsync(result, cancellationToken);
            await SetStateAsync(false, VpnConnectionPhase.Disabled, null, null);
        }

        _backgroundTask = RunAsync(_stopSource.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopSource.CancelAsync();
        if (_backgroundTask is null)
        {
            return;
        }

        try
        {
            await _backgroundTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown cancellation is expected.
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var checkImmediately = _validationEnabled;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var settings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
                if (settings.VpnEgressValidationEnabled != _validationEnabled)
                {
                    checkImmediately = await ApplyEnabledChangeAsync(settings, cancellationToken);
                }

                if (!_validationEnabled)
                {
                    if (_retryActivationWithoutProbe)
                    {
                        await ActivateAsync(validationEnabled: false, cancellationToken);
                        if (_retryActivationWithoutProbe)
                        {
                            await WaitForSettingsOrDelayAsync(
                                TimeSpan.FromSeconds(settings.VpnEgressDegradedCheckIntervalSeconds),
                                cancellationToken
                            );
                        }
                    }
                    else
                    {
                        await settingsChangeSignal.WaitAsync(cancellationToken);
                    }

                    continue;
                }

                if (_retryActivationWithoutProbe)
                {
                    await ActivateAsync(validationEnabled: true, cancellationToken);
                }
                else if (checkImmediately)
                {
                    await ValidateAndTransitionAsync(settings, cancellationToken);
                    checkImmediately = false;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                settings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
                var intervalSeconds = runtimeState.Snapshot.IsTorrentProcessingAvailable
                    ? settings.VpnEgressReadyCheckIntervalSeconds
                    : settings.VpnEgressDegradedCheckIntervalSeconds;
                var settingsChanged = await WaitForSettingsOrDelayAsync(
                    TimeSpan.FromSeconds(intervalSeconds), cancellationToken
                );
                if (settingsChanged)
                {
                    settings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
                    checkImmediately = await ApplyEnabledChangeAsync(settings, cancellationToken);
                }
                else if (_validationEnabled)
                {
                    checkImmediately = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _retryActivationWithoutProbe = false;
                await SetStateAsync(
                    _validationEnabled,
                    VpnConnectionPhase.Degraded,
                    VpnConnectionReason.UnexpectedFailure,
                    "VPN connection could not be confirmed. Torrent processing is paused.",
                    exception.Message
                );
                await WaitForSettingsOrDelayAsync(TimeSpan.FromSeconds(60), cancellationToken);
                checkImmediately = _validationEnabled;
            }
        }
    }

    private async Task<bool> ApplyEnabledChangeAsync(
        RuntimeSettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        if (settings.VpnEgressValidationEnabled == _validationEnabled)
        {
            return false;
        }

        _validationEnabled = settings.VpnEgressValidationEnabled;
        if (_validationEnabled)
        {
            _retryActivationWithoutProbe = false;
            return true;
        }

        await ActivateAsync(validationEnabled: false, cancellationToken);
        return false;
    }

    private async Task ValidateAndTransitionAsync(
        RuntimeSettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        var wasAvailable = runtimeState.Snapshot.IsTorrentProcessingAvailable;
        var result = await vpnEgressProbe.ValidateAsync(settings, cancellationToken);
        if (result.Outcome == VpnEgressValidationOutcome.Cancelled && cancellationToken.IsCancellationRequested)
        {
            return;
        }

        RecordValidationResult(result);

        var latestSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        if (latestSettings.VpnEgressValidationEnabled != _validationEnabled)
        {
            await ApplyEnabledChangeAsync(latestSettings, cancellationToken);
            return;
        }

        if (result.IsValidated)
        {
            if (!wasAvailable || !startupRecoveryState.Completed)
            {
                await ActivateAsync(validationEnabled: true, cancellationToken);
            }
            else if (runtimeState.Snapshot.Phase != VpnConnectionPhase.Ready)
            {
                await SetStateAsync(true, VpnConnectionPhase.Ready, null, null);
            }

            return;
        }

        _retryActivationWithoutProbe = false;
        var reason = MapReason(result.Outcome);
        var message = result.Outcome == VpnEgressValidationOutcome.DirectIsp
            ? "VPN connection appears to be down. Torrent processing is paused."
            : "VPN connection could not be confirmed. Torrent processing is paused.";

        if (!wasAvailable)
        {
            await SetStateAsync(true, VpnConnectionPhase.Degraded, reason, message, result.FailureSummary);
            return;
        }

        await SetStateAsync(true, VpnConnectionPhase.Suspending, reason, message, result.FailureSummary);
        try
        {
            using var suspensionSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            suspensionSource.CancelAfter(TimeSpan.FromSeconds(settings.VpnEgressEngineSuspensionTimeoutSeconds));
            await executionGate.CloseAsync(suspensionSource.Token);
            var suspension = await monoTorrentLifecycle.SuspendAsync(
                MonoTorrentSuspensionReason.VpnEgressNotValidated,
                suspensionSource.Token
            );
            await SetStateAsync(
                true,
                VpnConnectionPhase.Degraded,
                suspension.Succeeded ? reason : VpnConnectionReason.EngineSuspensionFailed,
                message,
                result.FailureSummary
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            await SetStateAsync(
                true,
                VpnConnectionPhase.Degraded,
                VpnConnectionReason.EngineSuspensionFailed,
                message,
                exception.Message
            );
        }
    }

    private async Task ActivateAsync(
        bool validationEnabled,
        CancellationToken cancellationToken)
    {
        if (!validationEnabled)
        {
            ClearLiveVpnDiagnostics();
        }

        var message = validationEnabled
            ? "VPN connection is available. Restarting torrent processing…"
            : "Restarting torrent processing…";
        await SetStateAsync(validationEnabled, VpnConnectionPhase.Activating, null, message);

        try
        {
            var result = await monoTorrentLifecycle.ActivateAsync(cancellationToken);
            if (!startupRecoveryState.Completed)
            {
                await startupRecoveryService.CompleteAsync(result, cancellationToken);
            }

            executionGate.Open();
            _retryActivationWithoutProbe = false;
            await SetStateAsync(
                validationEnabled,
                validationEnabled ? VpnConnectionPhase.Ready : VpnConnectionPhase.Disabled,
                null,
                null
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            _retryActivationWithoutProbe = true;
            await SetStateAsync(
                validationEnabled,
                VpnConnectionPhase.Degraded,
                VpnConnectionReason.EngineActivationFailed,
                "VPN connection is available, but torrent processing could not be restarted.",
                exception.Message
            );
        }
    }

    private async Task<bool> WaitForSettingsOrDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + delay;
        runtimeState.Update(snapshot => snapshot with { NextAutomaticRetryAtUtc = deadline });
        while (true)
        {
            var remaining = deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                ClearNextAutomaticRetry();
                return false;
            }

            using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var settingsTask = settingsChangeSignal.WaitAsync(waitSource.Token);
            var delayTask = Task.Delay(remaining, timeProvider, waitSource.Token);
            var completed = await Task.WhenAny(settingsTask, delayTask);
            await waitSource.CancelAsync();
            try
            {
                await completed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (completed == delayTask)
            {
                ClearNextAutomaticRetry();
                return false;
            }

            var settings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
            if (settings.VpnEgressValidationEnabled != _validationEnabled)
            {
                ClearNextAutomaticRetry();
                return true;
            }
        }
    }

    private async Task SetStateAsync(
        bool enabled,
        VpnConnectionPhase phase,
        VpnConnectionReason? reason,
        string? operatorMessage,
        string? failureSummary = null)
    {
        var current = runtimeState.Snapshot;
        var snapshot = current with
        {
            ValidationEnabled = enabled,
            Phase = phase,
            Reason = reason,
            OperatorMessage = operatorMessage,
            FailureSummary = failureSummary,
        };
        var transition = runtimeState.Set(snapshot);
        if (transition is null)
        {
            return;
        }

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = snapshot.IsTorrentProcessingAvailable
                    ? ActivityLogLevel.Information
                    : ActivityLogLevel.Warning,
                Category = VpnEgressActivityEvents.Category,
                EventType = VpnEgressActivityEvents.StateChanged,
                Message = operatorMessage ?? (enabled
                    ? "VPN connection is available. Torrent processing is active."
                    : "VPN validation is disabled. Torrent processing is active."),
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    ValidationEnabled = enabled,
                    PreviousPhase = transition.Previous.Phase.ToString(),
                    PreviousReason = transition.Previous.Reason?.ToString(),
                    NewPhase = transition.Current.Phase.ToString(),
                    NewReason = transition.Current.Reason?.ToString(),
                    FailureSummary = transition.Current.FailureSummary,
                }),
            },
            CancellationToken.None
        );
    }

    private void RecordValidationResult(VpnEgressValidationResult result)
    {
        runtimeState.Update(snapshot => snapshot with
        {
            LastCheckAtUtc = result.CheckedAtUtc,
            LastSuccessAtUtc = result.IsValidated ? result.CheckedAtUtc : snapshot.LastSuccessAtUtc,
            ObservedPublicIpv4 = result.Outcome is VpnEgressValidationOutcome.ValidatedEgress or
                VpnEgressValidationOutcome.DirectIsp
                    ? result.ObservedAddress?.ToString()
                    : null,
            FailureSummary = result.IsValidated ? null : result.FailureSummary,
            NextAutomaticRetryAtUtc = null,
        });
    }

    private void ClearLiveVpnDiagnostics()
    {
        runtimeState.Update(snapshot => snapshot with
        {
            LastCheckAtUtc = null,
            LastSuccessAtUtc = null,
            NextAutomaticRetryAtUtc = null,
            ObservedPublicIpv4 = null,
            FailureSummary = null,
        });
    }

    private void ClearNextAutomaticRetry()
    {
        runtimeState.Update(snapshot => snapshot with { NextAutomaticRetryAtUtc = null });
    }

    private static VpnConnectionReason MapReason(VpnEgressValidationOutcome outcome) => outcome switch
    {
        VpnEgressValidationOutcome.DirectIsp => VpnConnectionReason.DirectIsp,
        VpnEgressValidationOutcome.InvalidResponse => VpnConnectionReason.InvalidResponse,
        VpnEgressValidationOutcome.TimedOut => VpnConnectionReason.TimedOut,
        VpnEgressValidationOutcome.EndpointFailure => VpnConnectionReason.EndpointFailure,
        _ => VpnConnectionReason.UnexpectedFailure,
    };
}
