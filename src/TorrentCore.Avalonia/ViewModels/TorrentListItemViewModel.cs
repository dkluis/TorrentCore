#region

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Contracts.Torrents;

#endregion

namespace TorrentCore.Avalonia.ViewModels;

public partial class TorrentListItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private DateTimeOffset _addedAtUtc;
    [ObservableProperty]
    private bool _canDeleteData;
    [ObservableProperty]
    private bool _canPause;
    [ObservableProperty]
    private bool _canRefreshMetadata;
    [ObservableProperty]
    private bool _canRemove;
    [ObservableProperty]
    private bool _canResume;
    [ObservableProperty]
    private bool _canRetryCompletionCallback;
    [ObservableProperty]
    private string? _categoryKey;
    [ObservableProperty]
    private string _categoryText = "Uncategorized";
    [ObservableProperty]
    private DateTimeOffset? _completedAtUtc;
    [ObservableProperty]
    private string? _completionCallbackLastError;
    [ObservableProperty]
    private string? _completionCallbackState;
    [ObservableProperty]
    private int _connectedPeerCount;
    [ObservableProperty]
    private long _downloadRateBytesPerSecond;
    [ObservableProperty]
    private string? _errorMessage;
    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private bool _isSelected;
    [ObservableProperty]
    private DateTimeOffset? _lastActivityAtUtc;
    [ObservableProperty]
    private string _name = string.Empty;
    [ObservableProperty]
    private double _progressPercent;
    [ObservableProperty]
    private int? _queuePosition;
    [ObservableProperty]
    private TorrentState _state;
    [ObservableProperty]
    private int _trackerCount;
    [ObservableProperty]
    private long _uploadRateBytesPerSecond;
    [ObservableProperty]
    private TorrentWaitReason? _waitReason;

    public TorrentListItemViewModel(TorrentSummaryDto dto, Action<TorrentListItemViewModel> openDetail,
        Func<TorrentListItemViewModel, Task> pause, Func<TorrentListItemViewModel, Task> resume,
        Func<TorrentListItemViewModel, Task> refreshMetadata, Func<TorrentListItemViewModel, Task> resetMetadataSession,
        Func<TorrentListItemViewModel, Task> remove, Func<TorrentListItemViewModel, Task> deleteData,
        Func<TorrentListItemViewModel, Task> retryCompletionCallback, string categoryText)
    {
        TorrentId                      = dto.TorrentId;
        OpenDetailCommand              = new RelayCommand(() => openDetail(this));
        PauseCommand                   = new AsyncRelayCommand(() => pause(this));
        ResumeCommand                  = new AsyncRelayCommand(() => resume(this));
        RefreshMetadataCommand         = new AsyncRelayCommand(() => refreshMetadata(this));
        ResetMetadataSessionCommand    = new AsyncRelayCommand(() => resetMetadataSession(this));
        RemoveCommand                  = new AsyncRelayCommand(() => remove(this));
        DeleteDataCommand              = new AsyncRelayCommand(() => deleteData(this));
        RetryCompletionCallbackCommand = new AsyncRelayCommand(() => retryCompletionCallback(this));
        Apply(dto, categoryText);
    }

    public Guid TorrentId { get; }
    public IRelayCommand OpenDetailCommand { get; }
    public IAsyncRelayCommand PauseCommand { get; }
    public IAsyncRelayCommand ResumeCommand { get; }
    public IAsyncRelayCommand RefreshMetadataCommand { get; }
    public IAsyncRelayCommand ResetMetadataSessionCommand { get; }
    public IAsyncRelayCommand RemoveCommand { get; }
    public IAsyncRelayCommand DeleteDataCommand { get; }
    public IAsyncRelayCommand RetryCompletionCallbackCommand { get; }
    public string ProgressText => $"{ProgressPercent:0.0}%";
    public string DownloadRateText => $"{DownloadRateBytesPerSecond / 1_000_000.0:0.00} MB/s";
    public string UploadRateText => $"{UploadRateBytesPerSecond / 1_000_000.0:0.00} MB/s";
    public string AddedAtLocalText => AddedAtUtc.ToLocalTime().ToString("g");
    public string CompletedAtLocalText => CompletedAtUtc?.ToLocalTime().ToString("g") ?? "Not completed";
    public string LastActivityAtLocalText => LastActivityAtUtc?.ToLocalTime().ToString("g") ?? "No recent activity";
    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasCompletionCallbackLastError => !string.IsNullOrWhiteSpace(CompletionCallbackLastError);
    public string WaitReasonText
        => WaitReason switch
        {
            TorrentWaitReason.PendingMetadataDispatch => "Pending metadata dispatch",
            TorrentWaitReason.WaitingForMetadataSlot => "Waiting for metadata slot",
            TorrentWaitReason.PendingDownloadDispatch => "Pending download dispatch",
            TorrentWaitReason.WaitingForDownloadSlot => "Waiting for download slot",
            TorrentWaitReason.PausedByOperator => "Paused by operator",
            TorrentWaitReason.BlockedByError => "Blocked by error",
            _ => string.Empty,
        };
    public string WaitAndQueueText
        => QueuePosition is null || string.IsNullOrWhiteSpace(WaitReasonText) ? WaitReasonText :
                $"{WaitReasonText} (#{QueuePosition.Value})";
    public string CompletionCallbackStateText
        => !string.IsNullOrWhiteSpace(CompletionCallbackState) ? CompletionCallbackState! : CompletedAtUtc is null ?
                "Waiting for completion" : "Not requested";

    public void Apply(TorrentSummaryDto dto, string categoryText)
    {
        Name                        = dto.Name;
        CategoryKey                 = dto.CategoryKey;
        CategoryText                = categoryText;
        State                       = dto.State;
        ProgressPercent             = dto.ProgressPercent;
        DownloadRateBytesPerSecond  = dto.DownloadRateBytesPerSecond;
        UploadRateBytesPerSecond    = dto.UploadRateBytesPerSecond;
        TrackerCount                = dto.TrackerCount;
        ConnectedPeerCount          = dto.ConnectedPeerCount;
        WaitReason                  = dto.WaitReason;
        QueuePosition               = dto.QueuePosition;
        AddedAtUtc                  = dto.AddedAtUtc;
        CompletedAtUtc              = dto.CompletedAtUtc;
        LastActivityAtUtc           = dto.LastActivityAtUtc;
        CompletionCallbackState     = dto.CompletionCallbackState;
        CompletionCallbackLastError = dto.CompletionCallbackLastError;
        ErrorMessage                = dto.ErrorMessage;
        CanRefreshMetadata          = dto.CanRefreshMetadata;
        CanRetryCompletionCallback  = dto.CanRetryCompletionCallback;
        CanPause                    = dto.CanPause;
        CanResume                   = dto.CanResume;
        CanRemove                   = dto.CanRemove;
        CanDeleteData               = dto.State is not TorrentState.Seeding and not TorrentState.Completed;

        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(DownloadRateText));
        OnPropertyChanged(nameof(UploadRateText));
        OnPropertyChanged(nameof(WaitReasonText));
        OnPropertyChanged(nameof(WaitAndQueueText));
        OnPropertyChanged(nameof(AddedAtLocalText));
        OnPropertyChanged(nameof(CompletedAtLocalText));
        OnPropertyChanged(nameof(LastActivityAtLocalText));
        OnPropertyChanged(nameof(CompletionCallbackStateText));
        OnPropertyChanged(nameof(HasErrorMessage));
        OnPropertyChanged(nameof(HasCompletionCallbackLastError));
    }
}
