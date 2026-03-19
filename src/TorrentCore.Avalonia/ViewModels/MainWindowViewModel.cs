using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TorrentCore.Avalonia.Infrastructure;
using TorrentCore.Avalonia.Models;
using TorrentCore.Client;

namespace TorrentCore.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Dictionary<string, NavigationSection> _sectionMap;
    private readonly AvaloniaServiceConnectionManager _connectionManager;
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly TorrentsViewModel _torrentsViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ConnectionSetupViewModel _connectionSetupViewModel;
    private readonly TorrentCoreClient _client;

    [ObservableProperty]
    private NavigationSection? _selectedSection;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    [ObservableProperty]
    private string _currentTitle = "Service Connection";

    [ObservableProperty]
    private string _currentDescription = "Connect this desktop client to a TorrentCore service on the local network.";

    [ObservableProperty]
    private string _serviceBaseUrl = "Not configured";

    [ObservableProperty]
    private string _serviceStatusSummary = "Checking TorrentCore service connectivity...";

    [ObservableProperty]
    private bool _isConnectionSetupRequired = true;

    public MainWindowViewModel(TorrentCoreClient client, AvaloniaServiceConnectionManager connectionManager)
    {
        _client = client;
        _connectionManager = connectionManager;

        _dashboardViewModel = new DashboardViewModel(client);
        _torrentsViewModel = new TorrentsViewModel(client, ShowTorrentDetail);
        _logsViewModel = new LogsViewModel(client);
        _settingsViewModel = new SettingsViewModel(client);
        _connectionSetupViewModel = new ConnectionSetupViewModel(connectionManager, HandleConnectionSavedAsync);

        Sections = new ObservableCollection<NavigationSection>(
        [
            new NavigationSection(
                "connection",
                "Connection",
                "Service target",
                "Test and save the desktop app's TorrentCore service endpoint."
            ),
            new NavigationSection(
                "dashboard",
                "Dashboard",
                "Host and engine status",
                "Dashboard cards for service health, engine runtime, queue pressure, and storage policy."
            ),
            new NavigationSection(
                "torrents",
                "Torrents",
                "Magnets and lifecycle actions",
                "Add magnets, filter the torrent list, select multiple torrents, and run operator actions."
            ),
            new NavigationSection(
                "logs",
                "Logs",
                "Recent activity and errors",
                "Inspect persisted service, torrent, and engine events with lightweight filtering."
            ),
            new NavigationSection(
                "settings",
                "Settings",
                "Runtime policy controls",
                "Edit seeding, cleanup, queue concurrency, callback settings, and category routing from the desktop client."
            ),
        ]);

        _sectionMap = Sections.ToDictionary(section => section.Key, StringComparer.OrdinalIgnoreCase);
        CurrentViewModel = _connectionSetupViewModel;
    }

    public ObservableCollection<NavigationSection> Sections { get; }

    public bool CanNavigate => !IsConnectionSetupRequired;

    public async Task InitializeAsync()
    {
        var probeResult = await _connectionManager.GetStatusAsync();
        await ApplyConnectionStatusAsync(probeResult);
    }

    partial void OnIsConnectionSetupRequiredChanged(bool value) => OnPropertyChanged(nameof(CanNavigate));

    partial void OnSelectedSectionChanged(NavigationSection? value)
    {
        if (value is null)
        {
            return;
        }

        if (IsConnectionSetupRequired && !string.Equals(value.Key, "connection", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (value.Key)
        {
            case "connection":
                CurrentViewModel = _connectionSetupViewModel;
                CurrentTitle = value.Title;
                CurrentDescription = value.Description;
                _ = _connectionSetupViewModel.LoadAsync();
                break;
            case "dashboard":
                CurrentViewModel = _dashboardViewModel;
                CurrentTitle = value.Title;
                CurrentDescription = value.Description;
                _ = _dashboardViewModel.LoadAsync();
                break;
            case "torrents":
                CurrentViewModel = _torrentsViewModel;
                CurrentTitle = value.Title;
                CurrentDescription = value.Description;
                _ = _torrentsViewModel.LoadAsync();
                break;
            case "logs":
                CurrentViewModel = _logsViewModel;
                CurrentTitle = value.Title;
                CurrentDescription = value.Description;
                _ = _logsViewModel.LoadAsync();
                break;
            case "settings":
                CurrentViewModel = _settingsViewModel;
                CurrentTitle = value.Title;
                CurrentDescription = value.Description;
                _ = _settingsViewModel.LoadAsync();
                break;
        }
    }

    private void ShowTorrentDetail(Guid torrentId)
    {
        CurrentTitle = "Torrent Detail";
        CurrentDescription = "Runtime, transfer, identity, and recent log diagnostics for the selected torrent.";
        CurrentViewModel = new TorrentDetailViewModel(_client, torrentId, ShowTorrents);
    }

    private void ShowTorrents()
    {
        if (_sectionMap.TryGetValue("torrents", out var torrentsSection))
        {
            SelectedSection = torrentsSection;
        }
    }

    private async Task HandleConnectionSavedAsync()
    {
        var probeResult = await _connectionManager.RefreshAsync();
        await ApplyConnectionStatusAsync(probeResult);
    }

    private async Task ApplyConnectionStatusAsync(TorrentCoreConnectionProbeResult probeResult)
    {
        ServiceBaseUrl = probeResult.BaseUrl ?? _connectionManager.DefaultBaseUrl ?? "Not configured";
        ServiceStatusSummary = probeResult.IsReachable
            ? "Reachable"
            : probeResult.ErrorMessage ?? "Unavailable";

        if (!probeResult.IsReachable)
        {
            IsConnectionSetupRequired = true;
            if (_sectionMap.TryGetValue("connection", out var connectionSection))
            {
                SelectedSection = connectionSection;
            }

            CurrentTitle = "Service Connection";
            CurrentDescription = "Connect this desktop client to a TorrentCore service on the local network.";
            CurrentViewModel = _connectionSetupViewModel;
            await _connectionSetupViewModel.LoadAsync();
            return;
        }

        IsConnectionSetupRequired = false;
        if (_sectionMap.TryGetValue("dashboard", out var dashboardSection))
        {
            SelectedSection = dashboardSection;
        }
    }
}
