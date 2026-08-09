using System.Net;

namespace TorrentCore.Service.Vpn;

internal enum VpnEgressValidationOutcome
{
    ValidatedEgress,
    DirectIsp,
    InvalidResponse,
    TimedOut,
    Cancelled,
    EndpointFailure,
    UnexpectedFailure,
}

internal enum VpnEgressEndpointFailureReason
{
    HttpStatus,
    Dns,
    Connection,
    Tls,
    HttpProtocol,
    OtherHttp,
}

internal sealed record VpnEgressValidationResult
{
    public required VpnEgressValidationOutcome Outcome { get; init; }
    public VpnEgressEndpointFailureReason? EndpointFailureReason { get; init; }
    public IPAddress? ObservedAddress { get; init; }
    public HttpStatusCode? HttpStatusCode { get; init; }
    public required DateTimeOffset CheckedAtUtc { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? FailureSummary { get; init; }

    public bool IsValidated => Outcome == VpnEgressValidationOutcome.ValidatedEgress;
}
