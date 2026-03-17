namespace TorrentCore.Web.Connection;

public sealed class WebServiceConnectionRecord
{
    public string BaseUrl { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
