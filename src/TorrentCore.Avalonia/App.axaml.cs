using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TorrentCore.Avalonia.Infrastructure;
using TorrentCore.Avalonia.Models;
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
        var serviceUri = clientOptions.ToUri();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton(clientOptions);
        services.AddHttpClient<TorrentCoreClient>(client => client.BaseAddress = serviceUri);
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
