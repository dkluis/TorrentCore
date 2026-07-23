using System.Text.Json.Serialization;

namespace TorrentCore.Contracts.History;

[JsonConverter(typeof(JsonStringEnumConverter<TorrentRemovalKind>))]
public enum TorrentRemovalKind
{
    ManualRemoval = 0,
    ManualRemovalWithData = 1,
    CompletedTorrentCleanup = 2,
    ColdDownloadAbandonment = 3,
}
