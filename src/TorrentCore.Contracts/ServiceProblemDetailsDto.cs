namespace TorrentCore.Contracts;

public sealed class ServiceProblemDetailsDto
{
    public string? Type     { get; init; }
    public string? Title    { get; init; }
    public int?    Status   { get; init; }
    public string? Detail   { get; init; }
    public string? Instance { get; init; }
    public string? Code     { get; init; }
    public string? Target   { get; init; }
    public string? TraceId  { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
