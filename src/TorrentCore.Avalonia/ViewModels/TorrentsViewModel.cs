#region

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Avalonia.Infrastructure;
using TorrentCore.Client;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Torrents;

#endregion

namespace TorrentCore.Avalonia.ViewModels;

public partial class TorrentsViewModel(TorrentCoreClient client, Action<Guid> showTorrentDetail,
    IClipboardTextService                                clipboardTextService) : ViewModelBase
{
    private const    string                         AllCategoryFilterKey = "__all";
    private const    string                         UncategorizedCategoryFilterKey = "__uncategorized";
    private readonly List<TorrentListItemViewModel> _allTorrents = [];
    private readonly Dictionary<string, string>     _categoryDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    [ObservableProperty]
    private string? _actionMessage;
    [ObservableProperty]
    private string? _errorMessage;
    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private bool _isLoading;
    [ObservableProperty]
    private string _magnetUri = string.Empty;
    [ObservableProperty]
    private string _nameFilter = string.Empty;
    [ObservableProperty]
    private TorrentCategoryOptionViewModel? _selectedAddCategory;
    [ObservableProperty]
    private string _selectedCallbackState = "All";
    [ObservableProperty]
    private TorrentCategoryOptionViewModel? _selectedCategoryFilter;
    [ObservableProperty]
    private string _selectedSort = "Added (Newest)";
    [ObservableProperty]
    private string _selectedStatus = "All";
    [ObservableProperty]
    private string? _submitMessage;
    public ObservableCollection<TorrentListItemViewModel> VisibleTorrents { get; } = [];
    public ObservableCollection<TorrentCategoryOptionViewModel> AddCategoryOptions { get; } = [];
    public ObservableCollection<TorrentCategoryOptionViewModel> CategoryFilterOptions { get; } = [];
    public ObservableCollection<string> StatusOptions { get; } = new(["All", ..Enum.GetNames<TorrentState>()]);
    public ObservableCollection<string> CallbackStateOptions { get; } = new(
        [
            "All", "PendingFinalization", "Invoked", "Failed", "TimedOut",
        ]
    );
    public ObservableCollection<string> SortOptions { get; } = new(
        ["Added (Newest)", "Name", "State", "Progress (High to Low)"]
    );
    public  int  SelectedTorrentCount => _allTorrents.Count(item => item.IsSelected);
    public  int  TotalTorrentCount => _allTorrents.Count;
    public  int  VisibleTorrentCount => VisibleTorrents.Count;
    public  bool HasVisibleTorrents => VisibleTorrentCount > 0;
    public  bool HasNoVisibleTorrents => !IsLoading && !HasVisibleTorrents;
    public  bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public  bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);
    public  bool HasSubmitMessage => !string.IsNullOrWhiteSpace(SubmitMessage);
    public  bool CanRunBulkActions => !IsLoading && !IsBusy && SelectedTorrentCount > 0;
    partial void OnNameFilterChanged(string                                      value) { RebuildVisibleTorrents(); }
    partial void OnSelectedStatusChanged(string                                  value) { RebuildVisibleTorrents(); }
    partial void OnSelectedCategoryFilterChanged(TorrentCategoryOptionViewModel? value) { RebuildVisibleTorrents(); }
    partial void OnSelectedCallbackStateChanged(string                           value) { RebuildVisibleTorrents(); }
    partial void OnSelectedSortChanged(string                                    value) { RebuildVisibleTorrents(); }

    [RelayCommand]
    public async Task RefreshAsync() { await LoadAsync(); }

    [RelayCommand]
    public async Task AddMagnetAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(MagnetUri))
        {
            return;
        }

        IsBusy        = true;
        SubmitMessage = null;
        ActionMessage = null;
        ErrorMessage  = null;

        try
        {
            var detail = await client.AddMagnetAsync(
                new AddMagnetRequest
                {
                    MagnetUri   = MagnetUri.Trim(),
                    CategoryKey = string.IsNullOrWhiteSpace(SelectedAddCategory?.Key) ? null : SelectedAddCategory.Key,
                }
            );
            SubmitMessage       = $"Added torrent '{detail.Name}' in state '{detail.State}'.";
            MagnetUri           = string.Empty;
            SelectedAddCategory = ResolveDefaultAddCategory();
            await LoadAsync();
        }
        catch (TorrentCoreClientException exception)
        {
            SubmitMessage = exception.ServiceError?.Message ?? exception.Message;
        }
        catch (Exception exception)
        {
            SubmitMessage = $"Unable to add torrent: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RaiseComputedState();
        }
    }

    [RelayCommand]
    public async Task PasteMagnetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        SubmitMessage = null;
        ActionMessage = null;
        ErrorMessage  = null;

        try
        {
            var clipboardText = await clipboardTextService.GetTextAsync();
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                SubmitMessage = "Clipboard does not contain text to paste.";
                return;
            }

            MagnetUri     = clipboardText.Trim();
            SubmitMessage = "Pasted magnet text from the clipboard.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to read the clipboard: {exception.Message}";
        }
        finally
        {
            RaiseComputedState();
        }
    }

    [RelayCommand]
    public async Task PauseSelectedAsync()
    {
        await RunBulkActionAsync("Pause selected", item => item.CanPause, item => client.PauseAsync(item.TorrentId));
    }

    [RelayCommand]
    public async Task ResumeSelectedAsync()
    {
        await RunBulkActionAsync("Resume selected", item => item.CanResume, item => client.ResumeAsync(item.TorrentId));
    }

    [RelayCommand]
    public async Task RemoveSelectedAsync()
    {
        await RunBulkActionAsync(
            "Remove selected", item => item.CanRemove,
            item => client.RemoveAsync(item.TorrentId, new RemoveTorrentRequest {DeleteData = false})
        );
    }

    [RelayCommand]
    public void SelectVisible()
    {
        foreach (var torrent in VisibleTorrents)
        {
            torrent.IsSelected = true;
        }

        RaiseComputedState();
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var torrent in _allTorrents)
        {
            torrent.IsSelected = false;
        }

        RaiseComputedState();
    }

    [RelayCommand]
    public async Task PauseAsync(TorrentListItemViewModel? torrent)
    {
        if (torrent is null || torrent.IsBusy)
        {
            return;
        }

        await RunItemActionAsync(
            torrent, async () =>
            {
                var result = await client.PauseAsync(torrent.TorrentId);
                ActionMessage = $"Paused '{torrent.Name}' at {result.ProcessedAtUtc.ToLocalTime():g}.";
            }
        );
    }

    [RelayCommand]
    public async Task ResumeAsync(TorrentListItemViewModel? torrent)
    {
        if (torrent is null || torrent.IsBusy)
        {
            return;
        }

        await RunItemActionAsync(
            torrent, async () =>
            {
                var result = await client.ResumeAsync(torrent.TorrentId);
                ActionMessage = $"Resumed '{torrent.Name}' at {result.ProcessedAtUtc.ToLocalTime():g}.";
            }
        );
    }

    [RelayCommand]
    public async Task RefreshMetadataAsync(TorrentListItemViewModel? torrent)
    {
        if (torrent is null || torrent.IsBusy || !torrent.CanRefreshMetadata)
        {
            return;
        }

        await RunItemActionAsync(
            torrent, async () =>
            {
                var result = await client.RefreshMetadataAsync(torrent.TorrentId);
                ActionMessage =
                        $"Requested metadata refresh for '{torrent.Name}' at {result.ProcessedAtUtc.ToLocalTime():g}.";
            }
        );
    }

    [RelayCommand]
    public async Task ResetMetadataSessionAsync(TorrentListItemViewModel? torrent)
    {
        if (torrent is null || torrent.IsBusy || !torrent.CanRefreshMetadata)
        {
            return;
        }

        await RunItemActionAsync(
            torrent, async () =>
            {
                var result = await client.ResetMetadataSessionAsync(torrent.TorrentId);
                ActionMessage =
                        $"Recreated metadata session for '{torrent.Name}' at {result.ProcessedAtUtc.ToLocalTime():g}.";
            }
        );
    }

    [RelayCommand]
    public async Task RemoveAsync(TorrentListItemViewModel? torrent)
    {
        if (torrent is null || torrent.IsBusy)
        {
            return;
        }

        await RunItemActionAsync(
            torrent, async () =>
            {
                await client.RemoveAsync(torrent.TorrentId, new RemoveTorrentRequest {DeleteData = false});
                ActionMessage = $"Removed '{torrent.Name}'.";
            }
        );
    }

    [RelayCommand]
    public async Task DeleteDataAsync(TorrentListItemViewModel? torrent)
    {
        if (torrent is null || torrent.IsBusy || !torrent.CanDeleteData)
        {
            return;
        }

        await RunItemActionAsync(
            torrent, async () =>
            {
                await client.RemoveAsync(torrent.TorrentId, new RemoveTorrentRequest {DeleteData = true});
                ActionMessage = $"Removed '{torrent.Name}' and deleted its data.";
            }
        );
    }

    [RelayCommand]
    public async Task RetryCompletionCallbackAsync(TorrentListItemViewModel? torrent)
    {
        if (torrent is null || torrent.IsBusy || !torrent.CanRetryCompletionCallback)
        {
            return;
        }

        await RunItemActionAsync(
            torrent, async () =>
            {
                var result = await client.RetryCompletionCallbackAsync(torrent.TorrentId);
                ActionMessage =
                        $"Queued completion callback retry for '{torrent.Name}' at {result.ProcessedAtUtc.ToLocalTime():g}.";
            }
        );
    }

    [RelayCommand]
    public void OpenDetail(TorrentListItemViewModel? torrent)
    {
        if (torrent is null)
        {
            return;
        }

        showTorrentDetail(torrent.TorrentId);
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading    = true;
        ErrorMessage = null;

        try
        {
            var selectedIds = _allTorrents.Where(item => item.IsSelected).Select(item => item.TorrentId).ToHashSet();
            var selectedAddCategoryKey = SelectedAddCategory?.Key;
            var selectedCategoryFilterKey = SelectedCategoryFilter?.Key;
            foreach (var item in _allTorrents)
            {
                item.PropertyChanged -= HandleTorrentPropertyChanged;
            }

            _allTorrents.Clear();
            var categoriesTask = client.GetCategoriesAsync();
            var torrentsTask   = client.GetTorrentsAsync();
            await Task.WhenAll(categoriesTask, torrentsTask);

            ApplyCategoryOptions(categoriesTask.Result, selectedAddCategoryKey, selectedCategoryFilterKey);
            var torrents = torrentsTask.Result;

            foreach (var torrent in torrents.OrderByDescending(item => item.AddedAtUtc))
            {
                var item = new TorrentListItemViewModel(
                    torrent, OpenDetail, PauseAsync, ResumeAsync, RefreshMetadataAsync,
                    ResetMetadataSessionAsync, RemoveAsync, DeleteDataAsync, RetryCompletionCallbackAsync,
                    FormatCategory(torrent.CategoryKey)
                )
                {
                    IsSelected = selectedIds.Contains(torrent.TorrentId),
                };
                item.PropertyChanged += HandleTorrentPropertyChanged;
                _allTorrents.Add(item);
            }

            RebuildVisibleTorrents();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to load torrents: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
            RaiseComputedState();
        }
    }

    private async Task RunItemActionAsync(TorrentListItemViewModel torrent, Func<Task> action)
    {
        IsBusy         = true;
        ErrorMessage   = null;
        ActionMessage  = null;
        torrent.IsBusy = true;

        try
        {
            await action();
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            torrent.IsBusy = false;
            IsBusy         = false;
            RaiseComputedState();
        }
    }

    private async Task RunBulkActionAsync(string label, Func<TorrentListItemViewModel, bool> predicate,
        Func<TorrentListItemViewModel, Task>     action)
    {
        var selected = _allTorrents.Where(item => item.IsSelected && predicate(item)).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        IsBusy        = true;
        ErrorMessage  = null;
        ActionMessage = null;

        var succeeded = 0;
        var failed    = 0;

        try
        {
            foreach (var torrent in selected)
            {
                torrent.IsBusy = true;
                try
                {
                    await action(torrent);
                    succeeded++;
                }
                catch
                {
                    failed++;
                }
                finally
                {
                    torrent.IsBusy = false;
                }
            }

            ActionMessage = $"{label}: {succeeded} succeeded, {failed} failed.";
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseComputedState();
        }
    }

    private void RebuildVisibleTorrents()
    {
        var filtered = _allTorrents
                      .Where(item => string.IsNullOrWhiteSpace(NameFilter) || item.Name.Contains(
                                   NameFilter, StringComparison.OrdinalIgnoreCase
                               )
                       )
                      .Where(item => string.Equals(SelectedStatus,  "All", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(item.State.ToString(), SelectedStatus, StringComparison.OrdinalIgnoreCase)
                       )
                      .Where(MatchesCategoryFilter)
                      .Where(item => string.Equals(SelectedCallbackState, "All", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(
                                   item.CompletionCallbackState, SelectedCallbackState,
                                   StringComparison.OrdinalIgnoreCase
                               )
                       )
                      .ToArray();

        filtered = SelectedSort switch
        {
            "Name" => filtered.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            "State" => filtered.OrderBy(item => item.State.ToString(), StringComparer.OrdinalIgnoreCase)
                               .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                               .ToArray(),
            "Progress (High to Low)" => filtered.OrderByDescending(item => item.ProgressPercent)
                                                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                                                .ToArray(),
            _ => filtered.OrderByDescending(item => item.AddedAtUtc).ToArray(),
        };

        VisibleTorrents.Clear();
        foreach (var torrent in filtered)
        {
            VisibleTorrents.Add(torrent);
        }

        RaiseComputedState();
    }

    private void HandleTorrentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TorrentListItemViewModel.IsSelected))
        {
            RaiseComputedState();
        }
    }

    private void RaiseComputedState()
    {
        OnPropertyChanged(nameof(SelectedTorrentCount));
        OnPropertyChanged(nameof(TotalTorrentCount));
        OnPropertyChanged(nameof(VisibleTorrentCount));
        OnPropertyChanged(nameof(HasVisibleTorrents));
        OnPropertyChanged(nameof(HasNoVisibleTorrents));
        OnPropertyChanged(nameof(CanRunBulkActions));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasActionMessage));
        OnPropertyChanged(nameof(HasSubmitMessage));
    }

    private void ApplyCategoryOptions(IReadOnlyList<TorrentCategoryDto> categories, string? selectedAddCategoryKey,
        string?                                                         selectedCategoryFilterKey)
    {
        _categoryDisplayNames.Clear();
        AddCategoryOptions.Clear();
        CategoryFilterOptions.Clear();

        AddCategoryOptions.Add(
            new TorrentCategoryOptionViewModel
            {
                Key         = string.Empty,
                DisplayName = "Uncategorized",
            }
        );

        CategoryFilterOptions.Add(
            new TorrentCategoryOptionViewModel
            {
                Key         = AllCategoryFilterKey,
                DisplayName = "All",
            }
        );
        CategoryFilterOptions.Add(
            new TorrentCategoryOptionViewModel
            {
                Key         = UncategorizedCategoryFilterKey,
                DisplayName = "Uncategorized",
            }
        );

        foreach (var category in categories.OrderBy(item => item.SortOrder)
                                           .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _categoryDisplayNames[category.Key] = category.DisplayName;

            if (category.Enabled)
            {
                AddCategoryOptions.Add(
                    new TorrentCategoryOptionViewModel
                    {
                        Key         = category.Key,
                        DisplayName = category.DisplayName,
                    }
                );
            }

            CategoryFilterOptions.Add(
                new TorrentCategoryOptionViewModel
                {
                    Key         = category.Key,
                    DisplayName = category.DisplayName,
                }
            );
        }

        SelectedAddCategory = ResolveAddCategoryByKey(selectedAddCategoryKey) ?? ResolveDefaultAddCategory();
        SelectedCategoryFilter = ResolveCategoryFilterByKey(selectedCategoryFilterKey) ??
                CategoryFilterOptions.FirstOrDefault();
    }

    private bool MatchesCategoryFilter(TorrentListItemViewModel item)
    {
        var selectedKey = SelectedCategoryFilter?.Key ?? AllCategoryFilterKey;
        if (string.Equals(selectedKey, AllCategoryFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(selectedKey, UncategorizedCategoryFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(item.CategoryKey);
        }

        return string.Equals(item.CategoryKey, selectedKey, StringComparison.OrdinalIgnoreCase);
    }

    private TorrentCategoryOptionViewModel ResolveDefaultAddCategory()
    {
        return AddCategoryOptions.FirstOrDefault(option => string.Equals(
                    option.Key, "TV", StringComparison.OrdinalIgnoreCase
                )
        ) ?? AddCategoryOptions.First();
    }

    private TorrentCategoryOptionViewModel? ResolveAddCategoryByKey(string? key)
    {
        if (key is null)
        {
            return null;
        }

        return AddCategoryOptions.FirstOrDefault(option => string.Equals(
                    option.Key, key, StringComparison.OrdinalIgnoreCase
                )
        );
    }

    private TorrentCategoryOptionViewModel? ResolveCategoryFilterByKey(string? key)
    {
        if (key is null)
        {
            return null;
        }

        return CategoryFilterOptions.FirstOrDefault(option => string.Equals(
                    option.Key, key, StringComparison.OrdinalIgnoreCase
                )
        );
    }

    private string FormatCategory(string? categoryKey)
    {
        if (string.IsNullOrWhiteSpace(categoryKey))
        {
            return "Uncategorized";
        }

        return _categoryDisplayNames.TryGetValue(categoryKey, out var displayName) ? displayName : categoryKey;
    }
}
