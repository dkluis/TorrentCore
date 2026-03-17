using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentCore.Avalonia.Infrastructure;
using TorrentCore.Client;

namespace TorrentCore.Avalonia.ViewModels;

public partial class ConnectionSetupViewModel(
    AvaloniaServiceConnectionManager connectionManager,
    Func<Task> onConnectionSavedAsync) : ViewModelBase
{
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _configuredEndpoint = "Not configured";

    [ObservableProperty]
    private string _reachabilityStatus = "Unknown";

    [ObservableProperty]
    private string _lastCheckedSummary = "Not checked yet";

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy => IsLoading || IsTesting || IsSaving;

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
            var probeResult = await connectionManager.GetStatusAsync();
            BaseUrl = probeResult.BaseUrl ?? connectionManager.DefaultBaseUrl ?? string.Empty;
            ApplyStatus(probeResult);
        }
        finally
        {
            IsLoading = false;
            RaiseComputedState();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    public async Task TestAsync()
    {
        if (IsTesting || IsSaving)
        {
            return;
        }

        IsTesting = true;
        Message = null;
        ErrorMessage = null;

        try
        {
            var probeResult = await connectionManager.TestAsync(BaseUrl);
            ApplyStatus(probeResult);

            if (probeResult.IsReachable)
            {
                Message = "Connection test passed. Save this endpoint to use it on the next startup.";
            }
            else
            {
                ErrorMessage = probeResult.ErrorMessage ?? "The configured endpoint could not be reached.";
            }
        }
        finally
        {
            IsTesting = false;
            RaiseComputedState();
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsSaving || IsTesting)
        {
            return;
        }

        IsSaving = true;
        Message = null;
        ErrorMessage = null;

        try
        {
            var probeResult = await connectionManager.SaveAsync(BaseUrl);
            ApplyStatus(probeResult);

            if (!probeResult.IsReachable)
            {
                ErrorMessage = probeResult.ErrorMessage ?? "The configured endpoint could not be reached.";
                return;
            }

            Message = "Connection saved. Loading the desktop dashboard.";
            RaiseComputedState();
            await onConnectionSavedAsync();
        }
        finally
        {
            IsSaving = false;
            RaiseComputedState();
        }
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnIsTestingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    private void ApplyStatus(TorrentCoreConnectionProbeResult probeResult)
    {
        ConfiguredEndpoint = probeResult.BaseUrl ?? "Not configured";
        ReachabilityStatus = probeResult.IsReachable ? "Reachable" : "Unavailable";
        LastCheckedSummary = probeResult.CheckedAtUtc.LocalDateTime.ToString("g");
    }

    private void RaiseComputedState()
    {
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsBusy));
    }
}
