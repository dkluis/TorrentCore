using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Avalonia.ViewModels;

public partial class TorrentListItemViewModel : ViewModelBase
{
    public TorrentListItemViewModel(
        TorrentSummaryDto dto,
        Action<TorrentListItemViewModel> openDetail,
        Func<TorrentListItemViewModel, Task> pause,
        Func<TorrentListItemViewModel, Task> resume,
        Func<TorrentListItemViewModel, Task> remove,
        Func<TorrentListItemViewModel, Task> deleteData)
    {
        TorrentId = dto.TorrentId;
        OpenDetailCommand = new RelayCommand(() => openDetail(this));
        PauseCommand = new AsyncRelayCommand(() => pause(this));
        ResumeCommand = new AsyncRelayCommand(() => resume(this));
        RemoveCommand = new AsyncRelayCommand(() => remove(this));
        DeleteDataCommand = new AsyncRelayCommand(() => deleteData(this));
        Apply(dto);
    }

    public Guid TorrentId { get; }
    public IRelayCommand OpenDetailCommand { get; }
    public IAsyncRelayCommand PauseCommand { get; }
    public IAsyncRelayCommand ResumeCommand { get; }
    public IAsyncRelayCommand RemoveCommand { get; }
    public IAsyncRelayCommand DeleteDataCommand { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private TorrentState _state;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private long _downloadRateBytesPerSecond;

    [ObservableProperty]
    private long _uploadRateBytesPerSecond;

    [ObservableProperty]
    private int _trackerCount;

    [ObservableProperty]
    private int _connectedPeerCount;

    [ObservableProperty]
    private TorrentWaitReason? _waitReason;

    [ObservableProperty]
    private int? _queuePosition;

    [ObservableProperty]
    private DateTimeOffset _addedAtUtc;

    [ObservableProperty]
    private DateTimeOffset? _completedAtUtc;

    [ObservableProperty]
    private DateTimeOffset? _lastActivityAtUtc;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _canPause;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private bool _canRemove;

    [ObservableProperty]
    private bool _canDeleteData;

    [ObservableProperty]
    private bool _isBusy;

    public string ProgressText => $"{ProgressPercent:0.0}%";
    public string DownloadRateText => $"{DownloadRateBytesPerSecond / 1_000_000.0:0.00} MB/s";
    public string UploadRateText => $"{UploadRateBytesPerSecond / 1_000_000.0:0.00} MB/s";

    public string WaitReasonText => WaitReason switch
    {
        TorrentWaitReason.PendingMetadataDispatch => "Pending metadata dispatch",
        TorrentWaitReason.WaitingForMetadataSlot => "Waiting for metadata slot",
        TorrentWaitReason.PendingDownloadDispatch => "Pending download dispatch",
        TorrentWaitReason.WaitingForDownloadSlot => "Waiting for download slot",
        TorrentWaitReason.PausedByOperator => "Paused by operator",
        TorrentWaitReason.BlockedByError => "Blocked by error",
        _ => string.Empty,
    };

    public string WaitAndQueueText =>
        QueuePosition is null || string.IsNullOrWhiteSpace(WaitReasonText)
            ? WaitReasonText
            : $"{WaitReasonText} (#{QueuePosition.Value})";

    public void Apply(TorrentSummaryDto dto)
    {
        Name = dto.Name;
        State = dto.State;
        ProgressPercent = dto.ProgressPercent;
        DownloadRateBytesPerSecond = dto.DownloadRateBytesPerSecond;
        UploadRateBytesPerSecond = dto.UploadRateBytesPerSecond;
        TrackerCount = dto.TrackerCount;
        ConnectedPeerCount = dto.ConnectedPeerCount;
        WaitReason = dto.WaitReason;
        QueuePosition = dto.QueuePosition;
        AddedAtUtc = dto.AddedAtUtc;
        CompletedAtUtc = dto.CompletedAtUtc;
        LastActivityAtUtc = dto.LastActivityAtUtc;
        ErrorMessage = dto.ErrorMessage;
        CanPause = dto.CanPause;
        CanResume = dto.CanResume;
        CanRemove = dto.CanRemove;
        CanDeleteData = dto.State is not TorrentState.Seeding and not TorrentState.Completed;

        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(DownloadRateText));
        OnPropertyChanged(nameof(UploadRateText));
        OnPropertyChanged(nameof(WaitReasonText));
        OnPropertyChanged(nameof(WaitAndQueueText));
    }
}
