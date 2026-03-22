namespace TorrentCore.Service.Configuration;

public enum SeedingStopMode
{
    Unlimited            = 0,
    StopImmediately      = 1,
    StopAfterRatio       = 2,
    StopAfterTime        = 3,
    StopAfterRatioOrTime = 4,
}
