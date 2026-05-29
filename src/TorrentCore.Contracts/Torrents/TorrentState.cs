#region

using System.Text.Json.Serialization;

#endregion

namespace TorrentCore.Contracts.Torrents;

[JsonConverter(typeof(JsonStringEnumConverter<TorrentState>))]
public enum TorrentState
{
    ResolvingMetadata = 0,
    Queued            = 1,
    Downloading       = 2,
    Seeding           = 3,
    WaitingForFileCompletion = 4,
    Paused            = 5,
    Completed         = 6,
    Error             = 7,
    Removed           = 8,
}
