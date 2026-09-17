using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using TorrentCore.Client;
using TorrentCore.WebUI2.Shared;

namespace TorrentCore.WebUI2.Client.Services;

public sealed class BrowserSession(HttpClient http, IJSRuntime js) : IAsyncDisposable
{
    public ConnectionState State { get; private set; } = new([], null, 0);
    public ConnectionProfile? ActiveProfile => State.Profiles.Find(x => x.Id == State.ActiveProfileId);
    public long Revision => State.Revision;
    public bool IsInitialized { get; private set; }
    public event Action? Changed;
    private Guid _owner;
    private readonly Dictionary<long, (HttpClient Http, TorrentCoreClient Client)> _clients = [];
    private DotNetObjectReference<BrowserSession>? _self;
    public async Task InitializeAsync()
    {
        if (IsInitialized) return;
        _owner = await js.InvokeAsync<Guid>("tcBrowser.owner");
        _self = DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("tcBrowser.subscribeConnections", _self);
        await ReloadAsync();
        IsInitialized = true;
        Changed?.Invoke();
    }
    [JSInvokable] public async Task ReloadAsync()
    {
        using var request = Request(HttpMethod.Get, "connections");
        using var response = await http.SendAsync(request);
        await RequireSuccessAsync(response);
        var next = await response.Content.ReadFromJsonAsync<ConnectionState>() ?? throw new InvalidOperationException("Connection settings were not returned.");
        if (next.Revision < State.Revision || IsInitialized && next.Revision == State.Revision) return;
        State = next;
        Changed?.Invoke();
    }
    public async Task SaveAsync(SaveConnection value) => await ChangeAsync(HttpMethod.Post, "connections", value);
    public async Task UseAsync(Guid id) => await ChangeAsync(HttpMethod.Post, "connections/use", new SelectConnection(id, Revision));
    public async Task DeleteAsync(Guid id) => await ChangeAsync(HttpMethod.Post, "connections/delete", new SelectConnection(id, Revision));
    public async Task<ConnectionProbeResult> TestAsync(string address)
    {
        using var request = Request(HttpMethod.Post, "connections/test");
        request.Content = JsonContent.Create(new ProbeConnection(address));
        using var response = await http.SendAsync(request);
        await RequireSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ConnectionProbeResult>() ?? throw new InvalidOperationException("Connection test returned no result.");
    }
    private async Task ChangeAsync<T>(HttpMethod method, string path, T body)
    {
        using var request = Request(method, path);
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request);
        await RequireSuccessAsync(response);
        var next = await response.Content.ReadFromJsonAsync<ConnectionState>() ?? throw new InvalidOperationException("Connection settings were not returned.");
        if (next.Revision < State.Revision) return;
        State = next;
        Changed?.Invoke();
        await js.InvokeVoidAsync("tcBrowser.connectionChanged");
    }
    public TorrentCoreClient CreateClient(long revision)
    {
        if (_clients.TryGetValue(revision, out var existing)) return existing.Client;
        var forwarding = new HttpClient(new RevisionHandler(http, _owner, revision));
        var client = new TorrentCoreClient(forwarding, new Endpoint(new Uri(http.BaseAddress!, "service/")));
        _clients.Add(revision, (forwarding, client));
        return client;
    }
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-TorrentCore-Browser", _owner.ToString("D"));
        return request;
    }
    private static async Task RequireSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = await response.Content.ReadAsStringAsync();
        try
        {
            using var json = JsonDocument.Parse(message);
            if (json.RootElement.TryGetProperty("message", out var detail)) message = detail.GetString() ?? message;
        }
        catch (JsonException) { }
        throw new InvalidOperationException(message);
    }
    private sealed class Endpoint(Uri address) : ITorrentCoreEndpointProvider
    {
        public string CurrentBaseUrl => address.ToString();
        public Uri CurrentBaseUri => address;
    }
    private sealed class RevisionHandler(HttpClient transport, Guid owner, long revision) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            forwarded.Headers.Add("X-TorrentCore-Browser", owner.ToString("D"));
            forwarded.Headers.Add("X-TorrentCore-Revision", revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (request.Content is not null)
            {
                forwarded.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(token));
                foreach (var header in request.Content.Headers)
                    forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return await transport.SendAsync(forwarded, token);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await js.InvokeVoidAsync("tcBrowser.unsubscribeConnections");
        _self?.Dispose();
        foreach (var entry in _clients.Values) entry.Http.Dispose();
        _clients.Clear();
    }
}
