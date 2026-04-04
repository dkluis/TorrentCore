#region

using TorrentCore.Client;
using TorrentCore.Web.Components;
using TorrentCore.Web.Connection;

#endregion

var builder = WebApplication.CreateBuilder(args);
var clientOptions =
        builder.Configuration.GetSection(TorrentCoreClientOptions.SectionName).Get<TorrentCoreClientOptions>() ??
        new TorrentCoreClientOptions();
var endpointProvider = new MutableTorrentCoreEndpointProvider(clientOptions.BaseUrl);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(clientOptions);
builder.Services.AddSingleton(endpointProvider);
builder.Services.AddSingleton<ITorrentCoreEndpointProvider>(endpointProvider);
builder.Services.AddSingleton<WebServiceConnectionStore>();
builder.Services.AddSingleton<WebServiceConnectionManager>();
builder.Services.AddHttpClient<TorrentCoreClient>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
