#region

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using TorrentCore.Avalonia.ViewModels;
using TorrentCore.Avalonia.Views;

#endregion

namespace TorrentCore.Avalonia;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        return data switch
        {
            ConnectionSetupViewModel viewModel => new ConnectionSetupView {DataContext = viewModel},
            DashboardViewModel viewModel => new DashboardView {DataContext = viewModel},
            TorrentsViewModel viewModel => new TorrentsView {DataContext = viewModel},
            TorrentDetailViewModel viewModel => new TorrentDetailView {DataContext = viewModel},
            LogsViewModel viewModel => new LogsView {DataContext = viewModel},
            SettingsViewModel viewModel => new SettingsView {DataContext = viewModel},
            _ => new TextBlock {Text = $"No view registered for {data?.GetType().Name ?? "null"}."},
        };
    }

    public bool Match(object? data) { return data is ViewModelBase; }
}
