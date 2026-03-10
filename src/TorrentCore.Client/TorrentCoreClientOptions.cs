namespace TorrentCore.Client;

public sealed class TorrentCoreClientOptions
{
    public const string SectionName = "TorrentCoreService";

    public string BaseUrl { get; init; } = string.Empty;

    public Uri ToUri()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException("TorrentCoreService:BaseUrl is required.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"TorrentCoreService:BaseUrl '{BaseUrl}' is not a valid absolute URI.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("TorrentCoreService:BaseUrl must use http or https.");
        }

        return uri;
    }
}
