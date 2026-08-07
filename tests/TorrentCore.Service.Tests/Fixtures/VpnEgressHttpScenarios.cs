using System.Net;

namespace TorrentCore.Service.Tests.Fixtures;

internal static class VpnEgressHttpScenarios
{
    // Documentation-only address ranges keep the fixtures independent of the unresolved live direct-ISP CIDR.
    public const string VpnIpv4 = "203.0.113.44";
    public const string DirectIspIpv4 = "198.51.100.27";
    public const string PublicIpv6 = "2001:db8::44";

    public static ScriptedHttpMessageHandler Create(VpnEgressHttpScenario scenario)
    {
        var handler = new ScriptedHttpMessageHandler();
        switch (scenario)
        {
            case VpnEgressHttpScenario.VpnSuccess:
                handler.EnqueueJson($$"""{"ip":"{{VpnIpv4}}"}""");
                break;
            case VpnEgressHttpScenario.DirectIsp:
                handler.EnqueueJson($$"""{"ip":"{{DirectIspIpv4}}"}""");
                break;
            case VpnEgressHttpScenario.Ipv6:
                handler.EnqueueJson($$"""{"ip":"{{PublicIpv6}}"}""");
                break;
            case VpnEgressHttpScenario.MalformedJson:
                handler.EnqueueJson("{\"ip\":");
                break;
            case VpnEgressHttpScenario.Timeout:
                handler.EnqueueException(new TimeoutException("The scripted egress request timed out."));
                break;
            case VpnEgressHttpScenario.Cancellation:
                handler.EnqueueCancellation();
                break;
            case VpnEgressHttpScenario.EndpointFailure:
                handler.EnqueueStatus(HttpStatusCode.ServiceUnavailable);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        return handler;
    }
}

public enum VpnEgressHttpScenario
{
    VpnSuccess,
    DirectIsp,
    Ipv6,
    MalformedJson,
    Timeout,
    Cancellation,
    EndpointFailure,
}
