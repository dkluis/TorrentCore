using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using Microsoft.JSInterop;
using MudBlazor.Services;
using TorrentCore.WebUI2.Client;
using TorrentCore.WebUI2.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress), Timeout = TimeSpan.FromSeconds(45) });
builder.Services.AddScoped<BrowserSession>();
builder.Services.AddScoped<BrowserPreferences>();
builder.Services.AddMudServices(options =>
{
    options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;
    options.SnackbarConfiguration.PreventDuplicates = true;
    options.SnackbarConfiguration.ShowCloseIcon = true;
});
var host = builder.Build();
var language = await host.Services.GetRequiredService<IJSRuntime>().InvokeAsync<string>("tcBrowser.language");
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(language);
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.DefaultThreadCurrentCulture;
await host.RunAsync();
