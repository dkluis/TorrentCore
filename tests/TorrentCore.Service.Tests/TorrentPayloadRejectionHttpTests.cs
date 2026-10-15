using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentPayloadRejectionHttpTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    [Fact]
    public async Task ExistingContractKeepsEventIdentityAndDoesNotClaimDeletionEarly()
    {
        var id = Guid.NewGuid(); var outcomes = new List<string>();
        var effects = Effects(async request =>
        {
            Assert.Equal("/api/acquisition/rejections", request.RequestUri!.AbsolutePath);
            Assert.Equal("unit-key", request.Headers.GetValues("X-API-Key").Single());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(id, json.RootElement.GetProperty("eventId").GetGuid());
            outcomes.Add(json.RootElement.GetProperty("cleanupOutcome").GetString()!);
            return new(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { RecoveryId = id, InfoHash = Hash, Outcome = "" }) };
        });
        var row = new TorrentPayloadRejection(id, Hash, "/downloads", ["/downloads/payload"], ["setup.exe"]);
        Assert.True((await effects.ReportAsync(row, false, default)).Confirmed);
        Assert.True((await effects.ReportAsync(row, true, default)).Confirmed);
        Assert.Equal(new[] { "Failed", "DeletedIncludingData" }, outcomes);
    }
    [Theory]
    [InlineData(503)] [InlineData(401)] [InlineData(404)]
    public async Task ServiceFailuresCannotAcknowledgeCleanup(int status)
    {
        var result = await Effects(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)))
            .ReportAsync(new(Guid.NewGuid(), Hash, "/downloads", [], ["setup.exe"]), false, default);
        Assert.False(result.Confirmed);
    }
    [Fact]
    public async Task WrongHashAndMissingConfigurationCannotBeTreatedAsManualMissingIdentity()
    {
        var row = new TorrentPayloadRejection(Guid.NewGuid(), Hash, "/downloads", [], ["setup.exe"]);
        var wrong = Effects(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
        { Content = JsonContent.Create(new { RecoveryId = Guid.NewGuid(), InfoHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", Outcome = "" }) }));
        Assert.False((await wrong.ReportAsync(row, false, default)).Confirmed);
        var missing = new TorrentPayloadRejectionEffects(new Factory(_ => throw new Exception("Unexpected request")),
            _ => Task.FromResult<(string?, string?)>((null, null)), _ => Task.FromResult<IReadOnlyList<TorrentSnapshot>>([]));
        Assert.False((await missing.ReportAsync(row, false, default)).Confirmed);
    }
    private static TorrentPayloadRejectionEffects Effects(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) =>
        new(new Factory(reply), _ => Task.FromResult<(string?, string?)>(("http://unit.invalid", "unit-key")),
            _ => Task.FromResult<IReadOnlyList<TorrentSnapshot>>([]));
    private sealed class Factory(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(new Handler(reply)); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => reply(request); }
}
