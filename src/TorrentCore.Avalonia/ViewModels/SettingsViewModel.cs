using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Client;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Host;

namespace TorrentCore.Avalonia.ViewModels;

public partial class SettingsViewModel(TorrentCoreClient client) : ViewModelBase
{
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _engineRuntime = string.Empty;

    [ObservableProperty]
    private string _partialFilesSummary = string.Empty;

    [ObservableProperty]
    private string _persistedOverridesSummary = string.Empty;

    [ObservableProperty]
    private string _updatedAtSummary = string.Empty;

    [ObservableProperty]
    private string _retrievedAtSummary = string.Empty;

    [ObservableProperty]
    private bool _supportsLiveUpdates;

    [ObservableProperty]
    private string _seedingStopMode = "Unlimited";

    [ObservableProperty]
    private double _seedingStopRatio = 1.0;

    [ObservableProperty]
    private int _seedingStopMinutes = 60;

    [ObservableProperty]
    private string _completedTorrentCleanupMode = "Never";

    [ObservableProperty]
    private int _completedTorrentCleanupMinutes = 60;

    [ObservableProperty]
    private int _engineConnectionFailureLogBurstLimit = 5;

    [ObservableProperty]
    private int _engineConnectionFailureLogWindowSeconds = 60;

    [ObservableProperty]
    private int _engineMaximumConnections = 150;

    [ObservableProperty]
    private int _engineMaximumHalfOpenConnections = 8;

    [ObservableProperty]
    private int _engineMaximumDownloadRateBytesPerSecond;

    [ObservableProperty]
    private int _engineMaximumUploadRateBytesPerSecond;

    [ObservableProperty]
    private int _maxActiveMetadataResolutions = 4;

    [ObservableProperty]
    private int _maxActiveDownloads = 4;

    [ObservableProperty]
    private bool _completionCallbackEnabled;

    [ObservableProperty]
    private string _completionCallbackCommandPath = string.Empty;

    [ObservableProperty]
    private string _completionCallbackArguments = string.Empty;

    [ObservableProperty]
    private string _completionCallbackWorkingDirectory = string.Empty;

    [ObservableProperty]
    private int _completionCallbackTimeoutSeconds = 30;

    [ObservableProperty]
    private int _completionCallbackFinalizationTimeoutSeconds = 120;

    [ObservableProperty]
    private string _completionCallbackApiBaseUrlOverride = string.Empty;

    [ObservableProperty]
    private string _completionCallbackApiKeyOverride = string.Empty;

    [ObservableProperty]
    private bool _isCallbackAdvancedExpanded;

    [ObservableProperty]
    private int _appliedMaxConnections = 150;

    [ObservableProperty]
    private int _appliedHalfOpenConnections = 8;

    [ObservableProperty]
    private int _appliedDownloadRateBytesPerSecond;

    [ObservableProperty]
    private int _appliedUploadRateBytesPerSecond;

    [ObservableProperty]
    private bool _engineSettingsRequireRestart;

    public ObservableCollection<EditableTorrentCategoryViewModel> Categories { get; } = [];

    public IReadOnlyList<string> SeedingModes { get; } =
    [
        "Unlimited",
        "StopImmediately",
        "StopAfterRatio",
        "StopAfterTime",
        "StopAfterRatioOrTime",
    ];

    public IReadOnlyList<string> CleanupModes { get; } =
    [
        "Never",
        "AfterCompletedMinutes",
    ];

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasCategories => Categories.Count > 0;
    public string AppliedDownloadRateText => FormatRateLimit(AppliedDownloadRateBytesPerSecond);
    public string AppliedUploadRateText => FormatRateLimit(AppliedUploadRateBytesPerSecond);

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        Message = null;
        ErrorMessage = null;

        try
        {
            var settings = await client.UpdateRuntimeSettingsAsync(new UpdateRuntimeSettingsRequest
            {
                SeedingStopMode = SeedingStopMode,
                SeedingStopRatio = SeedingStopRatio,
                SeedingStopMinutes = SeedingStopMinutes,
                CompletedTorrentCleanupMode = CompletedTorrentCleanupMode,
                CompletedTorrentCleanupMinutes = CompletedTorrentCleanupMinutes,
                EngineConnectionFailureLogBurstLimit = EngineConnectionFailureLogBurstLimit,
                EngineConnectionFailureLogWindowSeconds = EngineConnectionFailureLogWindowSeconds,
                EngineMaximumConnections = EngineMaximumConnections,
                EngineMaximumHalfOpenConnections = EngineMaximumHalfOpenConnections,
                EngineMaximumDownloadRateBytesPerSecond = EngineMaximumDownloadRateBytesPerSecond,
                EngineMaximumUploadRateBytesPerSecond = EngineMaximumUploadRateBytesPerSecond,
                MaxActiveMetadataResolutions = MaxActiveMetadataResolutions,
                MaxActiveDownloads = MaxActiveDownloads,
                CompletionCallbackEnabled = CompletionCallbackEnabled,
                CompletionCallbackCommandPath = CompletionCallbackCommandPath,
                CompletionCallbackArguments = CompletionCallbackArguments,
                CompletionCallbackWorkingDirectory = CompletionCallbackWorkingDirectory,
                CompletionCallbackTimeoutSeconds = CompletionCallbackTimeoutSeconds,
                CompletionCallbackFinalizationTimeoutSeconds = CompletionCallbackFinalizationTimeoutSeconds,
                CompletionCallbackApiBaseUrlOverride = CompletionCallbackApiBaseUrlOverride,
                CompletionCallbackApiKeyOverride = CompletionCallbackApiKeyOverride,
            });

            var updatedCategories = new List<TorrentCategoryDto>(Categories.Count);
            foreach (var category in Categories.OrderBy(item => item.SortOrder).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                updatedCategories.Add(await client.UpdateCategoryAsync(category.Key, new UpdateTorrentCategoryRequest
                {
                    DisplayName = category.DisplayName,
                    CallbackLabel = category.CallbackLabel,
                    DownloadRootPath = category.DownloadRootPath,
                    Enabled = category.Enabled,
                    InvokeCompletionCallback = category.InvokeCompletionCallback,
                    SortOrder = category.SortOrder,
                }));
            }

            Apply(settings);
            ApplyCategories(updatedCategories);
            Message = settings.EngineSettingsRequireRestart
                ? "Runtime settings and categories saved. Restart TorrentCore.Service to apply engine throttle changes."
                : "Runtime settings and categories saved.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to save runtime settings and categories: {exception.Message}";
        }
        finally
        {
            IsSaving = false;
            RaiseComputedState();
        }
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        Message = null;
        ErrorMessage = null;

        try
        {
            var settingsTask = client.GetRuntimeSettingsAsync();
            var categoriesTask = client.GetCategoriesAsync();
            await Task.WhenAll(settingsTask, categoriesTask);
            var settings = settingsTask.Result;
            if (settings is null)
            {
                ErrorMessage = "Runtime settings endpoint returned no payload.";
                return;
            }

            Apply(settings);
            ApplyCategories(categoriesTask.Result);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to load runtime settings and categories: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
            RaiseComputedState();
        }
    }

    private void Apply(RuntimeSettingsDto settings)
    {
        EngineRuntime = settings.EngineRuntime;
        PartialFilesSummary = settings.PartialFilesEnabled
            ? $"Enabled ({settings.PartialFileSuffix})"
            : "Disabled";
        PersistedOverridesSummary = settings.UsesPersistedOverrides ? "Yes" : "No";
        SupportsLiveUpdates = settings.SupportsLiveUpdates;
        UpdatedAtSummary = settings.UpdatedAtUtc?.ToLocalTime().ToString("g") ?? "Not yet changed";
        RetrievedAtSummary = settings.RetrievedAtUtc.ToLocalTime().ToString("g");
        SeedingStopMode = settings.SeedingStopMode;
        SeedingStopRatio = settings.SeedingStopRatio;
        SeedingStopMinutes = settings.SeedingStopMinutes;
        CompletedTorrentCleanupMode = settings.CompletedTorrentCleanupMode;
        CompletedTorrentCleanupMinutes = settings.CompletedTorrentCleanupMinutes;
        EngineConnectionFailureLogBurstLimit = settings.EngineConnectionFailureLogBurstLimit;
        EngineConnectionFailureLogWindowSeconds = settings.EngineConnectionFailureLogWindowSeconds;
        EngineMaximumConnections = settings.EngineMaximumConnections;
        EngineMaximumHalfOpenConnections = settings.EngineMaximumHalfOpenConnections;
        EngineMaximumDownloadRateBytesPerSecond = settings.EngineMaximumDownloadRateBytesPerSecond;
        EngineMaximumUploadRateBytesPerSecond = settings.EngineMaximumUploadRateBytesPerSecond;
        MaxActiveMetadataResolutions = settings.MaxActiveMetadataResolutions;
        MaxActiveDownloads = settings.MaxActiveDownloads;
        CompletionCallbackEnabled = settings.CompletionCallbackEnabled;
        CompletionCallbackCommandPath = settings.CompletionCallbackCommandPath ?? string.Empty;
        CompletionCallbackArguments = settings.CompletionCallbackArguments ?? string.Empty;
        CompletionCallbackWorkingDirectory = settings.CompletionCallbackWorkingDirectory ?? string.Empty;
        CompletionCallbackTimeoutSeconds = settings.CompletionCallbackTimeoutSeconds;
        CompletionCallbackFinalizationTimeoutSeconds = settings.CompletionCallbackFinalizationTimeoutSeconds;
        CompletionCallbackApiBaseUrlOverride = settings.CompletionCallbackApiBaseUrlOverride ?? string.Empty;
        CompletionCallbackApiKeyOverride = settings.CompletionCallbackApiKeyOverride ?? string.Empty;
        AppliedMaxConnections = settings.AppliedEngineMaximumConnections;
        AppliedHalfOpenConnections = settings.AppliedEngineMaximumHalfOpenConnections;
        AppliedDownloadRateBytesPerSecond = settings.AppliedEngineMaximumDownloadRateBytesPerSecond;
        AppliedUploadRateBytesPerSecond = settings.AppliedEngineMaximumUploadRateBytesPerSecond;
        EngineSettingsRequireRestart = settings.EngineSettingsRequireRestart;
        RaiseComputedState();
    }

    private void ApplyCategories(IReadOnlyList<TorrentCategoryDto> categories)
    {
        Categories.Clear();

        foreach (var category in categories
                     .OrderBy(item => item.SortOrder)
                     .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            Categories.Add(new EditableTorrentCategoryViewModel(category.Key)
            {
                DisplayName = category.DisplayName,
                CallbackLabel = category.CallbackLabel,
                DownloadRootPath = category.DownloadRootPath,
                Enabled = category.Enabled,
                InvokeCompletionCallback = category.InvokeCompletionCallback,
                SortOrder = category.SortOrder,
            });
        }

        OnPropertyChanged(nameof(HasCategories));
    }

    private void RaiseComputedState()
    {
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasCategories));
        OnPropertyChanged(nameof(AppliedDownloadRateText));
        OnPropertyChanged(nameof(AppliedUploadRateText));
    }

    private static string FormatRateLimit(int bytesPerSecond) =>
        bytesPerSecond <= 0 ? "Unlimited" : $"{bytesPerSecond / 1_000_000.0:0.00} MB/s";
}
