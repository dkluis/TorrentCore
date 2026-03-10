using TorrentCore.Contracts;

namespace TorrentCore.Client;

public sealed class TorrentCoreClientException(string message, int statusCode, ServiceErrorDto? serviceError = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public ServiceErrorDto? ServiceError { get; } = serviceError;
}
