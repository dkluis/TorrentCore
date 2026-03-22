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
    PausedByOperator        = 4,
    BlockedByError          = 5,
}
