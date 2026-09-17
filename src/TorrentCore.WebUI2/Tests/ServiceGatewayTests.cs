using System.Net;
using Microsoft.AspNetCore.Http.Features;
using TorrentCore.WebUI2.Host;

namespace TorrentCore.WebUI2.Tests;

public sealed class ServiceGatewayTests
{
    [Theory]
    [InlineData("GET", "api/host/runtime-settings", true)]
    [InlineData("PUT", "api/host/runtime-settings", true)]
    [InlineData("POST", "api/host/runtime-settings", false)]
    [InlineData("POST", "api/host/restart-service", true)]
    [InlineData("POST", "api/maintenance/logs/cleanup", true)]
    [InlineData("GET", "api/configuration", false)]
    [InlineData("GET", "api/../health", false)]
    [InlineData("GET", "http://another-host/api/health", false)]
    public void GatewayOnlyExposesExistingOperatorRoutes(string method, string path, bool expected)
        => Assert.Equal(expected, ServiceRoutes.IsAllowed(method, path));

    [Theory]
    [InlineData("metadata/reset")]
    [InlineData("completion-callback/retry")]
    [InlineData("resume-on-hold")]
    [InlineData("remove")]
    public void ExistingTorrentActionsPassWithoutChangingContract(string action)
        => Assert.True(ServiceRoutes.IsAllowed("POST", $"api/torrents/{Guid.NewGuid()}/{action}"));

    [Fact]
    public async Task ForwardingPreservesDeleteBodyAndServiceFailure()
    {
        using var http = new HttpClient(new ConnectionTests.FakeHandler(async request =>
        {
            Assert.Equal("https://test-host:7500/api/torrents/00000000-0000-0000-0000-000000000001/remove", request.RequestUri!.ToString());
            Assert.Equal("{\"deleteData\":false}", await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.Conflict) { Content = new StringContent("{\"detail\":\"Busy\"}", System.Text.Encoding.UTF8, "application/problem+json") };
        }));
        var context = new DefaultHttpContext();
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/service/api/torrents/00000000-0000-0000-0000-000000000001/remove";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"deleteData\":false}"));
        context.Response.Body = new MemoryStream();
        await new ServiceGateway(http).ForwardAsync(context, "https://test-host:7500", "api/torrents/00000000-0000-0000-0000-000000000001/remove");
        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Equal("{\"detail\":\"Busy\"}", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
    [Theory]
    [InlineData("two%20words", "two words")]
    [InlineData("percent%2520", "percent%20")]
    [InlineData("part%2Fkey", "part%2Fkey")]
    public async Task CategoryForwardingKeepsClientEncoding(string encoded, string routed)
    {
        using var http = new HttpClient(new ConnectionTests.FakeHandler(request =>
        {
            Assert.Equal($"https://test-host/api/categories/{encoded}", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }));
        var context = new DefaultHttpContext();
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = $"/service/api/categories/{encoded}";
        context.Request.Method = "PUT";
        context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();
        await new ServiceGateway(http).ForwardAsync(context, "https://test-host", $"api/categories/{routed}");
    }

}
