#region

using Avalonia.Controls;
using Avalonia.Threading;

#endregion

namespace TorrentCore.Avalonia.Views;

public partial class LogsView : UserControl
{
    private readonly DispatcherTimer _autoRefreshTimer = new() {Interval = TimeSpan.FromSeconds(5)};

    public LogsView()
    {
        InitializeComponent();
        _autoRefreshTimer.Tick += AutoRefreshTimerOnTick;
        AttachedToVisualTree   += (_, _) => _autoRefreshTimer.Start();
        DetachedFromVisualTree += (_, _) => _autoRefreshTimer.Stop();
    }

    private async void AutoRefreshTimerOnTick(object? sender, EventArgs e)
    {
        if (DataContext is not ViewModels.LogsViewModel {AutoRefresh: true} viewModel)
        {
            return;
        }

        await viewModel.LoadAsync();
    }
}
