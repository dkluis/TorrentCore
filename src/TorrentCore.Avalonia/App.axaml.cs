using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TorrentCore.Avalonia.Infrastructure;
using TorrentCore.Avalonia.ViewModels;
using TorrentCore.Client;

namespace TorrentCore.Avalonia;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() =>
        AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var configuration = AppConfigLoader.Load();
        var clientOptions = configuration.TorrentCoreService;
        var endpointProvider = new MutableTorrentCoreEndpointProvider(clientOptions.BaseUrl);

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton(clientOptions);
        services.AddSingleton(endpointProvider);
        services.AddSingleton<ITorrentCoreEndpointProvider>(endpointProvider);
        services.AddSingleton<AppConnectionSettingsStore>();
        services.AddSingleton<AvaloniaServiceConnectionManager>();
        services.AddHttpClient<TorrentCoreClient>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
            _ = _services.GetRequiredService<MainWindowViewModel>().InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
