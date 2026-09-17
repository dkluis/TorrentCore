using System.Net;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using TorrentCore.Contracts.Torrents;
using TorrentCore.WebUI2.Client.Services;
using TorrentCore.WebUI2.Shared;

namespace TorrentCore.WebUI2.Tests;

public sealed class ConnectionTests
{
    [Theory]
    [InlineData("ca-server:7500", "http://ca-server:7500")]
    [InlineData(" HTTPS://CA-SERVER:7500/ ", "https://ca-server:7500")]
    public void NativeAddressesAllowHttpHttpsAndOptionalScheme(string input, string expected)
        => Assert.Equal(expected, ConnectionRules.NormalizeAddress(input));

    [Theory]
    [InlineData("http://host/path")]
    [InlineData("http://host?query=1")]
    [InlineData("http://user:password@host")]
    [InlineData("ftp://host")]
    public void ConnectionRequiresNativeHostOnlyAddress(string input)
        => Assert.Throws<ArgumentException>(() => ConnectionRules.NormalizeAddress(input));

    [Fact]
    public void SavingOfflineProfileSelectsItAndDeletingActiveDoesNotSelectAnother()
    {
        var first = ConnectionRules.Save(new([], null, 0), new(null, "Test", "host:7500", "Test", 0));
        var second = ConnectionRules.Save(first, new(null, "Production", "other:7500", "Production", 1));
        var result = ConnectionRules.Delete(second, new(second.ActiveProfileId!.Value, 2));
        Assert.Single(result.Profiles);
        Assert.Null(result.ActiveProfileId);
        Assert.Equal(3, result.Revision);
    }

    [Fact]
    public void OldTabCannotChangeConnectionAndDuplicateAddressIsRejected()
    {
        var current = ConnectionRules.Save(new([], null, 0), new(null, "Test", "host:7500", "Test", 0));
        Assert.Throws<InvalidOperationException>(() => ConnectionRules.Delete(current, new(current.ActiveProfileId!.Value, 0)));
        Assert.Throws<ArgumentException>(() => ConnectionRules.Save(current, new(null, "Duplicate", "http://HOST:7500/", "Test", 1)));
        Assert.Throws<ArgumentException>(() => ConnectionRules.Save(current, new(null, "Unclassified", "other", "Unclassified", 1)));
    }

    [Fact]
    public async Task ExistingClientWritesCarryCapturedConnectionRevisionAndBody()
    {
        var owner = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var torrentId = Guid.NewGuid();
        var sent = false;
        using var transport = new HttpClient(new FakeHandler(async request =>
        {
            Assert.Equal(owner.ToString("D"), request.Headers.GetValues("X-TorrentCore-Browser").Single());
            if (request.RequestUri!.AbsolutePath == "/connections")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ConnectionState([new(profileId, "Test", "http://test:7500", "Test")], profileId, 4)) };
            Assert.Equal($"/service/api/torrents/{torrentId}/remove", request.RequestUri.AbsolutePath);
            Assert.Equal("4", request.Headers.GetValues("X-TorrentCore-Revision").Single());
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("\"deleteData\":true", await request.Content!.ReadAsStringAsync());
            sent = true;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new TorrentActionResultDto
            { TorrentId = torrentId, Action = "remove", State = TorrentState.Removed, ProcessedAtUtc = DateTimeOffset.UtcNow, DataDeleted = true }) };
        })) { BaseAddress = new("http://browser/") };
        await using var session = new BrowserSession(transport, new FakeJs(owner));
        await session.InitializeAsync();
        await session.CreateClient(session.Revision).RemoveAsync(torrentId, new RemoveTorrentRequest { DeleteData = true });
        Assert.True(sent);
    }

    [Fact]
    public async Task DelayedReloadCannotReplaceNewerTabConnectionState()
    {
        var oldReply = new TaskCompletionSource<HttpResponseMessage>();
        var count = 0;
        using var transport = new HttpClient(new FakeHandler(_ =>
        {
            count++;
            return count == 1 ? oldReply.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new ConnectionState([], null, 3)) });
        })) { BaseAddress = new("http://browser/") };
        await using var session = new BrowserSession(transport, new FakeJs(Guid.NewGuid()));
        var delayed = session.ReloadAsync();
        await session.ReloadAsync();
        oldReply.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(new ConnectionState([], null, 2)) });
        await delayed;
        Assert.Equal(3, session.Revision);
    }

    [Fact]
    public async Task SameConnectionReloadDoesNotResetEditorsAndReusesTransport()
    {
        using var transport = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonContent.Create(new ConnectionState([], null, 2)) }))) { BaseAddress = new("http://browser/") };
        await using var session = new BrowserSession(transport, new FakeJs(Guid.NewGuid()));
        await session.InitializeAsync();
        var changes = 0;
        session.Changed += () => changes++;
        await session.ReloadAsync();
        Assert.Equal(0, changes);
        Assert.Same(session.CreateClient(2), session.CreateClient(2));
    }

    private sealed class FakeJs(Guid owner) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromResult(identifier == "tcBrowser.owner" ? (TValue)(object)owner : default!);
    }
    internal sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request);
    }
}
