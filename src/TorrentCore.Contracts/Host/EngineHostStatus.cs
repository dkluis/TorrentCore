#region

using System.Text.Json.Serialization;

#endregion

namespace TorrentCore.Contracts.Host;

[JsonConverter(typeof(JsonStringEnumConverter<EngineHostStatus>))]
public enum EngineHostStatus
{
    Starting = 0,
    Ready    = 1,
    Degraded = 2,
    Stopped  = 3,
    Faulted  = 4,
}
