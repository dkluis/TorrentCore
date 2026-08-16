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
    IExpressVpnController expressVpnController,
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
    private const int RequiredEligibleValidationFailures = 2;
    private const int MaximumProviderAttempts = 2;
    private readonly CancellationTokenSource _stopSource = new();
    private Task? _backgroundTask;
    private DateTimeOffset _serviceStartedAtUtc;
    private bool _engineSuspendedForVpn;
    private bool _retryActivationWithoutProbe;
    private bool _validationEnabled;
    private ExpressVpnAutomaticRecoveryMode _recoveryMode;
    private int _consecutiveEligibleValidationFailures;
    private int _reconnectAttempts;
    private int _launchAttempts;
    private DateTimeOffset? _lastReconnectAttemptAtUtc;
    private DateTimeOffset? _controllerUnavailableSinceUtc;
    private string? _lastControllerObservation;
    private VpnEgressValidationOutcome? _latestValidationOutcome;
    private bool _reconnectExhaustionLogged;
    private bool _launchExhaustionLogged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _serviceStartedAtUtc = timeProvider.GetUtcNow();
        if (serviceOptions.Value.EngineMode != TorrentEngineMode.MonoTorrent)
        {
            await startupRecoveryService.StartAsync(cancellationToken);
            await SetStateAsync(false, VpnConnectionPhase.Disabled, null, null);
            return;
        }

        var settings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        _validationEnabled = settings.VpnEgressValidationEnabled;
        _recoveryMode = settings.ExpressVpnAutomaticRecoveryMode;
        UpdateExpressVpnRecoveryState(
            _recoveryMode == ExpressVpnAutomaticRecoveryMode.Disabled
                ? ExpressVpnRecoveryPhase.Inactive
                : ExpressVpnRecoveryPhase.WaitingForConfirmation,
            _recoveryMode == ExpressVpnAutomaticRecoveryMode.Disabled
                ? null
                : "Waiting for two eligible VPN validation failures before automatic ExpressVPN recovery."
        );
        if (_validationEnabled)
        {
            await executionGate.CloseAsync(cancellationToken);
            _engineSuspendedForVpn = true;
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
            _engineSuspendedForVpn = false;
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
        _recoveryMode = settings.ExpressVpnAutomaticRecoveryMode;
        if (_validationEnabled)
        {
            _retryActivationWithoutProbe = false;
            _engineSuspendedForVpn = false;
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
        _latestValidationOutcome = result.Outcome;

        var latestSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        if (latestSettings.VpnEgressValidationEnabled != _validationEnabled)
        {
            await ApplyEnabledChangeAsync(latestSettings, cancellationToken);
            return;
        }

        if (result.IsValidated)
        {
            ResetRecoveryEpisode();
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

        UpdateRecoveryEligibility(latestSettings, result);
        UpdateRecoveryStateAfterFailure(latestSettings);

        _retryActivationWithoutProbe = false;
        var reason = MapReason(result.Outcome);
        var message = result.Outcome == VpnEgressValidationOutcome.DirectIsp
            ? "VPN connection appears to be down. Torrent processing is paused."
            : "VPN connection could not be confirmed. Torrent processing is paused.";

        if (!wasAvailable && _engineSuspendedForVpn)
        {
            await SetStateAsync(true, VpnConnectionPhase.Degraded, reason, message, result.FailureSummary);
            await TryAutomaticRecoveryAsync(latestSettings, cancellationToken);
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
            _engineSuspendedForVpn = suspension.Succeeded;
            await SetStateAsync(
                true,
                VpnConnectionPhase.Degraded,
                suspension.Succeeded ? reason : VpnConnectionReason.EngineSuspensionFailed,
                message,
                result.FailureSummary
            );
            if (suspension.Succeeded)
            {
                await TryAutomaticRecoveryAsync(latestSettings, cancellationToken);
            }
            else
            {
                UpdateExpressVpnRecoveryState(
                    ExpressVpnRecoveryPhase.WaitingForConfirmation,
                    "MonoTorrent suspension failed. Automatic ExpressVPN recovery is blocked."
                );
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            _engineSuspendedForVpn = false;
            UpdateExpressVpnRecoveryState(
                ExpressVpnRecoveryPhase.WaitingForConfirmation,
                "MonoTorrent suspension failed. Automatic ExpressVPN recovery is blocked."
            );
            await SetStateAsync(
                true,
                VpnConnectionPhase.Degraded,
                VpnConnectionReason.EngineSuspensionFailed,
                message,
                exception.Message
            );
        }
    }

    private async Task TryAutomaticRecoveryAsync(
        RuntimeSettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        if (!_engineSuspendedForVpn ||
            settings.ExpressVpnAutomaticRecoveryMode == ExpressVpnAutomaticRecoveryMode.Disabled)
        {
            UpdateExpressVpnRecoveryState(ExpressVpnRecoveryPhase.Inactive, null);
            return;
        }

        if (!expressVpnController.IsSupported)
        {
            UpdateExpressVpnRecoveryState(
                ExpressVpnRecoveryPhase.Inactive,
                "Automatic ExpressVPN recovery is unavailable on this host."
            );
            return;
        }

        if (_consecutiveEligibleValidationFailures < RequiredEligibleValidationFailures)
        {
            UpdateExpressVpnRecoveryState(
                ExpressVpnRecoveryPhase.WaitingForConfirmation,
                "Waiting for a second eligible VPN validation failure before automatic ExpressVPN recovery."
            );
            return;
        }

        var now = timeProvider.GetUtcNow();
        var startupDeadline = _serviceStartedAtUtc + TimeSpan.FromSeconds(settings.ExpressVpnRecoveryDelaySeconds);
        if (now < startupDeadline)
        {
            UpdateExpressVpnRecoveryState(
                ExpressVpnRecoveryPhase.WaitingForRecoveryDelay,
                "Automatic ExpressVPN recovery is waiting for the configured recovery delay.",
                startupDeadline
            );
            return;
        }

        UpdateExpressVpnRecoveryState(
            ExpressVpnRecoveryPhase.WaitingForController,
            "Checking whether ExpressVPN is available."
        );
        var controllerState = await expressVpnController.GetConnectionStateAsync(cancellationToken);
        await ObserveControllerStateAsync(controllerState);
        if (!controllerState.IsAvailable || controllerState.State is null)
        {
            await TryLaunchExpressVpnAsync(settings, now, cancellationToken);
            return;
        }

        _controllerUnavailableSinceUtc = null;
        switch (controllerState.State.Value)
        {
            case ExpressVpnConnectionState.Connecting:
            case ExpressVpnConnectionState.Reconnecting:
            case ExpressVpnConnectionState.DisconnectingToReconnect:
                UpdateExpressVpnRecoveryState(
                    ExpressVpnRecoveryPhase.WaitingForController,
                    $"ExpressVPN is {controllerState.State}. Torrent processing remains suspended."
                );
                return;

            case ExpressVpnConnectionState.Disconnecting:
                var disconnected = await expressVpnController.WaitForConnectionStateAsync(
                    ExpressVpnConnectionState.Disconnected,
                    cancellationToken
                );
                await ObserveControllerStateAsync(disconnected);
                if (!disconnected.IsAvailable || disconnected.State != ExpressVpnConnectionState.Disconnected)
                {
                    UpdateExpressVpnRecoveryState(
                        ExpressVpnRecoveryPhase.WaitingForController,
                        "ExpressVPN did not finish disconnecting. Torrent processing remains suspended."
                    );
                    return;
                }

                await TryConnectOnlyAsync(settings, now, cancellationToken);
                return;

            case ExpressVpnConnectionState.Disconnected:
                await TryConnectOnlyAsync(settings, now, cancellationToken);
                return;

            case ExpressVpnConnectionState.Connected:
                await TryDisconnectConnectAsync(settings, now, cancellationToken);
                return;
        }
    }

    private async Task TryLaunchExpressVpnAsync(
        RuntimeSettingsSnapshot settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _controllerUnavailableSinceUtc ??= startupRecoveryState.Completed ? now : _serviceStartedAtUtc;
        var unavailableDelay = TimeSpan.FromSeconds(settings.ExpressVpnUnavailableLaunchDelaySeconds);
        var launchDeadline = _controllerUnavailableSinceUtc.Value + unavailableDelay;
        if (_launchAttempts >= MaximumProviderAttempts)
        {
            if (now >= launchDeadline)
            {
                UpdateExpressVpnRecoveryState(
                    ExpressVpnRecoveryPhase.Exhausted,
                    "ExpressVPN is not running or responding. Torrent processing remains suspended. Two automatic launch attempts were unsuccessful."
                );
                await WriteExhaustedLogAsync("launch", cancellationToken);
            }
            else
            {
                UpdateExpressVpnRecoveryState(
                    ExpressVpnRecoveryPhase.WaitingForController,
                    "Waiting to confirm whether the final ExpressVPN launch attempt restored the controller.",
                    launchDeadline
                );
            }
            return;
        }

        if (now < launchDeadline)
        {
            UpdateExpressVpnRecoveryState(
                ExpressVpnRecoveryPhase.WaitingForController,
                "ExpressVPN is unavailable. An automatic application launch is scheduled.",
                launchDeadline
            );
            return;
        }

        UpdateExpressVpnRecoveryState(
            ExpressVpnRecoveryPhase.LaunchingApplication,
            "Requesting macOS to launch ExpressVPN. Torrent processing remains suspended."
        );
        var launch = await expressVpnController.LaunchApplicationAsync(cancellationToken);
        if (launch.Started)
        {
            _launchAttempts++;
            _controllerUnavailableSinceUtc = now;
        }
        var outcome = DescribeActionOutcome(launch);
        UpdateLastProviderAction(now, outcome);
        await WriteLaunchAttemptLogAsync(launch, cancellationToken);
        UpdateExpressVpnRecoveryState(
            ExpressVpnRecoveryPhase.WaitingForController,
            launch.Started
                ? "ExpressVPN launch was requested. Waiting for the controller to become available."
                : "ExpressVPN could not be launched. Torrent processing remains suspended.",
            launch.Started ? now + unavailableDelay : launchDeadline
        );
    }

    private async Task TryDisconnectConnectAsync(
        RuntimeSettingsSnapshot settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!CanStartReconnect(settings, now))
        {
            await UpdateReconnectDelayOrExhaustionAsync(settings, now, cancellationToken);
            return;
        }

        UpdateExpressVpnRecoveryState(
            ExpressVpnRecoveryPhase.Disconnecting,
            "Disconnecting ExpressVPN. Torrent processing remains suspended."
        );
        var attemptNumber = _reconnectAttempts + 1;
        var disconnect = await expressVpnController.DisconnectAsync(cancellationToken);
        if (disconnect.Started)
        {
            BeginReconnectAttempt(now);
        }
        if (!disconnect.Succeeded)
        {
            var outcome = DescribeActionOutcome(disconnect);
            UpdateLastProviderAction(now, outcome);
            if (disconnect.Started)
            {
                await WriteRecoveryAttemptLogAsync(
                    attemptNumber, "Connected", disconnect, null, null, null, outcome, cancellationToken
                );
            }
            await UpdateReconnectDelayOrExhaustionAsync(settings, now, cancellationToken);
            return;
        }

        var disconnected = await expressVpnController.WaitForConnectionStateAsync(
            ExpressVpnConnectionState.Disconnected,
            cancellationToken
        );
        await ObserveControllerStateAsync(disconnected);
        if (!disconnected.IsAvailable || disconnected.State != ExpressVpnConnectionState.Disconnected)
        {
            var outcome = disconnected.TimedOut ? "DisconnectTransitionTimedOut" : "DisconnectTransitionFailed";
            UpdateLastProviderAction(now, outcome);
            await WriteRecoveryAttemptLogAsync(
                attemptNumber, "Connected", disconnect, disconnected, null, null, outcome, cancellationToken
            );
            await UpdateReconnectDelayOrExhaustionAsync(settings, now, cancellationToken);
            return;
        }

        var sequence = await ConnectAndValidateAsync(
            settings, now, countConnectAsAttempt: false, cancellationToken
        );
        var finalOutcome = DescribeSequenceOutcome(sequence);
        UpdateLastProviderAction(now, finalOutcome);
        await WriteRecoveryAttemptLogAsync(
            attemptNumber, "Connected", disconnect, disconnected, sequence, sequence.Validation, finalOutcome,
            cancellationToken
        );
        if (sequence.Validation?.IsValidated != true)
        {
            await UpdateReconnectDelayOrExhaustionAsync(settings, timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    private async Task TryConnectOnlyAsync(
        RuntimeSettingsSnapshot settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!CanStartReconnect(settings, now))
        {
            await UpdateReconnectDelayOrExhaustionAsync(settings, now, cancellationToken);
            return;
        }

        var attemptNumber = _reconnectAttempts + 1;
        var sequence = await ConnectAndValidateAsync(
            settings, now, countConnectAsAttempt: true, cancellationToken
        );
        var outcome = DescribeSequenceOutcome(sequence);
        UpdateLastProviderAction(now, outcome);
        if (sequence.Connect.Started)
        {
            await WriteRecoveryAttemptLogAsync(
                attemptNumber, "Disconnected", null, null, sequence, sequence.Validation, outcome, cancellationToken
            );
        }
        if (sequence.Validation?.IsValidated != true)
        {
            await UpdateReconnectDelayOrExhaustionAsync(settings, timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    private async Task<ConnectValidationSequence> ConnectAndValidateAsync(
        RuntimeSettingsSnapshot settings,
        DateTimeOffset attemptStartedAtUtc,
        bool countConnectAsAttempt,
        CancellationToken cancellationToken)
    {
        UpdateExpressVpnRecoveryState(
            ExpressVpnRecoveryPhase.Connecting,
            "Connecting ExpressVPN. Torrent processing remains suspended."
        );
        var connect = await expressVpnController.ConnectAsync(cancellationToken);
        if (countConnectAsAttempt && connect.Started)
        {
            BeginReconnectAttempt(attemptStartedAtUtc);
        }
        if (!connect.Succeeded)
        {
            return new ConnectValidationSequence(connect, null, null);
        }

        var connected = await expressVpnController.WaitForConnectionStateAsync(
            ExpressVpnConnectionState.Connected,
            cancellationToken
        );
        await ObserveControllerStateAsync(connected);
        if (!connected.IsAvailable || connected.State != ExpressVpnConnectionState.Connected)
        {
            return new ConnectValidationSequence(connect, connected, null);
        }

        UpdateExpressVpnRecoveryState(
            ExpressVpnRecoveryPhase.Validating,
            "ExpressVPN reports connected. Validating TorrentCore public egress before restarting MonoTorrent."
        );
        var verification = await vpnEgressProbe.ValidateAsync(settings, cancellationToken);
        if (verification.Outcome == VpnEgressValidationOutcome.Cancelled && cancellationToken.IsCancellationRequested)
        {
            return new ConnectValidationSequence(connect, connected, verification);
        }

        RecordValidationResult(verification);
        var latestSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        if (latestSettings.VpnEgressValidationEnabled != _validationEnabled)
        {
            await ApplyEnabledChangeAsync(latestSettings, cancellationToken);
            return new ConnectValidationSequence(connect, connected, verification);
        }

        if (verification.IsValidated)
        {
            ResetRecoveryEpisode();
            await ActivateAsync(validationEnabled: true, cancellationToken);
            return new ConnectValidationSequence(connect, connected, verification);
        }

        UpdateRecoveryEligibility(latestSettings, verification);
        await SetStateAsync(
            true,
            VpnConnectionPhase.Degraded,
            MapReason(verification.Outcome),
            verification.Outcome == VpnEgressValidationOutcome.DirectIsp
                ? "VPN connection appears to be down. Torrent processing is paused."
                : "VPN connection could not be confirmed. Torrent processing is paused.",
            verification.FailureSummary
        );
        return new ConnectValidationSequence(connect, connected, verification);
    }

    private bool CanStartReconnect(RuntimeSettingsSnapshot settings, DateTimeOffset now)
        => _reconnectAttempts < MaximumProviderAttempts &&
           (_lastReconnectAttemptAtUtc is null ||
            now >= _lastReconnectAttemptAtUtc.Value +
            TimeSpan.FromSeconds(settings.ExpressVpnRecoveryDelaySeconds));

    private void BeginReconnectAttempt(DateTimeOffset now)
    {
        _reconnectAttempts++;
        _lastReconnectAttemptAtUtc = now;
        runtimeState.Update(snapshot => snapshot with
        {
            ExpressVpnReconnectAttemptsUsed = _reconnectAttempts,
        });
    }

    private void UpdateRecoveryEligibility(
        RuntimeSettingsSnapshot settings,
        VpnEgressValidationResult result)
    {
        if (settings.ExpressVpnAutomaticRecoveryMode != _recoveryMode)
        {
            _recoveryMode = settings.ExpressVpnAutomaticRecoveryMode;
            _consecutiveEligibleValidationFailures = 0;
        }

        var eligible = settings.ExpressVpnAutomaticRecoveryMode switch
        {
            ExpressVpnAutomaticRecoveryMode.DirectIspOnly =>
                result.Outcome == VpnEgressValidationOutcome.DirectIsp,
            ExpressVpnAutomaticRecoveryMode.AnyValidationFailure =>
                result.Outcome is not VpnEgressValidationOutcome.ValidatedEgress and
                    not VpnEgressValidationOutcome.Cancelled,
            _ => false,
        };
        _consecutiveEligibleValidationFailures = eligible
            ? _consecutiveEligibleValidationFailures + 1
            : 0;
    }

    private void UpdateRecoveryStateAfterFailure(RuntimeSettingsSnapshot settings)
    {
        if (settings.ExpressVpnAutomaticRecoveryMode == ExpressVpnAutomaticRecoveryMode.Disabled)
        {
            UpdateExpressVpnRecoveryState(ExpressVpnRecoveryPhase.Inactive, null);
            return;
        }

        if (_consecutiveEligibleValidationFailures < RequiredEligibleValidationFailures)
        {
            UpdateExpressVpnRecoveryState(
                ExpressVpnRecoveryPhase.WaitingForConfirmation,
                "Waiting for a second eligible VPN validation failure before automatic ExpressVPN recovery."
            );
        }
    }

    private async Task UpdateReconnectDelayOrExhaustionAsync(
        RuntimeSettingsSnapshot settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_reconnectAttempts >= MaximumProviderAttempts)
        {
            UpdateExpressVpnRecoveryState(
                ExpressVpnRecoveryPhase.Exhausted,
                "ExpressVPN automatic recovery did not restore validated TorrentCore egress. Torrent processing remains suspended. Two automatic reconnect attempts were unsuccessful."
            );
            await WriteExhaustedLogAsync("reconnect", cancellationToken);
            return;
        }

        if (_lastReconnectAttemptAtUtc is { } lastAttempt)
        {
            var deadline = lastAttempt + TimeSpan.FromSeconds(settings.ExpressVpnRecoveryDelaySeconds);
            UpdateExpressVpnRecoveryState(
                ExpressVpnRecoveryPhase.WaitingForRecoveryDelay,
                "Automatic ExpressVPN recovery is waiting before another reconnect attempt.",
                deadline > now ? deadline : null
            );
            return;
        }

        UpdateExpressVpnRecoveryState(
            ExpressVpnRecoveryPhase.WaitingForController,
            "ExpressVPN recovery could not start. Torrent processing remains suspended."
        );
    }

    private async Task ObserveControllerStateAsync(ExpressVpnControllerStateResult result)
    {
        var observation = result.IsAvailable && result.State is { } state
            ? state.ToString()
            : "Unavailable";
        runtimeState.Update(snapshot => snapshot with { ExpressVpnConnectionState = observation });
        if (string.Equals(_lastControllerObservation, observation, StringComparison.Ordinal))
        {
            return;
        }

        var previous = _lastControllerObservation;
        _lastControllerObservation = observation;
        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = result.IsAvailable ? ActivityLogLevel.Information : ActivityLogLevel.Warning,
                Category = VpnEgressActivityEvents.Category,
                EventType = VpnEgressActivityEvents.ExpressVpnControllerStateChanged,
                Message = result.IsAvailable
                    ? $"ExpressVPN controller state changed to {observation}."
                    : "ExpressVPN controller is unavailable.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    PreviousState = previous,
                    NewState = observation,
                    DurationMilliseconds = result.Duration.TotalMilliseconds,
                    result.TimedOut,
                    result.ExitCode,
                    result.FailureSummary,
                }),
            },
            CancellationToken.None
        );
    }

    private async Task WriteLaunchAttemptLogAsync(
        ExpressVpnControllerActionResult launch,
        CancellationToken cancellationToken)
    {
        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = launch.Succeeded ? ActivityLogLevel.Information : ActivityLogLevel.Warning,
                Category = VpnEgressActivityEvents.Category,
                EventType = VpnEgressActivityEvents.ExpressVpnLaunchAttempted,
                Message = launch.Succeeded
                    ? "ExpressVPN application launch was requested."
                    : "ExpressVPN application launch request failed.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    Attempt = launch.Started ? _launchAttempts : _launchAttempts + 1,
                    Maximum = MaximumProviderAttempts,
                    launch.Started,
                    launch.Succeeded,
                    launch.TimedOut,
                    launch.ExitCode,
                    DurationMilliseconds = launch.Duration.TotalMilliseconds,
                    launch.FailureSummary,
                    LaterControllerDisposition = "AwaitingControllerCheck",
                }),
            }, cancellationToken
        );
    }

    private async Task WriteRecoveryAttemptLogAsync(
        int attemptNumber,
        string priorControllerState,
        ExpressVpnControllerActionResult? disconnect,
        ExpressVpnControllerStateResult? disconnected,
        ConnectValidationSequence? sequence,
        VpnEgressValidationResult? validation,
        string outcome,
        CancellationToken cancellationToken)
    {
        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = validation?.IsValidated == true
                    ? ActivityLogLevel.Information
                    : ActivityLogLevel.Warning,
                Category = VpnEgressActivityEvents.Category,
                EventType = VpnEgressActivityEvents.ExpressVpnRecoveryAttempted,
                Message = validation?.IsValidated == true
                    ? "ExpressVPN automatic recovery restored validated TorrentCore egress."
                    : "ExpressVPN automatic recovery did not restore validated TorrentCore egress.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    Attempt = attemptNumber,
                    Maximum = MaximumProviderAttempts,
                    TriggerOutcome = _latestValidationOutcome?.ToString(),
                    PriorControllerState = priorControllerState,
                    Disconnect = disconnect is null ? null : new
                    {
                        disconnect.Started,
                        disconnect.Succeeded,
                        disconnect.TimedOut,
                        disconnect.ExitCode,
                        DurationMilliseconds = disconnect.Duration.TotalMilliseconds,
                        disconnect.FailureSummary,
                    },
                    DisconnectedTransition = disconnected is null ? null : new
                    {
                        disconnected.IsAvailable,
                        State = disconnected.State?.ToString(),
                        disconnected.TimedOut,
                        disconnected.ExitCode,
                        DurationMilliseconds = disconnected.Duration.TotalMilliseconds,
                        disconnected.FailureSummary,
                    },
                    Connect = sequence is null ? null : new
                    {
                        sequence.Connect.Started,
                        sequence.Connect.Succeeded,
                        sequence.Connect.TimedOut,
                        sequence.Connect.ExitCode,
                        DurationMilliseconds = sequence.Connect.Duration.TotalMilliseconds,
                        sequence.Connect.FailureSummary,
                    },
                    ConnectedTransition = sequence?.Connected is null ? null : new
                    {
                        sequence.Connected.IsAvailable,
                        State = sequence.Connected.State?.ToString(),
                        sequence.Connected.TimedOut,
                        sequence.Connected.ExitCode,
                        DurationMilliseconds = sequence.Connected.Duration.TotalMilliseconds,
                        sequence.Connected.FailureSummary,
                    },
                    ValidationOutcome = validation?.Outcome.ToString(),
                    Outcome = outcome,
                }),
            }, cancellationToken
        );
    }

    private async Task WriteExhaustedLogAsync(string category, CancellationToken cancellationToken)
    {
        if (category == "launch")
        {
            if (_launchExhaustionLogged)
            {
                return;
            }
            _launchExhaustionLogged = true;
        }
        else
        {
            if (_reconnectExhaustionLogged)
            {
                return;
            }
            _reconnectExhaustionLogged = true;
        }

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Warning,
                Category = VpnEgressActivityEvents.Category,
                EventType = VpnEgressActivityEvents.ExpressVpnRecoveryExhausted,
                Message = category == "launch"
                    ? "ExpressVPN automatic application launch attempts were exhausted."
                    : "ExpressVPN automatic reconnect attempts were exhausted.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    Category = category,
                    FinalControllerState = runtimeState.Snapshot.ExpressVpnConnectionState,
                    LatestValidationOutcome = _latestValidationOutcome?.ToString(),
                    Disposition = "Manual recovery or a later successful public-egress validation is required.",
                }),
            }, cancellationToken
        );
    }

    private void UpdateExpressVpnRecoveryState(
        ExpressVpnRecoveryPhase phase,
        string? message,
        DateTimeOffset? nextActionAtUtc = null)
    {
        runtimeState.Update(snapshot => snapshot with
        {
            ExpressVpnRecoveryPhase = phase,
            ExpressVpnReconnectAttemptsUsed = _reconnectAttempts,
            ExpressVpnLaunchAttemptsUsed = _launchAttempts,
            ExpressVpnNextActionAtUtc = nextActionAtUtc,
            ExpressVpnRecoveryMessage = message,
        });
    }

    private void UpdateLastProviderAction(DateTimeOffset occurredAtUtc, string outcome)
    {
        runtimeState.Update(snapshot => snapshot with
        {
            ExpressVpnLastActionAtUtc = occurredAtUtc,
            ExpressVpnLastActionOutcome = outcome,
            ExpressVpnReconnectAttemptsUsed = _reconnectAttempts,
            ExpressVpnLaunchAttemptsUsed = _launchAttempts,
        });
    }

    private static string DescribeActionOutcome(ExpressVpnControllerActionResult action)
        => action.Succeeded ? "Succeeded" : action.TimedOut ? "TimedOut" : action.Started ? "Failed" : "NotStarted";

    private static string DescribeSequenceOutcome(ConnectValidationSequence sequence)
    {
        if (!sequence.Connect.Succeeded)
        {
            return $"Connect{DescribeActionOutcome(sequence.Connect)}";
        }
        if (sequence.Connected?.State != ExpressVpnConnectionState.Connected)
        {
            return sequence.Connected?.TimedOut == true ? "ConnectTransitionTimedOut" : "ConnectTransitionFailed";
        }
        return sequence.Validation?.Outcome.ToString() ?? "ValidationNotCompleted";
    }

    private void ResetRecoveryEpisode()
    {
        _consecutiveEligibleValidationFailures = 0;
        _reconnectAttempts = 0;
        _launchAttempts = 0;
        _lastReconnectAttemptAtUtc = null;
        _controllerUnavailableSinceUtc = null;
        _reconnectExhaustionLogged = false;
        _launchExhaustionLogged = false;
        UpdateExpressVpnRecoveryState(ExpressVpnRecoveryPhase.Inactive, null);
    }

    private sealed record ConnectValidationSequence(
        ExpressVpnControllerActionResult Connect,
        ExpressVpnControllerStateResult? Connected,
        VpnEgressValidationResult? Validation);

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
            _engineSuspendedForVpn = false;
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
        ResetRecoveryEpisode();
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
