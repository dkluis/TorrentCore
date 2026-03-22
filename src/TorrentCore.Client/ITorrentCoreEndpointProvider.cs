namespace TorrentCore.Client;

public interface ITorrentCoreEndpointProvider
{
    string? CurrentBaseUrl { get; }
    Uri?    CurrentBaseUri { get; }
}
