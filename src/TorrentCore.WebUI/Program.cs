using MudBlazor.Services;
using TorrentCore.Client;
using TorrentCore.WebUI.Components;
using TorrentCore.WebUI.Connection;
using TorrentCore.WebUI.Services;
using TorrentCore.WebUI.State;

var builder = WebApplication.CreateBuilder(args);
var clientOptions =
        builder.Configuration.GetSection(TorrentCoreClientOptions.SectionName).Get<TorrentCoreClientOptions>() ??
        new TorrentCoreClientOptions();
var endpointProvider = new MutableTorrentCoreEndpointProvider(clientOptions.BaseUrl);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton(clientOptions);
builder.Services.AddSingleton(endpointProvider);
builder.Services.AddSingleton<ITorrentCoreEndpointProvider>(endpointProvider);
builder.Services.AddSingleton<WebServiceConnectionStore>();
builder.Services.AddSingleton<WebServiceConnectionManager>();
builder.Services.AddScoped<ITorrentCoreApiAdapter, TorrentCoreApiAdapter>();
builder.Services.AddScoped<IOperatorFeedbackService, OperatorFeedbackService>();
builder.Services.AddScoped<IPageStateStore, CircuitPageStateStore>();
builder.Services.AddHttpClient<TorrentCoreClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
