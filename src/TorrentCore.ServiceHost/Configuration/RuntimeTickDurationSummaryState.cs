namespace TorrentCore.Service.Configuration;

public sealed class RuntimeTickDurationSummaryState
{
    private int _enabled;

    public bool Enabled => Volatile.Read(ref _enabled) != 0;

    public void Set(bool enabled)
    {
        Volatile.Write(ref _enabled, enabled ? 1 : 0);
    }
}
