namespace TorrentCore.Service.Configuration;

public enum TorrentEncryptionMode
{
    PlainTextPreferred = 0,
    EncryptedPreferred = 1,
    EncryptedRequired = 2,
}
