namespace TorrentCore.Client;

public sealed class TorrentCoreClientOptions
{
    public const string SectionName = "TorrentCoreService";
    public       string BaseUrl { get; init; } = string.Empty;
    public       Uri    ToUri() { return ParseBaseUrl(BaseUrl); }

    public static Uri ParseBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("TorrentCoreService:BaseUrl is required.");
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"TorrentCoreService:BaseUrl '{baseUrl}' is not a valid absolute URI.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("TorrentCoreService:BaseUrl must use http.");
        }

        return uri;
    }

    public static string NormalizeBaseUrl(string? baseUrl) { return ParseBaseUrl(baseUrl).ToString(); }
}
