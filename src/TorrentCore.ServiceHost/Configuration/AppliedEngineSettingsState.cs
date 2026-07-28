namespace TorrentCore.Service.Configuration;

public sealed class AppliedEngineSettingsState
{
    public bool EngineAllowPeerExchange { get; private set; }
    public TorrentEncryptionMode EngineEncryptionMode            { get; private set; }
    public int EngineMaximumConnections                { get; private set; }
    public int EngineMaximumHalfOpenConnections        { get; private set; }
    public int EngineMaximumDownloadRateBytesPerSecond { get; private set; }
    public int EngineMaximumUploadRateBytesPerSecond   { get; private set; }

    public void Set(bool engineAllowPeerExchange, TorrentEncryptionMode engineEncryptionMode,
        int             engineMaximumConnections,
        int             engineMaximumHalfOpenConnections,        int engineMaximumDownloadRateBytesPerSecond,
        int             engineMaximumUploadRateBytesPerSecond)
    {
        EngineAllowPeerExchange          = engineAllowPeerExchange;
        EngineEncryptionMode            = engineEncryptionMode;
        EngineMaximumConnections                = engineMaximumConnections;
        EngineMaximumHalfOpenConnections        = engineMaximumHalfOpenConnections;
        EngineMaximumDownloadRateBytesPerSecond = engineMaximumDownloadRateBytesPerSecond;
        EngineMaximumUploadRateBytesPerSecond   = engineMaximumUploadRateBytesPerSecond;
    }
}
