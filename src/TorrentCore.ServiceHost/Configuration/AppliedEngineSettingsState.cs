namespace TorrentCore.Service.Configuration;

public sealed class AppliedEngineSettingsState
{
    public TorrentEncryptionMode EngineEncryptionMode            { get; private set; }
    public int EngineMaximumConnections                { get; private set; }
    public int EngineMaximumHalfOpenConnections        { get; private set; }
    public int EngineMaximumDownloadRateBytesPerSecond { get; private set; }
    public int EngineMaximumUploadRateBytesPerSecond   { get; private set; }

    public void Set(TorrentEncryptionMode engineEncryptionMode,  int engineMaximumConnections,
        int             engineMaximumHalfOpenConnections,        int engineMaximumDownloadRateBytesPerSecond,
        int             engineMaximumUploadRateBytesPerSecond)
    {
        EngineEncryptionMode            = engineEncryptionMode;
        EngineMaximumConnections                = engineMaximumConnections;
        EngineMaximumHalfOpenConnections        = engineMaximumHalfOpenConnections;
        EngineMaximumDownloadRateBytesPerSecond = engineMaximumDownloadRateBytesPerSecond;
        EngineMaximumUploadRateBytesPerSecond   = engineMaximumUploadRateBytesPerSecond;
    }
}
