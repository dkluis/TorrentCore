#region

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Client;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Torrents;

#endregion

namespace TorrentCore.Avalonia.ViewModels;

public partial class TorrentDetailViewModel(TorrentCoreClient client, Guid torrentId, Action goBack) : ViewModelBase
{
    [ObservableProperty]
    private string? _actionMessage;
    [ObservableProperty]
    private string? _errorMessage;
    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private bool _isLoading;
    private CallbackLogSummary? _latestCallbackLog;
    [ObservableProperty]
    private TorrentDetailDto? _torrent;
    public ObservableCollection<ActivityLogEntryItemViewModel> Logs { get; } = [];
    public bool HasTorrent => Torrent is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);
    public bool CanRefreshMetadata => Torrent?.CanRefreshMetadata ?? false;
    public bool CanRetryCompletionCallback => Torrent?.CanRetryCompletionCallback ?? false;
    public string CategoryText { get; private set; } = "Uncategorized";
    public string WaitText => FormatWaitReason(Torrent?.WaitReason, Torrent?.QueuePosition);
    public string DownloadRateText => FormatRate(Torrent?.DownloadRateBytesPerSecond ?? 0);
    public string UploadRateText => FormatRate(Torrent?.UploadRateBytesPerSecond ?? 0);
    public string DownloadedText => FormatBytes(Torrent?.DownloadedBytes ?? 0);
    public string TotalSizeText
        => Torrent?.TotalBytes is null ? "Pending metadata" : FormatBytes(Torrent.TotalBytes.Value);
    public string MetadataResolvedText => Torrent?.TotalBytes is null ? "No" : "Yes";
    public string ErrorText => string.IsNullOrWhiteSpace(Torrent?.ErrorMessage) ? "None" : Torrent!.ErrorMessage!;
    public string InfoHashText
        => string.IsNullOrWhiteSpace(Torrent?.InfoHash) ? "Pending metadata" : Torrent!.InfoHash!;
    public string MagnetUriText        => Torrent?.MagnetUri                                   ?? string.Empty;
    public string AddedAtLocalText     => Torrent?.AddedAtUtc.ToLocalTime().ToString("g")      ?? string.Empty;
    public string CompletedAtLocalText => Torrent?.CompletedAtUtc?.ToLocalTime().ToString("g") ?? "Not completed";
    public string LastActivityAtLocalText
        => Torrent?.LastActivityAtUtc?.ToLocalTime().ToString("g") ?? "No recent activity";
    public string CompletionCallbackStateText
        => FormatCompletionCallbackState(Torrent?.CompletionCallbackState, Torrent?.CompletedAtUtc);
    public string CompletionCallbackPendingSinceText => FormatLocalTime(Torrent?.CompletionCallbackPendingSinceUtc);
    public string CompletionCallbackInvokedAtText    => FormatLocalTime(Torrent?.CompletionCallbackInvokedAtUtc);
    public string CompletionCallbackFinalPayloadPathText
        => Torrent?.CompletionCallbackFinalPayloadPath ?? "Not available";
    public string CompletionCallbackPendingReasonText
        => string.IsNullOrWhiteSpace(Torrent?.CompletionCallbackPendingReason) ? "None" :
                Torrent!.CompletionCallbackPendingReason!;
    public string CompletionCallbackLastErrorText
        => string.IsNullOrWhiteSpace(Torrent?.CompletionCallbackLastError) ? "None" :
                Torrent!.CompletionCallbackLastError!;
    public string LatestCallbackEventText            => FormatCallbackEvent(_latestCallbackLog);
    public string LatestCallbackEventTimeText        => FormatCallbackEventTime(_latestCallbackLog);
    public string LatestCallbackMessageText          => _latestCallbackLog?.Message ?? "Not yet";
    public string LatestCallbackProcessIdText        => FormatNullableInt(_latestCallbackLog?.ProcessId);
    public string LatestCallbackExitCodeText         => FormatNullableInt(_latestCallbackLog?.ExitCode);
    public string LatestCallbackCommandPathText      => _latestCallbackLog?.CommandPath      ?? "Not yet";
    public string LatestCallbackWorkingDirectoryText => _latestCallbackLog?.WorkingDirectory ?? "Not yet";
    public string LatestCallbackProcessTimeoutText   => FormatTimeoutSeconds(_latestCallbackLog?.ProcessTimeoutSeconds);
    public string LatestCallbackFinalizationWaitText
        => FormatTimeoutSeconds(_latestCallbackLog?.FinalizationTimeoutSeconds);
    public bool CanDeleteData
        => Torrent is not null && Torrent.State is not TorrentState.Seeding and not TorrentState.Completed;

    [RelayCommand]
    public async Task RefreshAsync() { await LoadAsync(); }

    [RelayCommand]
    public void Back() { goBack(); }

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
            }
        );
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
            }
        );
    }

    [RelayCommand]
    public async Task RefreshMetadataAsync()
    {
        if (Torrent is null || !CanRefreshMetadata)
        {
            return;
        }

        await RunActionAsync(async () =>
            {
                var result = await client.RefreshMetadataAsync(Torrent.TorrentId);
                ActionMessage = $"Requested metadata refresh at {result.ProcessedAtUtc.ToLocalTime():g}.";
            }
        );
    }

    [RelayCommand]
    public async Task ResetMetadataSessionAsync()
    {
        if (Torrent is null || !CanRefreshMetadata)
        {
            return;
        }

        await RunActionAsync(async () =>
            {
                var result = await client.ResetMetadataSessionAsync(Torrent.TorrentId);
                ActionMessage = $"Recreated metadata session at {result.ProcessedAtUtc.ToLocalTime():g}.";
            }
        );
    }

    [RelayCommand]
    public async Task RetryCompletionCallbackAsync()
    {
        if (Torrent is null || !CanRetryCompletionCallback)
        {
            return;
        }

        await RunActionAsync(async () =>
            {
                var result = await client.RetryCompletionCallbackAsync(Torrent.TorrentId);
                ActionMessage = $"Queued completion callback retry at {result.ProcessedAtUtc.ToLocalTime():g}.";
            }
        );
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
                await client.RemoveAsync(Torrent.TorrentId, new RemoveTorrentRequest {DeleteData = false});
                goBack();
            }, false
        );
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
                await client.RemoveAsync(Torrent.TorrentId, new RemoveTorrentRequest {DeleteData = true});
                goBack();
            }, false
        );
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
            var torrentTask    = client.GetTorrentAsync(torrentId);
            var categoriesTask = client.GetCategoriesAsync();
            await Task.WhenAll(torrentTask, categoriesTask);

            Torrent = torrentTask.Result;
            Logs.Clear();

            if (Torrent is null)
            {
                ErrorMessage       = $"Torrent '{torrentId}' was not found.";
                CategoryText       = "Uncategorized";
                _latestCallbackLog = null;
                return;
            }

            CategoryText = FormatCategory(Torrent.CategoryKey, categoriesTask.Result);
            var entries = await client.GetRecentLogsAsync(20, torrentId: torrentId);
            foreach (var entry in entries)
            {
                Logs.Add(new ActivityLogEntryItemViewModel(entry));
            }

            _latestCallbackLog = GetLatestCallbackLog(entries);

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

        IsBusy        = true;
        ErrorMessage  = null;
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
        OnPropertyChanged(nameof(MetadataResolvedText));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(InfoHashText));
        OnPropertyChanged(nameof(MagnetUriText));
        OnPropertyChanged(nameof(AddedAtLocalText));
        OnPropertyChanged(nameof(CompletedAtLocalText));
        OnPropertyChanged(nameof(LastActivityAtLocalText));
        OnPropertyChanged(nameof(CategoryText));
        OnPropertyChanged(nameof(CompletionCallbackStateText));
        OnPropertyChanged(nameof(CompletionCallbackPendingSinceText));
        OnPropertyChanged(nameof(CompletionCallbackInvokedAtText));
        OnPropertyChanged(nameof(CompletionCallbackFinalPayloadPathText));
        OnPropertyChanged(nameof(CompletionCallbackPendingReasonText));
        OnPropertyChanged(nameof(CompletionCallbackLastErrorText));
        OnPropertyChanged(nameof(LatestCallbackEventText));
        OnPropertyChanged(nameof(LatestCallbackEventTimeText));
        OnPropertyChanged(nameof(LatestCallbackMessageText));
        OnPropertyChanged(nameof(LatestCallbackProcessIdText));
        OnPropertyChanged(nameof(LatestCallbackExitCodeText));
        OnPropertyChanged(nameof(LatestCallbackCommandPathText));
        OnPropertyChanged(nameof(LatestCallbackWorkingDirectoryText));
        OnPropertyChanged(nameof(LatestCallbackProcessTimeoutText));
        OnPropertyChanged(nameof(LatestCallbackFinalizationWaitText));
        OnPropertyChanged(nameof(CanRefreshMetadata));
        OnPropertyChanged(nameof(CanRetryCompletionCallback));
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
            _ => "Not waiting",
        };

        return queuePosition is null || waitReason is null ? label : $"{label} (#{queuePosition.Value})";
    }

    private static string FormatRate(long  bytesPerSecond) { return $"{bytesPerSecond / 1_000_000.0:0.00} MB/s"; }
    private static string FormatBytes(long bytes)          { return $"{bytes          / 1_000_000_000.0:0.00} GB"; }

    private static string FormatCompletionCallbackState(string? state, DateTimeOffset? completedAtUtc)
    {
        return !string.IsNullOrWhiteSpace(state) ? state : completedAtUtc is null ?
                "Waiting for completion" : "Not requested";
    }

    private static string FormatLocalTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Not yet";
    }

    private static string FormatCallbackEvent(CallbackLogSummary? callbackLog)
    {
        return callbackLog is null ? "Not yet" : FormatCallbackEventName(callbackLog.EventType);
    }

    private static string FormatCallbackEventTime(CallbackLogSummary? callbackLog)
    {
        return callbackLog is null ? "Not yet" :
                callbackLog.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    private static string FormatNullableInt(int? value)
    {
        return value?.ToString(CultureInfo.CurrentCulture) ?? "Not available";
    }

    private static string FormatTimeoutSeconds(int? value)
    {
        return value is null ? "Not available" : $"{value.Value.ToString(CultureInfo.CurrentCulture)} seconds";
    }

    private static string FormatCallbackEventName(string eventType)
    {
        const string prefix = "torrent.callback.";
        var normalized = eventType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? eventType[prefix.Length..] :
                eventType;
        var parts = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? eventType : string.Join(
            ' ', parts.Select(static part => char.ToUpperInvariant(part[0]) + part[1..])
        );
    }

    private static string FormatCategory(string?               categoryKey,
        IReadOnlyList<Contracts.Categories.TorrentCategoryDto> categories)
    {
        if (string.IsNullOrWhiteSpace(categoryKey))
        {
            return "Uncategorized";
        }

        var category = categories.FirstOrDefault(item => string.Equals(
                    item.Key, categoryKey, StringComparison.OrdinalIgnoreCase
                )
        );
        return category?.DisplayName ?? categoryKey;
    }

    private static CallbackLogSummary? GetLatestCallbackLog(IReadOnlyList<ActivityLogEntryDto> logs)
    {
        var callbackLog = logs
                         .Where(log => log.EventType.StartsWith("torrent.callback.", StringComparison.OrdinalIgnoreCase)
                          )
                         .OrderByDescending(log => log.OccurredAtUtc)
                         .FirstOrDefault();

        if (callbackLog is null)
        {
            return null;
        }

        string? commandPath                = null;
        string? workingDirectory           = null;
        int?    processId                  = null;
        int?    exitCode                   = null;
        int?    processTimeoutSeconds      = null;
        int?    finalizationTimeoutSeconds = null;

        if (!string.IsNullOrWhiteSpace(callbackLog.DetailsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(callbackLog.DetailsJson);
                var       root     = document.RootElement;
                commandPath                = GetOptionalString(root, "CommandPath");
                workingDirectory           = GetOptionalString(root, "WorkingDirectory");
                processId                  = GetOptionalInt32(root, "ProcessId");
                exitCode                   = GetOptionalInt32(root, "ExitCode");
                processTimeoutSeconds      = GetOptionalInt32(root, "CompletionCallbackTimeoutSeconds");
                finalizationTimeoutSeconds = GetOptionalInt32(root, "CompletionCallbackFinalizationTimeoutSeconds");
            }
            catch (JsonException) { }
        }

        return new CallbackLogSummary(
            callbackLog.EventType, callbackLog.OccurredAtUtc, callbackLog.Message, commandPath, workingDirectory,
            processId, exitCode, processTimeoutSeconds, finalizationTimeoutSeconds
        );
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ?
                value.GetString() : null;
    }

    private static int? GetOptionalInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out var number) ? number : null;
    }

    private sealed record CallbackLogSummary(string EventType, DateTimeOffset OccurredAtUtc, string Message,
        string? CommandPath, string? WorkingDirectory, int? ProcessId, int? ExitCode, int? ProcessTimeoutSeconds,
        int? FinalizationTimeoutSeconds);
}
