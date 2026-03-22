namespace TorrentCore.Contracts;

public sealed class ServiceErrorDto
{
    public required string  Code    { get; init; }
    public required string  Message { get; init; }
    public          string? Target  { get; init; }
    public          string? TraceId { get; init; }
}
