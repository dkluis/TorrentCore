using MudBlazor;

namespace TorrentCore.WebUI.Services;

public sealed class OperatorFeedbackService(ISnackbar snackbar, IDialogService dialogService) : IOperatorFeedbackService
{
    public void Info(string message) => Add(message, Severity.Info);
    public void Success(string message) => Add(message, Severity.Success);
    public void Warning(string message) => Add(message, Severity.Warning);
    public void Error(string message) => Add(message, Severity.Error);

    public void ShowActionResult(ServiceCallResult result, string successMessage)
    {
        if (result.IsSuccess)
        {
            Success(successMessage);
            return;
        }

        Error(result.ErrorMessage ?? "The request failed.");
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel"
    )
    {
        var confirmed = await dialogService.ShowMessageBox(
            title,
            message,
            yesText: confirmText,
            cancelText: cancelText,
            options: new DialogOptions
            {
                CloseButton = true,
                CloseOnEscapeKey = true,
                BackdropClick = false,
                FullWidth = true,
                MaxWidth = MaxWidth.Small,
            }
        );

        return confirmed == true;
    }

    private void Add(string message, Severity severity)
    {
        snackbar.Add(
            message,
            severity,
            options =>
            {
                options.ShowCloseIcon = true;
                options.VisibleStateDuration = 4500;
            }
        );
    }
}
