using TorrentCore.Web.Components;
using TorrentCore.Client;

var builder = WebApplication.CreateBuilder(args);
var clientOptions = builder.Configuration.GetSection(TorrentCoreClientOptions.SectionName).Get<TorrentCoreClientOptions>() ??
                    new TorrentCoreClientOptions();
var serviceUri = clientOptions.ToUri();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient<TorrentCoreClient>(client => client.BaseAddress = serviceUri);
builder.Services.AddSingleton(clientOptions);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
