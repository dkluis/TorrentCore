#region

using CommunityToolkit.Mvvm.ComponentModel;

#endregion

namespace TorrentCore.Avalonia.ViewModels;

public partial class EditableTorrentCategoryViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _callbackLabel = string.Empty;
    [ObservableProperty]
    private string _displayName = string.Empty;
    [ObservableProperty]
    private string _downloadRootPath = string.Empty;
    [ObservableProperty]
    private bool _enabled;
    [ObservableProperty]
    private bool _invokeCompletionCallback;
    [ObservableProperty]
    private int _sortOrder;
    public EditableTorrentCategoryViewModel(string key) { Key = key; }
    public string Key { get; }
}
