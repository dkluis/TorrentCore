using System.Text.Json.Serialization;

namespace TorrentCore.Contracts.History;

[JsonConverter(typeof(JsonStringEnumConverter<TorrentHistoryOutcome>))]
public enum TorrentHistoryOutcome
{
    Active = 0,
    Removed = 1,
    Abandoned = 2,
}
