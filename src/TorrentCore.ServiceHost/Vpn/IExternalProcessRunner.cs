namespace TorrentCore.Service.Vpn;

internal sealed record ExternalProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

internal sealed record ExternalProcessResult
{
    public required bool Started { get; init; }
    public required bool TimedOut { get; init; }
    public int? ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public string? FailureSummary { get; init; }

    public bool Succeeded => Started && !TimedOut && ExitCode == 0;
}

internal interface IExternalProcessRunner
{
    Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken);
}
