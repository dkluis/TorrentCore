using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Features;
using TorrentCore.Contracts;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.WebUI2.Shared;

namespace TorrentCore.WebUI2.Host;

public sealed class ServiceGateway(HttpClient http)
{
    public async Task<ConnectionProbeResult> ProbeAsync(string address, CancellationToken token)
    {
        var endpoint = new Uri(ConnectionRules.NormalizeAddress(address) + "/api/health");
        try
        {
            using var response = await http.GetAsync(endpoint, token);
            if (!response.IsSuccessStatusCode) return new(false, $"The service returned status {(int)response.StatusCode}.", null, null);
            var health = await response.Content.ReadFromJsonAsync<ServiceHealthDto>(token);
            if (health?.ServiceName != "TorrentCore.Service") return new(false, "This address did not identify itself as TorrentCore.Service.", null, null);
            if (health.ApiVersion > ServiceApiContract.CurrentVersion) return new(false, "This service uses a newer API version than this browser UI supports.", null, null);
            return new(true, "Connected to TorrentCore.Service.", null, null);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(false, "The TorrentCore connection test timed out.", null, null); }
        catch (HttpRequestException exception) { return new(false, $"TorrentCore could not be reached. {exception.Message}", null, null); }
        catch (System.Text.Json.JsonException) { return new(false, "This address did not return a TorrentCore health response.", null, null); }
    }

    public async Task ForwardAsync(HttpContext context, string address, string path)
    {
        if (!ServiceRoutes.IsAllowed(context.Request.Method, path)) { context.Response.StatusCode = 404; return; }
        // Keep the client-encoded category key and query unchanged across the proxy.
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (rawTarget is null || !rawTarget.StartsWith("/service/", StringComparison.Ordinal))
            throw new ArgumentException("The service request path is invalid.");
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), ConnectionRules.NormalizeAddress(address) + rawTarget["/service".Length..]);
        if (context.Request.Method is "POST" or "PUT")
        {
            request.Content = new StreamContent(context.Request.Body);
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType)) request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
            context.Response.Headers.CacheControl = "no-store";
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsJsonAsync(new { title = "TorrentCore request timed out", detail = "The operation may still be running. Refresh before repeating it." }, context.RequestAborted);
        }
        catch (HttpRequestException exception)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new { title = "TorrentCore could not be reached", detail = exception.Message }, context.RequestAborted);
        }
    }
}
