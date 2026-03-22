namespace TorrentCore.Service.Configuration;

public sealed class AppliedEngineSettingsState
{
    public int EngineMaximumConnections                { get; private set; }
    public int EngineMaximumHalfOpenConnections        { get; private set; }
    public int EngineMaximumDownloadRateBytesPerSecond { get; private set; }
    public int EngineMaximumUploadRateBytesPerSecond   { get; private set; }

    public void Set(int engineMaximumConnections,                int engineMaximumHalfOpenConnections,
        int             engineMaximumDownloadRateBytesPerSecond, int engineMaximumUploadRateBytesPerSecond)
    {
        EngineMaximumConnections                = engineMaximumConnections;
        EngineMaximumHalfOpenConnections        = engineMaximumHalfOpenConnections;
        EngineMaximumDownloadRateBytesPerSecond = engineMaximumDownloadRateBytesPerSecond;
        EngineMaximumUploadRateBytesPerSecond   = engineMaximumUploadRateBytesPerSecond;
    }
}
