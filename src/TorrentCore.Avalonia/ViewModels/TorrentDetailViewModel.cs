using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Client;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Avalonia.ViewModels;

public partial class TorrentDetailViewModel(TorrentCoreClient client, Guid torrentId, Action goBack) : ViewModelBase
{
    [ObservableProperty]
    private TorrentDetailDto? _torrent;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _actionMessage;

    public ObservableCollection<ActivityLogEntryDto> Logs { get; } = [];

    public bool HasTorrent => Torrent is not null;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

    public string WaitText => FormatWaitReason(Torrent?.WaitReason, Torrent?.QueuePosition);
    public string DownloadRateText => FormatRate(Torrent?.DownloadRateBytesPerSecond ?? 0);
    public string UploadRateText => FormatRate(Torrent?.UploadRateBytesPerSecond ?? 0);
    public string DownloadedText => FormatBytes(Torrent?.DownloadedBytes ?? 0);
    public string TotalSizeText => Torrent?.TotalBytes is null ? "Pending metadata" : FormatBytes(Torrent.TotalBytes.Value);
    public string AddedAtLocalText => Torrent?.AddedAtUtc.ToLocalTime().ToString("g") ?? string.Empty;
    public string CompletedAtLocalText => Torrent?.CompletedAtUtc?.ToLocalTime().ToString("g") ?? "Not completed";
    public string LastActivityAtLocalText => Torrent?.LastActivityAtUtc?.ToLocalTime().ToString("g") ?? "No recent activity";

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    public void Back() => goBack();

    [RelayCommand]
    public async Task PauseAsync()
    {
        if (Torrent is null)
        {
            return;
        }

        await RunActionAsync(async () =>
        {
            var result = await client.PauseAsync(Torrent.TorrentId);
            ActionMessage = $"Paused at {result.ProcessedAtUtc.ToLocalTime():g}.";
        });
    }

    [RelayCommand]
    public async Task ResumeAsync()
    {
        if (Torrent is null)
        {
            return;
        }

        await RunActionAsync(async () =>
        {
            var result = await client.ResumeAsync(Torrent.TorrentId);
            ActionMessage = $"Resumed at {result.ProcessedAtUtc.ToLocalTime():g}.";
        });
    }

    [RelayCommand]
    public async Task RemoveAsync()
    {
        if (Torrent is null)
        {
            return;
        }

        await RunActionAsync(
            async () =>
            {
                await client.RemoveAsync(Torrent.TorrentId, new RemoveTorrentRequest { DeleteData = false });
                goBack();
            },
            reloadAfter: false);
    }

    [RelayCommand]
    public async Task DeleteDataAsync()
    {
        if (Torrent is null || !CanDeleteData)
        {
            return;
        }

        await RunActionAsync(
            async () =>
            {
                await client.RemoveAsync(Torrent.TorrentId, new RemoveTorrentRequest { DeleteData = true });
                goBack();
            },
            reloadAfter: false);
    }

    public bool CanDeleteData =>
        Torrent is not null && Torrent.State is not TorrentState.Seeding and not TorrentState.Completed;

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
            Torrent = await client.GetTorrentAsync(torrentId);
            Logs.Clear();

            if (Torrent is null)
            {
                ErrorMessage = $"Torrent '{torrentId}' was not found.";
                return;
            }

            var entries = await client.GetRecentLogsAsync(take: 20, torrentId: torrentId);
            foreach (var entry in entries)
            {
                Logs.Add(entry);
            }

            RaiseComputedState();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to load torrent detail: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
            RaiseComputedState();
        }
    }

    private async Task RunActionAsync(Func<Task> action, bool reloadAfter = true)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        ActionMessage = null;

        try
        {
            await action();
            if (reloadAfter)
            {
                await LoadAsync();
            }
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

    private void RaiseComputedState()
    {
        OnPropertyChanged(nameof(HasTorrent));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasActionMessage));
        OnPropertyChanged(nameof(WaitText));
        OnPropertyChanged(nameof(DownloadRateText));
        OnPropertyChanged(nameof(UploadRateText));
        OnPropertyChanged(nameof(DownloadedText));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(AddedAtLocalText));
        OnPropertyChanged(nameof(CompletedAtLocalText));
        OnPropertyChanged(nameof(LastActivityAtLocalText));
        OnPropertyChanged(nameof(CanDeleteData));
    }

    private static string FormatWaitReason(TorrentWaitReason? waitReason, int? queuePosition)
    {
        var label = waitReason switch
        {
            TorrentWaitReason.PendingMetadataDispatch => "Pending metadata dispatch",
            TorrentWaitReason.WaitingForMetadataSlot => "Waiting for metadata slot",
            TorrentWaitReason.PendingDownloadDispatch => "Pending download dispatch",
            TorrentWaitReason.WaitingForDownloadSlot => "Waiting for download slot",
            TorrentWaitReason.PausedByOperator => "Paused by operator",
            TorrentWaitReason.BlockedByError => "Blocked by error",
            _ => string.Empty,
        };

        return queuePosition is null || string.IsNullOrWhiteSpace(label)
            ? label
            : $"{label} (#{queuePosition.Value})";
    }

    private static string FormatRate(long bytesPerSecond) =>
        $"{bytesPerSecond / 1_000_000.0:0.00} MB/s";

    private static string FormatBytes(long bytes) =>
        $"{bytes / 1_000_000_000.0:0.00} GB";
}
