using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Vpn;

internal sealed class VpnEgressProbe(
    IHttpClientFactory httpClientFactory,
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext,
    TimeProvider timeProvider) : IVpnEgressProbe
{
    internal const string HttpClientName = "VpnEgressProbe";
    internal const int MaximumResponseBytes = 16 * 1024;

    private readonly SemaphoreSlim _logGate = new(1, 1);
    private LogSignature? _lastLoggedSignature;

    public async Task<VpnEgressValidationResult> ValidateAsync(
        RuntimeSettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var startedTimestamp = timeProvider.GetTimestamp();
        var endpoint = settings.VpnEgressValidationEndpoint;
        VpnEgressValidationResult result;

        try
        {
            using var timeoutSource = new CancellationTokenSource(
                TimeSpan.FromSeconds(settings.VpnEgressRequestTimeoutSeconds),
                timeProvider
            );
            using var requestSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token
            );
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestSource.Token
            );

            if (!response.IsSuccessStatusCode)
            {
                result = CreateResult(
                    VpnEgressValidationOutcome.EndpointFailure,
                    startedTimestamp,
                    endpointFailureReason: VpnEgressEndpointFailureReason.HttpStatus,
                    httpStatusCode: response.StatusCode,
                    failureSummary: $"HTTP status {(int)response.StatusCode}."
                );
            }
            else
            {
                result = await ClassifySuccessfulResponseAsync(
                    response,
                    settings.VpnEgressDirectIspCidrs,
                    startedTimestamp,
                    requestSource.Token
                );
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(VpnEgressValidationOutcome.Cancelled, startedTimestamp);
        }
        catch (OperationCanceledException)
        {
            result = CreateResult(
                VpnEgressValidationOutcome.TimedOut,
                startedTimestamp,
                failureSummary: "The egress validation request timed out."
            );
        }
        catch (TimeoutException)
        {
            result = CreateResult(
                VpnEgressValidationOutcome.TimedOut,
                startedTimestamp,
                failureSummary: "The egress validation request timed out."
            );
        }
        catch (HttpRequestException exception)
        {
            var reason = MapHttpFailureReason(exception.HttpRequestError);
            result = CreateResult(
                VpnEgressValidationOutcome.EndpointFailure,
                startedTimestamp,
                endpointFailureReason: reason,
                httpStatusCode: exception.StatusCode,
                failureSummary: GetHttpFailureSummary(reason)
            );
        }
        catch (Exception exception)
        {
            result = CreateResult(
                VpnEgressValidationOutcome.UnexpectedFailure,
                startedTimestamp,
                failureSummary: $"Unexpected failure ({exception.GetType().Name})."
            );
        }

        await WriteChangedResultAsync(result, endpoint);
        return result;
    }

    private async Task<VpnEgressValidationResult> ClassifySuccessfulResponseAsync(
        HttpResponseMessage response,
        IReadOnlyList<string> directIspCidrs,
        long startedTimestamp,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            return CreateInvalidResponse(startedTimestamp, "The response exceeded the 16 KiB limit.");
        }

        byte[] responseBytes;
        try
        {
            responseBytes = await ReadBoundedContentAsync(response.Content, cancellationToken);
        }
        catch (ResponseTooLargeException)
        {
            return CreateInvalidResponse(startedTimestamp, "The response exceeded the 16 KiB limit.");
        }

        EgressAddressResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<EgressAddressResponse>(responseBytes);
        }
        catch (JsonException)
        {
            return CreateInvalidResponse(startedTimestamp, "The response was not valid JSON.");
        }

        if (string.IsNullOrWhiteSpace(payload?.Ip) ||
            !IPAddress.TryParse(payload.Ip, out var observedAddress))
        {
            return CreateInvalidResponse(startedTimestamp, "The response did not contain a valid IP address.");
        }

        if (observedAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return CreateResult(
                VpnEgressValidationOutcome.InvalidResponse,
                startedTimestamp,
                observedAddress: observedAddress,
                failureSummary: "The response did not contain an IPv4 address."
            );
        }

        var isDirectIsp = directIspCidrs.Any(cidr =>
            Ipv4CidrBlock.TryParse(cidr, out var block) && block.Contains(observedAddress)
        );

        return CreateResult(
            isDirectIsp
                ? VpnEgressValidationOutcome.DirectIsp
                : VpnEgressValidationOutcome.ValidatedEgress,
            startedTimestamp,
            observedAddress: observedAddress,
            failureSummary: isDirectIsp
                ? "Observed public IPv4 matched a configured direct ISP CIDR."
                : null
        );
    }

    private async Task WriteChangedResultAsync(VpnEgressValidationResult result, string endpoint)
    {
        if (result.Outcome == VpnEgressValidationOutcome.Cancelled)
        {
            return;
        }

        var signature = new LogSignature(result.Outcome, result.EndpointFailureReason);
        await _logGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_lastLoggedSignature == signature)
            {
                return;
            }

            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = result.IsValidated ? ActivityLogLevel.Information : ActivityLogLevel.Warning,
                    Category = VpnEgressActivityEvents.Category,
                    EventType = VpnEgressActivityEvents.ValidationCompleted,
                    Message = CreateLogMessage(result),
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            Outcome = result.Outcome.ToString(),
                            EndpointFailureReason = result.EndpointFailureReason?.ToString(),
                            EndpointAuthority = GetEndpointAuthority(endpoint),
                            DurationMilliseconds = result.Duration.TotalMilliseconds,
                            ObservedAddress = result.ObservedAddress?.ToString(),
                            HttpStatusCode = (int?)result.HttpStatusCode,
                            result.FailureSummary,
                        }
                    ),
                },
                CancellationToken.None
            );

            _lastLoggedSignature = signature;
        }
        finally
        {
            _logGate.Release();
        }
    }

    private VpnEgressValidationResult CreateInvalidResponse(long startedTimestamp, string failureSummary)
        => CreateResult(
            VpnEgressValidationOutcome.InvalidResponse,
            startedTimestamp,
            failureSummary: failureSummary
        );

    private VpnEgressValidationResult CreateResult(
        VpnEgressValidationOutcome outcome,
        long startedTimestamp,
        VpnEgressEndpointFailureReason? endpointFailureReason = null,
        IPAddress? observedAddress = null,
        HttpStatusCode? httpStatusCode = null,
        string? failureSummary = null)
        => new()
        {
            Outcome = outcome,
            EndpointFailureReason = endpointFailureReason,
            ObservedAddress = observedAddress,
            HttpStatusCode = httpStatusCode,
            CheckedAtUtc = timeProvider.GetUtcNow(),
            Duration = timeProvider.GetElapsedTime(startedTimestamp),
            FailureSummary = failureSummary,
        };

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var readBuffer = new byte[4096];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, cancellationToken);
            if (bytesRead == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + bytesRead > MaximumResponseBytes)
            {
                throw new ResponseTooLargeException();
            }

            buffer.Write(readBuffer, 0, bytesRead);
        }
    }

    private static VpnEgressEndpointFailureReason MapHttpFailureReason(HttpRequestError error)
        => error switch
        {
            HttpRequestError.NameResolutionError => VpnEgressEndpointFailureReason.Dns,
            HttpRequestError.ConnectionError => VpnEgressEndpointFailureReason.Connection,
            HttpRequestError.SecureConnectionError => VpnEgressEndpointFailureReason.Tls,
            HttpRequestError.HttpProtocolError or
                HttpRequestError.InvalidResponse or
                HttpRequestError.ResponseEnded or
                HttpRequestError.VersionNegotiationError => VpnEgressEndpointFailureReason.HttpProtocol,
            _ => VpnEgressEndpointFailureReason.OtherHttp,
        };

    private static string GetHttpFailureSummary(VpnEgressEndpointFailureReason reason)
        => reason switch
        {
            VpnEgressEndpointFailureReason.Dns => "The endpoint name could not be resolved.",
            VpnEgressEndpointFailureReason.Connection => "A connection to the endpoint could not be established.",
            VpnEgressEndpointFailureReason.Tls => "The secure connection to the endpoint failed.",
            VpnEgressEndpointFailureReason.HttpProtocol => "The endpoint returned an HTTP protocol failure.",
            _ => "The endpoint request failed.",
        };

    private static string CreateLogMessage(VpnEgressValidationResult result)
        => result.Outcome switch
        {
            VpnEgressValidationOutcome.ValidatedEgress => "VPN egress validation completed with validated egress.",
            VpnEgressValidationOutcome.DirectIsp => "VPN egress validation detected a configured direct-ISP address.",
            VpnEgressValidationOutcome.InvalidResponse => "VPN egress validation received an invalid response.",
            VpnEgressValidationOutcome.TimedOut => "VPN egress validation timed out.",
            VpnEgressValidationOutcome.EndpointFailure =>
                $"VPN egress validation endpoint failed ({result.EndpointFailureReason}).",
            _ => "VPN egress validation failed unexpectedly.",
        };

    private static string? GetEndpointAuthority(string endpoint)
        => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)
            : null;

    private sealed record EgressAddressResponse([property: JsonPropertyName("ip")] string? Ip);

    private sealed record LogSignature(
        VpnEgressValidationOutcome Outcome,
        VpnEgressEndpointFailureReason? EndpointFailureReason);

    private sealed class ResponseTooLargeException : Exception;
}
