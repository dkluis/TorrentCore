using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Client;
namespace TorrentCore.Avalonia.ViewModels;

public partial class LogsViewModel(TorrentCoreClient client) : ViewModelBase
{
    [ObservableProperty]
    private bool _autoRefresh;

    [ObservableProperty]
    private int _take = 100;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _eventType = string.Empty;

    [ObservableProperty]
    private string _level = string.Empty;

    [ObservableProperty]
    private string _torrentIdText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _lastRefreshedText = string.Empty;

    public ObservableCollection<ActivityLogEntryItemViewModel> Entries { get; } = [];
    public IReadOnlyList<int> TakeOptions { get; } = [20, 50, 100, 200];
    public IReadOnlyList<string> LevelOptions { get; } = ["", "Information", "Warning", "Error"];
    public IReadOnlyList<string> CategoryOptions { get; } = ["", "startup", "engine", "torrent", "runtime"];

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasEntries => Entries.Count > 0;
    public bool HasNoEntries => !IsLoading && !HasEntries && !HasError;
    public bool HasLastRefreshed => !string.IsNullOrWhiteSpace(LastRefreshedText);

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    public async Task ClearFiltersAsync()
    {
        Category = string.Empty;
        EventType = string.Empty;
        Level = string.Empty;
        TorrentIdText = string.Empty;
        Take = 100;
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Guid? torrentId = null;
            if (!string.IsNullOrWhiteSpace(TorrentIdText))
            {
                if (!Guid.TryParse(TorrentIdText, out var parsed))
                {
                    ErrorMessage = "Torrent Id filter must be a valid GUID.";
                    return;
                }

                torrentId = parsed;
            }

            var logs = await client.GetRecentLogsAsync(
                take: Math.Max(Take, 1),
                category: string.IsNullOrWhiteSpace(Category) ? null : Category,
                eventType: string.IsNullOrWhiteSpace(EventType) ? null : EventType,
                level: string.IsNullOrWhiteSpace(Level) ? null : Level,
                torrentId: torrentId);

            Entries.Clear();
            foreach (var entry in logs)
            {
                Entries.Add(new ActivityLogEntryItemViewModel(entry));
            }

            LastRefreshedText = DateTimeOffset.Now.ToString("g");
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to load logs: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(HasNoEntries));
            OnPropertyChanged(nameof(HasLastRefreshed));
        }
    }
}
