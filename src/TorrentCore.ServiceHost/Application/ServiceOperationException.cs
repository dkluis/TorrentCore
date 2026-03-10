namespace TorrentCore.Service.Application;

public sealed class ServiceOperationException(string code, string message, int statusCode, string? target = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public string? Target { get; } = target;
}
