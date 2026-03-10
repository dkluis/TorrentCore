using System.Text.Json.Serialization;

namespace TorrentCore.Contracts.Torrents;

[JsonConverter(typeof(JsonStringEnumConverter<TorrentState>))]
public enum TorrentState
{
    ResolvingMetadata = 0,
    Queued = 1,
    Downloading = 2,
    Seeding = 3,
    Paused = 4,
    Completed = 5,
    Error = 6,
    Removed = 7,
}
