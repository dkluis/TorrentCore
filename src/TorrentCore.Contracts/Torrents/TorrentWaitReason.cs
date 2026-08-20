#region

using System.Text.Json.Serialization;

#endregion

namespace TorrentCore.Contracts.Torrents;

[JsonConverter(typeof(JsonStringEnumConverter<TorrentWaitReason>))]
public enum TorrentWaitReason
{
    PendingMetadataDispatch = 0,
    WaitingForMetadataSlot  = 1,
    PendingDownloadDispatch = 2,
    WaitingForDownloadSlot  = 3,
    WaitingForFileCompletion = 4,
    PausedByOperator        = 5,
    BlockedByError          = 6,
    HeldByOperator          = 7,
    AutomaticallyYieldedDownload = 8,
}
