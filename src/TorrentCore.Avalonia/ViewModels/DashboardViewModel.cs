#region

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Client;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

#endregion

namespace TorrentCore.Avalonia.ViewModels;

public partial class DashboardViewModel(TorrentCoreClient client) : ViewModelBase
{
    [ObservableProperty]
    private bool _autoRefresh = true;
    [ObservableProperty]
    private string? _errorMessage;
    [ObservableProperty]
    private EngineHostStatusDto? _hostStatus;
    [ObservableProperty]
    private bool _isLoading;
    [ObservableProperty]
    private string _lastRefreshedText = string.Empty;
    private IReadOnlyList<TorrentSummaryDto> _torrents = Array.Empty<TorrentSummaryDto>();
    public  bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public  bool HasHostStatus => HostStatus is not null;
    public  bool HasLastRefreshed => !string.IsNullOrWhiteSpace(LastRefreshedText);
    public  string CheckedAtLocalText => HostStatus?.CheckedAtUtc.ToLocalTime().ToString("g") ?? string.Empty;
    public  string CurrentDownloadRateText => FormatRate(HostStatus?.CurrentDownloadRateBytesPerSecond ?? 0);
    public  string CurrentUploadRateText => FormatRate(HostStatus?.CurrentUploadRateBytesPerSecond ?? 0);
    public  string MaxDownloadRateText => FormatRateLimit(HostStatus?.EngineMaximumDownloadRateBytesPerSecond ?? 0);
    public  string MaxUploadRateText => FormatRateLimit(HostStatus?.EngineMaximumUploadRateBytesPerSecond ?? 0);
    public string RecoveryCompletedText
        => HostStatus?.StartupRecoveryCompletedAtUtc?.ToLocalTime().ToString("g") ?? "Not yet completed";
    public int CallbackPendingCount   => CountCallbackState("PendingFinalization");
    public int CallbackInvokedCount   => CountCallbackState("Invoked");
    public int CallbackFailedCount    => CountCallbackState("Failed");
    public int CallbackTimedOutCount  => CountCallbackState("TimedOut");
    public int CallbackRetryableCount => CallbackFailedCount + CallbackTimedOutCount;
    public string SupportsLiveControlText
        => HostStatus is null ? string.Empty :
                $"Magnet Adds={HostStatus.SupportsMagnetAdds}, Pause={HostStatus.SupportsPause}, Resume={HostStatus.SupportsResume}, Remove={HostStatus.SupportsRemove}";
    public string HostModelText
        => HostStatus is null ? string.Empty :
                $"Persistent Storage={HostStatus.SupportsPersistentStorage}, Multi Host={HostStatus.SupportsMultiHost}";

    [RelayCommand]
    public async Task RefreshAsync() { await LoadAsync(); }

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
            var hostStatusTask = client.GetHostStatusAsync();
            var torrentsTask   = client.GetTorrentsAsync();
            await Task.WhenAll(hostStatusTask, torrentsTask);

            HostStatus        = hostStatusTask.Result;
            _torrents         = torrentsTask.Result;
            LastRefreshedText = DateTimeOffset.Now.ToString("g");
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to load host status: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
            RaiseComputedState();
        }
    }

    private static string FormatRate(long bytesPerSecond) { return $"{bytesPerSecond / 1_000_000.0:0.00} MB/s"; }

    private static string FormatRateLimit(int bytesPerSecond)
    {
        return bytesPerSecond <= 0 ? "Unlimited" : FormatRate(bytesPerSecond);
    }

    private int CountCallbackState(string state)
    {
        return _torrents.Count(torrent => string.Equals(
                    torrent.CompletionCallbackState, state, StringComparison.OrdinalIgnoreCase
                )
        );
    }

    private void RaiseComputedState()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasHostStatus));
        OnPropertyChanged(nameof(HasLastRefreshed));
        OnPropertyChanged(nameof(CheckedAtLocalText));
        OnPropertyChanged(nameof(CurrentDownloadRateText));
        OnPropertyChanged(nameof(CurrentUploadRateText));
        OnPropertyChanged(nameof(MaxDownloadRateText));
        OnPropertyChanged(nameof(MaxUploadRateText));
        OnPropertyChanged(nameof(RecoveryCompletedText));
        OnPropertyChanged(nameof(CallbackPendingCount));
        OnPropertyChanged(nameof(CallbackInvokedCount));
        OnPropertyChanged(nameof(CallbackFailedCount));
        OnPropertyChanged(nameof(CallbackTimedOutCount));
        OnPropertyChanged(nameof(CallbackRetryableCount));
        OnPropertyChanged(nameof(SupportsLiveControlText));
        OnPropertyChanged(nameof(HostModelText));
    }
}
