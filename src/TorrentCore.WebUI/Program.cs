using MudBlazor.Services;
using TorrentCore.Client;
using TorrentCore.WebUI.Components;

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
builder.Services.AddHttpClient<TorrentCoreClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
