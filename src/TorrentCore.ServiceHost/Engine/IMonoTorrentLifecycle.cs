namespace TorrentCore.Service.Engine;

public interface IMonoTorrentLifecycle
{
    Task<TorrentEngineRecoveryResult> ActivateAsync(CancellationToken cancellationToken);

    Task<MonoTorrentSuspensionResult> SuspendAsync(
        MonoTorrentSuspensionReason reason,
        CancellationToken cancellationToken);
}

public enum MonoTorrentSuspensionReason
{
    VpnEgressNotValidated,
    ServiceShutdown,
}

public sealed record MonoTorrentSuspensionFailure(
    string Phase,
    string Message,
    Guid? TorrentId = null);

public sealed record MonoTorrentSuspensionResult(
    bool EngineWasActive,
    bool EngineReleased,
    IReadOnlyList<MonoTorrentSuspensionFailure> Failures,
    DateTimeOffset CompletedAtUtc)
{
    public bool Succeeded => EngineReleased && Failures.Count == 0;
}
