# ExpressVPN Automatic Recovery Definition

## Status

This is the active definition and sliced delivery plan for optional ExpressVPN recovery owned by
`TorrentCore.Service`.

Slices 0 through 4 are implemented and covered by isolated tests. Slice 5 remains planned. No live ExpressVPN
mutation, TorrentCore restart, deployment, or installed-service verification was performed for slices 0 through 4.

The behavior in this document is not implemented merely because it is defined here. Until an implementation slice is
completed and verified, the current code and the active documentation remain authoritative. Creating this definition
does not authorize a live ExpressVPN disconnect, connect, application launch, TorrentCore restart, or deployment.

## Outcome

When VPN egress validation is enabled, TorrentCore may optionally ask ExpressVPN to repair a degraded split-tunnel
path. Recovery reproduces the operator's successful manual action: disconnect ExpressVPN and connect it again. If the
ExpressVPN controller is unavailable during startup or a later degradation, TorrentCore may launch the ExpressVPN
application after a bounded wait.

The feature must remain fail-closed:

- the TorrentCore execution gate is closed before suspension begins;
- MonoTorrent is fully suspended and disposed before any ExpressVPN command that can change connectivity;
- a failed or timed-out MonoTorrent suspension forbids ExpressVPN disconnect, connect, and launch actions;
- MonoTorrent remains suspended throughout provider detection, waits, launches, disconnects, connects, and retries;
- ExpressVPN reporting `Connected` and a successful command exit are diagnostic results, not proof of protected
  TorrentCore egress;
- only a successful TorrentCore public-IPv4 validation may authorize MonoTorrent activation;
- the execution gate opens only after validation and MonoTorrent activation both succeed.

The Service API, persistence-only magnet admission, settings, logs, and other degraded-mode behavior remain available
while MonoTorrent is suspended.

## Related Sources Of Truth

- [Architecture](architecture.md)
- [Operator settings](operator-settings.md)
- [Deployment](deployment.md)
- [Testing](testing.md)
- [Troubleshooting](troubleshooting.md)
- [Service app and VPN egress plan](torrentcore-service-app-vpn-egress-plan.md)

This definition extends the existing provider-neutral egress validator with an explicitly ExpressVPN-specific recovery
adapter. It does not change the meaning of a validated public address.

## Evidence Behind The Definition

The operator-supplied ExpressVPN diagnostic archive and read-only live controller inspection established the following:

- ExpressVPN can report `Connected` while TorrentCore's split-tunnel traffic is timing out.
- Split Tunnel can remain enabled and retain the correct `TorrentCoreService.app` `vpn` rule while its data path is not
  forwarding TorrentCore traffic.
- ExpressVPN's `pubip` value is the direct ISP address known by ExpressVPN, not the public exit observed by the
  TorrentCore process path.
- ExpressVPN's `vpnip` currently returns `Unknown` even while TorrentCore independently validates a non-ISP public
  egress address, so it cannot be an activation prerequisite.
- The ExpressVPN daemon may be present but temporarily unresponsive. Process presence alone is therefore not a useful
  readiness test.
- A TorrentCore validation timeout means egress could not be confirmed. It does not prove direct-ISP leakage, but it
  remains a valid fail-closed degraded condition.

ExpressVPN controller data can refine diagnostics and choose a recovery action. It cannot replace TorrentCore's
process-specific public-IP validation.

## Confirmed Operator Decisions

### Recovery Mode

One runtime setting selects the policy:

| Contract value | Operator label | Provider recovery eligibility |
|---|---|---|
| `Disabled` | Disabled | Never control or launch ExpressVPN |
| `DirectIspOnly` | Direct ISP detected | Two consecutive `DirectIsp` validation results |
| `AnyValidationFailure` | Any unvalidated egress | Two consecutive completed validation failures of any non-cancelled kind |

`Disabled` is the default so an upgrade preserves current runtime behavior. VPN validation continues to suspend and
recover MonoTorrent using its current provider-neutral behavior when automatic ExpressVPN recovery is disabled.

The UI must not use the ambiguous label `Always`. `Any unvalidated egress` means every eligible degraded validation
episode, not periodic reconnects while egress is healthy.

### Required Validation Failures

Provider recovery requires two consecutive eligible failed checks. This count is fixed at two for the initial
implementation.

- The first failed check closes admission and suspends MonoTorrent immediately. Provider recovery does not wait to make
  the engine safe.
- The second eligible failure may authorize provider recovery after the timing and engine-suspension prerequisites are
  satisfied.
- A cancelled check does not count.
- An engine activation or suspension error is not a validation failure and does not count.
- A successful `ValidatedEgress` result ends the degradation episode and clears validation, reconnect, and launch
  counters.
- In `DirectIspOnly` mode, a timeout or endpoint failure does not contribute to the two-result direct-ISP streak.
- In `AnyValidationFailure` mode, different failure outcomes may form the two-result streak because all mean that
  TorrentCore egress is unvalidated.

### Shared Recovery Delay

`ExpressVpnRecoveryDelaySeconds` is one shared runtime setting with a default of `180` seconds. It controls both:

- the minimum Service uptime before the first provider recovery action at startup; and
- the minimum interval between explicit disconnect/connect recovery cycles in one degradation episode.

The delay does not postpone the first egress check, engine suspension, or recovery after a successful validation. If
startup validation succeeds immediately, MonoTorrent starts immediately.

The delay exists to prevent TorrentCore from repeatedly cycling the machine's VPN in a tight loop. It is not a period
during which MonoTorrent may continue processing after a failed check.

### ExpressVPN Unavailable Delay

`ExpressVpnUnavailableLaunchDelaySeconds` is a runtime setting with a default of `300` seconds.

- Controller availability is tested through a bounded read-only `expressvpnctl get connectionstate` command, not only
  through a process list.
- At startup, the first automatic launch is not scheduled later than Service uptime plus the unavailable delay merely
  because the shared recovery delay was also observed. The three-minute and five-minute waits do not stack into an
  eight-minute initial wait.
- During a later degradation, the unavailable window begins when the eligible recovery first confirms that the
  controller is unavailable.
- If the controller becomes responsive during the window, no application launch occurs.

### Attempt Limits

The initial implementation uses a fixed maximum of two attempts for each external recovery category in one degradation
episode:

- at most two ExpressVPN disconnect/connect recovery cycles; and
- at most two ExpressVPN application launch attempts.

The counters are separate and both use the same maximum. A provider action counts when TorrentCore actually starts the
external launch or control command. Merely observing a transitional controller state does not consume an attempt.

After the first unsuccessful application launch, TorrentCore waits another unavailable-delay window while continuing
read-only controller checks. If the controller is still unavailable, it launches the application a second time. After
the second launch and another unsuccessful availability window, TorrentCore stops launching ExpressVPN for the episode.

After the second unsuccessful disconnect/connect cycle, TorrentCore stops changing ExpressVPN connectivity for the
episode. Public-IP validation and read-only controller checks continue so manual recovery can still restore processing.

Attempt counters reset after validated TorrentCore egress or a new Service start. Disabling and re-enabling only the
automatic recovery mode must not be used as an implicit attempt-counter bypass within the same unvalidated episode.

### ExpressVPN Application Launch

TorrentCore launches the registered macOS application through LaunchServices:

```text
/usr/bin/open -g -a ExpressVPN
```

TorrentCore must not directly start, kill, restart, or otherwise own `expressvpn-daemon`, the split-tunnel extension,
OpenVPN, Lightway, WireGuard, or another ExpressVPN helper. A successful `open` exit means only that macOS accepted the
launch request. Controller responsiveness and TorrentCore egress must still be confirmed separately.

If both launch attempts are exhausted, the operator-visible warning is:

> ExpressVPN is not running or responding. Torrent processing remains suspended. Two automatic launch attempts were
> unsuccessful.

### Reconnect Selection

After all safety and eligibility prerequisites are met, the latest read-only ExpressVPN controller state chooses the
action:

| Controller state | Action |
|---|---|
| `Connected` | Disconnect, wait for `Disconnected`, then connect to the current/last-selected location |
| `Disconnected` | Connect only |
| `Connecting` | Wait; do not compete with the in-progress connection |
| `Reconnecting` | Wait; do not compete with ExpressVPN's recovery |
| `DisconnectingToReconnect` | Wait |
| `Disconnecting` | Wait for `Disconnected`, then connect if the recovery remains eligible |
| Command unavailable, timed out, or invalid response | Enter or remain in the unavailable/launch path |

The implementation uses the installed `/usr/local/bin/expressvpnctl` controller with an absolute path and argument-list
process invocation. It must not use a shell, interpolate command text, inherit an arbitrary working directory, or rely
on the LaunchAgent `PATH`.

## Runtime Safety Model

### Suspension Interlock

Provider-changing actions require a positive, current engine disposition proving that:

1. the execution gate is closed;
2. all admitted engine work has drained within the configured suspension timeout;
3. `IMonoTorrentLifecycle.SuspendAsync` returned success;
4. the `ClientEngine`, managers, listener, DHT activity, tracker activity, and adapter-owned coordinators are disposed or
   cleared according to the existing lifecycle contract; and
5. no later settings transition or cancellation invalidated that disposition.

If suspension fails or times out:

- remain degraded with `EngineSuspensionFailed`;
- do not invoke ExpressVPN;
- retry the suspension interlock while the execution gate remains closed;
- do not skip later suspension retries merely because the public runtime snapshot is already degraded; and
- do not activate MonoTorrent unless a later public-IP validation succeeds and the serialized lifecycle can establish
  one valid engine instance.

This hardening is required before the ExpressVPN controller adapter is connected to production orchestration. The
existing ten-second suspension timeout observed in diagnostics makes this an explicit first slice rather than an
assumption.

### Recovery Success

An ExpressVPN recovery attempt is successful only after all of the following:

1. the requested provider action has completed or ExpressVPN has independently reached a stable connected state;
2. a new TorrentCore public-IPv4 probe completes successfully;
3. the observed address does not match any configured direct-ISP CIDR;
4. the validation settings used for the result are still effective;
5. MonoTorrent activation and durable recovery succeed; and
6. the execution gate opens after activation.

`expressvpnctl` exit code zero, `connectionstate=Connected`, `dnsconfigured=true`, `splittunnel=true`, a retained app
rule, or a non-`Unknown` `vpnip` value can never independently satisfy this definition.

### Recovery Failure And Exhaustion

When provider recovery does not restore validated egress:

- MonoTorrent remains suspended;
- the execution gate remains closed;
- provider attempt counts and the next eligible action time remain visible;
- degraded public-IP checks continue at the configured degraded interval;
- validated egress from a manual operator action automatically resumes normal activation;
- settings and persistence-only magnet admission remain available; and
- exhausting provider actions never disables VPN validation automatically.

After reconnect attempts are exhausted, the operator-visible warning is:

> ExpressVPN automatic recovery did not restore validated TorrentCore egress. Torrent processing remains suspended.
> Two automatic reconnect attempts were unsuccessful.

### Service Stop And Settings Changes

- Service shutdown cancels waits, controller polling, and child-process observation.
- Shutdown never activates MonoTorrent to clean up a provider recovery attempt.
- Turning VPN egress validation off retains its existing explicit behavior: TorrentCore leaves the VPN-gated policy and
  attempts normal engine activation without a VPN check.
- Turning automatic ExpressVPN recovery off cancels future provider actions but does not turn VPN validation off or
  activate MonoTorrent.
- A live mode change takes effect on the next serialized recovery decision and clears only the consecutive eligibility
  streak; it does not erase already-consumed external attempt counts in the current degradation episode.
- Live timing changes apply to the next deadline calculation and do not retroactively move an external action already
  in progress.

## Runtime Settings

The settings are additive rows in the existing runtime-settings store and are exposed by the runtime-settings API,
Swagger/OpenAPI, WebUI, and native macOS Settings screen.

| Setting | Default | Applies live | Purpose |
|---|---:|---|---|
| `ExpressVpnAutomaticRecoveryMode` | `Disabled` | Yes | Selects disabled, direct-ISP-only, or any-validation-failure recovery |
| `ExpressVpnRecoveryDelaySeconds` | `180` | Yes | Shared startup grace and minimum disconnect/connect interval |
| `ExpressVpnUnavailableLaunchDelaySeconds` | `300` | Yes | Wait before each of at most two application launch attempts |

The two-consecutive-failure requirement and two-attempt limits are fixed policy in the initial implementation rather
than additional operator settings. Settings validation must reject unknown modes and nonpositive delays. UI help must
explain that delay values do not allow torrent processing during degradation.

Automatic ExpressVPN recovery is effective only when all of these are true:

- `VpnEgressValidationEnabled` is `true`;
- the engine mode is MonoTorrent;
- the host is macOS;
- `ExpressVpnAutomaticRecoveryMode` is not `Disabled`; and
- the current degradation is eligible for the selected mode.

On unsupported hosts, provider-neutral VPN validation remains functional and the Service must not try to execute a
macOS controller or application launch.

## Provider Adapter Boundary

The provider adapter is isolated behind interfaces so normal tests never require ExpressVPN or network access.

The read-only boundary supports:

- controller availability;
- current connection state;
- optional diagnostic reads for DNS configured, split-tunnel enabled, split-app rules, protocol, region, ISP public IP,
  and assigned VPN IP; and
- bounded output capture with sanitized failure summaries.

The mutating boundary supports only:

- disconnect;
- connect to the current/last-selected location; and
- launch the ExpressVPN application through LaunchServices.

It does not support changing region, protocol, Network Lock, split-tunnel enablement, split-app rules, DNS settings,
background mode, or account state.

All processes require:

- `ProcessStartInfo.ArgumentList` rather than shell text;
- explicit executable paths;
- redirected, bounded standard output and error;
- an outer TorrentCore timeout and cancellation token even when the CLI receives its own timeout;
- complete process disposal and no orphaned output-reader tasks; and
- sanitized persistence through the existing activity-log path, never `ILogger`.

Each controller command and each awaited connection-state transition has a fixed 60-second TorrentCore timeout.

## Runtime State And Operator Diagnostics

The provider recovery state is serialized with the existing VPN coordinator and projected additively through
`/api/host/status`. The contract exposes:

- configured recovery mode;
- provider recovery phase;
- last observed ExpressVPN connection state;
- reconnect attempts used and maximum;
- application-launch attempts used and maximum;
- next provider action time when scheduled;
- last provider action time and outcome; and
- a concise operator recovery message distinct from the sanitized egress failure summary.

Suggested provider phases are `Inactive`, `WaitingForConfirmation`, `WaitingForRecoveryDelay`,
`WaitingForController`, `LaunchingApplication`, `Disconnecting`, `Connecting`, `Validating`, and `Exhausted`.
Torrent-processing availability remains authoritative and false for every phase except the existing validated `Ready`
state.

Stable activity events use the existing `vpn` category:

| Event type | When written | Required details |
|---|---|---|
| `vpn.expressvpn.controller_state_changed` | Controller availability or stable connection state changes | previous/new state, command duration, sanitized failure |
| `vpn.expressvpn.recovery_attempted` | Each connect-only or disconnect/connect attempt | attempt/max, trigger outcome, prior controller state, command outcomes and durations |
| `vpn.expressvpn.launch_attempted` | Each LaunchServices request | attempt/max, process outcome, later controller disposition |
| `vpn.expressvpn.recovery_exhausted` | A launch or reconnect category reaches two unsuccessful attempts | category, final controller state, latest validation outcome, next manual-recovery disposition |

Unchanged polling results are not repeatedly persisted. Raw controller diagnostic output is not stored wholesale.

The WebUI and native macOS Dashboard must distinguish at least:

- validation degraded while awaiting a second check;
- MonoTorrent suspension failed, so provider recovery was forbidden;
- ExpressVPN is starting or transitioning;
- ExpressVPN is unavailable and an automatic launch is scheduled;
- provider recovery is running;
- launch attempts are exhausted; and
- reconnect attempts are exhausted.

## Non-Goals

- A generic VPN-provider plugin system.
- Changing TorrentCore's application identity, launcher, bundle identifier, or split-tunnel rule.
- Restarting the TorrentCore Service or WebUI process as provider recovery.
- Starting or killing ExpressVPN daemon/helper processes directly.
- Automatically editing ExpressVPN split-tunnel settings.
- Automatically changing ExpressVPN region or VPN protocol.
- Automatically enabling Network Lock or background mode.
- Treating ExpressVPN status as proof of TorrentCore egress.
- Replacing the current public-IPv4 validation endpoint or adding a multi-endpoint quorum in this workstream.
- Allowing torrent processing while recovery is waiting, exhausted, or uncertain.

## Sliced Delivery Plan

### Slice 0: Suspension Interlock And Characterization

#### Work

- Add deterministic lifecycle fixtures that expose gate state, engine disposition, and provider-call observation.
- Harden the coordinator so a failed suspension remains retryable even after the public host state is already degraded.
- Establish a positive suspension disposition required by all later provider-changing actions.
- Characterize startup, ready-to-degraded, suspension-timeout, settings-disable, and later-validation-success behavior.
- Add no ExpressVPN production executor or command in this slice.

#### Acceptance

- A failed or timed-out suspension leaves admission closed and produces zero provider-changing actions.
- Later degraded checks retry suspension rather than assuming the engine is absent.
- A later validation success creates at most one engine and opens the gate only after activation.
- Validation-disabled behavior remains unchanged.
- Existing VPN coordinator, lifecycle, API, and queue tests pass.

### Slice 1: Persisted Recovery Settings And Contracts

#### Work

- Add the three runtime settings, enum validation, SQLite round-trip, defaults, and live settings mapping.
- Extend public runtime-settings contracts and regenerate the committed OpenAPI contract.
- Update WebUI and Apple contract adapters, fixtures, and compatibility defaults.
- Keep `Disabled` behavior identical to the pre-feature Service.

#### Acceptance

- Existing databases gain additive settings without schema rewrites or changes to active torrents.
- Omitted fields from older clients preserve effective values.
- Invalid modes and delay values return structured validation errors.
- `Disabled` issues no ExpressVPN read or write commands.

### Slice 2: ExpressVPN Controller Adapter

#### Work

- Add injectable command execution and time abstractions.
- Implement bounded read-only controller queries and strict connection-state parsing.
- Implement connect, disconnect, and LaunchServices request methods without integrating them into the coordinator.
- Sanitize output and record no logs through `ILogger`.

#### Acceptance

- Unit tests cover every documented controller state, nonzero exit, timeout, cancellation, malformed output, missing
  executable, and application-launch failure.
- Command construction uses fixed executable paths and argument lists with no shell.
- Tests require neither installed ExpressVPN nor Internet access.
- No production path calls a mutating method yet.

### Slice 3: Serialized Automatic Recovery State Machine

#### Work

- Integrate the provider adapter behind the Slice 0 suspension interlock.
- Implement two-check eligibility, the shared recovery delay, unavailable delay, startup non-stacking rule, controller
  state selection, separate two-attempt caps, and episode resets.
- Keep public-IP validation authoritative before activation.
- Continue degraded polling after exhaustion for manual recovery.

#### Acceptance

- No launch, disconnect, or connect begins before successful MonoTorrent suspension.
- Command ordering is exactly disconnect, confirmed disconnected, connect, validation, activation, gate open.
- A disconnected controller state uses connect only.
- Transitional states do not consume attempts or trigger competing commands.
- Startup can validate and activate immediately; failed startup cannot perform provider recovery before the shared delay.
- Initial startup unavailability launches at the five-minute deadline rather than after stacked three- and five-minute
  waits.
- Launch and reconnect categories stop after two attempts while validation continues.
- Service cancellation leaves MonoTorrent inactive and no coordinator task orphaned.

### Slice 4: Host Status, Activity Logs, And Operator UI

#### Work

- Add provider recovery status fields and stable activity events.
- Add mode and timing editors plus help to WebUI and native macOS Settings.
- Add Dashboard warnings and scheduled-action/attempt context to both operator surfaces.
- Preserve compatibility with older Service versions that omit the new fields.

#### Acceptance

- Operators can distinguish egress failure, suspension failure, controller absence, provider recovery, and exhaustion.
- Both Settings surfaces round-trip the same effective values and validation errors.
- Repeated unchanged polls do not flood the database.
- OpenAPI, .NET callers, Apple mappings, and UI fixture tests remain aligned.

### Slice 5: Mac Integration Proof And Documentation Cutover

#### Work

- Perform an operator-authorized macOS proof with no active torrents and a recoverable network window.
- Verify controller reads, one connect-only case, one disconnect/connect case, application launch, attempt exhaustion,
  Service cancellation, and later manual recovery.
- Confirm the installed `TorrentCoreService.app` identity and ExpressVPN rule do not change.
- Merge durable behavior into architecture, operator settings, deployment, testing, and troubleshooting documentation.
- Move this definition to `docs/archive/` after implementation and documentation cutover are complete.

#### Acceptance

- Live proof shows the engine is absent before every provider-changing action.
- Packet/public-IP evidence confirms no MonoTorrent activation before validated non-ISP egress.
- A successful recovery restores persisted torrents without duplicate managers or changed desired intent.
- Failed launch and reconnect exhaustion remain suspended with accurate warnings.
- The full .NET suite, Swift package suite, relevant macOS UI build-for-testing, and release checks pass.

Live disconnect/connect and application-launch tests are never ordinary automated test-suite side effects. They require
explicit operator authorization on the target Mac.

## Rollback Model

- Set `ExpressVpnAutomaticRecoveryMode` to `Disabled` to stop future provider actions without disabling public-egress
  validation.
- The settings are additive generic runtime-setting rows; an older Service ignores them.
- No database schema rollback is required.
- No ExpressVPN settings, region, protocol, split-tunnel rule, or application files are changed by the feature.
- If recovery is exhausted, manual ExpressVPN recovery followed by a successful normal egress check restores
  MonoTorrent processing.

## Definition Of Complete

This workstream is complete only when:

- every slice is implemented and its acceptance criteria are verified;
- no provider-changing command can execute without a successful, current MonoTorrent suspension disposition;
- activation is possible only after successful TorrentCore public-IP validation;
- the agreed modes, shared three-minute delay, five-minute unavailable delay, two failed checks, and two-attempt limits
  behave identically in deterministic tests and the authorized macOS proof;
- both operator settings surfaces and Dashboards expose effective settings and actionable warnings;
- activity logging is bounded, sanitized, and uses the existing persistence path;
- provider-disabled behavior remains backward compatible; and
- durable behavior is merged into active documentation and this planning definition is archived.
