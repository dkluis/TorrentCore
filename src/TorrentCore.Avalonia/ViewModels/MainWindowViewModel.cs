using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TorrentCore.Avalonia.Models;
using TorrentCore.Client;

namespace TorrentCore.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Dictionary<string, NavigationSection> _sectionMap;
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly TorrentsViewModel _torrentsViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly TorrentCoreClient _client;

    [ObservableProperty]
    private NavigationSection? _selectedSection;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    [ObservableProperty]
    private string _currentTitle = "Dashboard";

    [ObservableProperty]
    private string _currentDescription = "Host health, engine saturation, and runtime policy summary.";

    public MainWindowViewModel(TorrentCoreClient client, AppConfiguration configuration)
    {
        _client = client;
        ServiceBaseUrl = configuration.TorrentCoreService.BaseUrl;

        _dashboardViewModel = new DashboardViewModel(client);
        _torrentsViewModel = new TorrentsViewModel(client, ShowTorrentDetail);
        _logsViewModel = new LogsViewModel(client);
        _settingsViewModel = new SettingsViewModel(client);

        Sections = new ObservableCollection<NavigationSection>(
        [
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
                "Edit seeding, cleanup, queue concurrency, and engine throttle settings from the desktop client."
            ),
        ]);

        _sectionMap = Sections.ToDictionary(section => section.Key, StringComparer.OrdinalIgnoreCase);
        SelectedSection = Sections[0];
    }

    public ObservableCollection<NavigationSection> Sections { get; }

    public string ServiceBaseUrl { get; }

    partial void OnSelectedSectionChanged(NavigationSection? value)
    {
        if (value is null)
        {
            return;
        }

        switch (value.Key)
        {
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
}
