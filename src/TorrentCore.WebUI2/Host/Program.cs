using System.Globalization;
using TorrentCore.WebUI2.Host;
using TorrentCore.WebUI2.Shared;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://0.0.0.0:7510");
builder.WebHost.UseStaticWebAssets();
// The working directory is external to the app bundle and is preserved by deployment.
builder.Services.AddSingleton<IConnectionFiles>(new ConnectionFiles(Path.Combine(builder.Environment.ContentRootPath, "Config", "browser-connections")));
builder.Services.AddSingleton<ConnectionStore>();
builder.Services.AddHttpClient<ServiceGateway>(client => client.Timeout = TimeSpan.FromSeconds(35))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = exception is ArgumentException ? 400 : exception is InvalidOperationException ? 409 : 500;
        await context.Response.WriteAsJsonAsync(new { message = exception.Message }, context.RequestAborted);
    }
});
app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" });
app.MapGet("/connections", async (HttpContext context, ConnectionStore store) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(await store.ReadAsync(Browser(context), context.RequestAborted));
});
app.MapPost("/connections", async (SaveConnection request, HttpContext context, ConnectionStore store) =>
    Results.Json(await store.ChangeAsync(Browser(context), state => ConnectionRules.Save(state, request), context.RequestAborted)));
app.MapPost("/connections/use", async (SelectConnection request, HttpContext context, ConnectionStore store) =>
    Results.Json(await store.ChangeAsync(Browser(context), state => ConnectionRules.Select(state, request), context.RequestAborted)));
app.MapPost("/connections/delete", async (SelectConnection request, HttpContext context, ConnectionStore store) =>
    Results.Json(await store.ChangeAsync(Browser(context), state => ConnectionRules.Delete(state, request), context.RequestAborted)));
app.MapPost("/connections/test", async (ProbeConnection request, HttpContext context, ServiceGateway gateway) =>
{
    _ = Browser(context);
    return Results.Json(await gateway.ProbeAsync(request.Address, context.RequestAborted));
});
app.MapMethods("/service/{**path}", ["GET", "POST", "PUT"], async (string path, HttpContext context, ConnectionStore store, ServiceGateway gateway) =>
{
    var state = await store.ReadAsync(Browser(context), context.RequestAborted);
    if (!long.TryParse(context.Request.Headers["X-TorrentCore-Revision"], NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
        throw new ArgumentException("The connection revision is missing. Reload this page.");
    ConnectionRules.CheckRevision(state, revision);
    var profile = state.Profiles.Find(p => p.Id == state.ActiveProfileId) ?? throw new InvalidOperationException("Choose a service connection first.");
    await gateway.ForwardAsync(context, profile.Address, path);
});
app.MapFallbackToFile("index.html");
app.Run();

static Guid Browser(HttpContext context)
{
    if (!Guid.TryParse(context.Request.Headers["X-TorrentCore-Browser"], out var browser) || browser == Guid.Empty)
        throw new ArgumentException("Browser connection settings are missing. Reload this page.");
    return browser;
}
