using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Client;
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
    private int _appliedMaxConnections = 150;

    [ObservableProperty]
    private int _appliedHalfOpenConnections = 8;

    [ObservableProperty]
    private int _appliedDownloadRateBytesPerSecond;

    [ObservableProperty]
    private int _appliedUploadRateBytesPerSecond;

    [ObservableProperty]
    private bool _engineSettingsRequireRestart;

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
            });

            Apply(settings);
            Message = settings.EngineSettingsRequireRestart
                ? "Runtime settings saved. Restart TorrentCore.Service to apply engine throttle changes."
                : "Runtime settings saved.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to save runtime settings: {exception.Message}";
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
            var settings = await client.GetRuntimeSettingsAsync();
            if (settings is null)
            {
                ErrorMessage = "Runtime settings endpoint returned no payload.";
                return;
            }

            Apply(settings);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Unable to load runtime settings: {exception.Message}";
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
        UpdatedAtSummary = settings.UpdatedAtUtc?.ToLocalTime().ToString("g") ?? "Not yet changed";
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
        AppliedMaxConnections = settings.AppliedEngineMaximumConnections;
        AppliedHalfOpenConnections = settings.AppliedEngineMaximumHalfOpenConnections;
        AppliedDownloadRateBytesPerSecond = settings.AppliedEngineMaximumDownloadRateBytesPerSecond;
        AppliedUploadRateBytesPerSecond = settings.AppliedEngineMaximumUploadRateBytesPerSecond;
        EngineSettingsRequireRestart = settings.EngineSettingsRequireRestart;
        RaiseComputedState();
    }

    private void RaiseComputedState()
    {
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(AppliedDownloadRateText));
        OnPropertyChanged(nameof(AppliedUploadRateText));
    }

    private static string FormatRateLimit(int bytesPerSecond) =>
        bytesPerSecond <= 0 ? "Unlimited" : $"{bytesPerSecond / 1_000_000.0:0.00} MB/s";
}
