using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Client;
using TorrentCore.Contracts.Host;

namespace TorrentCore.Avalonia.ViewModels;

public partial class DashboardViewModel(TorrentCoreClient client) : ViewModelBase
{
    [ObservableProperty]
    private EngineHostStatusDto? _hostStatus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasHostStatus => HostStatus is not null;

    public string CheckedAtLocalText => HostStatus?.CheckedAtUtc.ToLocalTime().ToString("g") ?? string.Empty;
    public string CurrentDownloadRateText => FormatRate(HostStatus?.CurrentDownloadRateBytesPerSecond ?? 0);
    public string CurrentUploadRateText => FormatRate(HostStatus?.CurrentUploadRateBytesPerSecond ?? 0);
    public string MaxDownloadRateText => FormatRateLimit(HostStatus?.EngineMaximumDownloadRateBytesPerSecond ?? 0);
    public string MaxUploadRateText => FormatRateLimit(HostStatus?.EngineMaximumUploadRateBytesPerSecond ?? 0);
    public string RecoveryCompletedText => HostStatus?.StartupRecoveryCompletedAtUtc?.ToLocalTime().ToString("g") ?? "Not yet completed";

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync();

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
            HostStatus = await client.GetHostStatusAsync();
            OnPropertyChanged(nameof(HasHostStatus));
            OnPropertyChanged(nameof(CheckedAtLocalText));
            OnPropertyChanged(nameof(CurrentDownloadRateText));
            OnPropertyChanged(nameof(CurrentUploadRateText));
            OnPropertyChanged(nameof(MaxDownloadRateText));
            OnPropertyChanged(nameof(MaxUploadRateText));
            OnPropertyChanged(nameof(RecoveryCompletedText));
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to load host status: {exception.Message}";
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasError));
        }
    }

    private static string FormatRate(long bytesPerSecond) =>
        $"{bytesPerSecond / 1_000_000.0:0.00} MB/s";

    private static string FormatRateLimit(int bytesPerSecond) =>
        bytesPerSecond <= 0 ? "Unlimited" : FormatRate(bytesPerSecond);
}
