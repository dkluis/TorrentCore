namespace TorrentCore.WebUI.Services;

public interface IOperatorFeedbackService
{
    void Info(string message);
    void Success(string message);
    void Warning(string message);
    void Error(string message);
    void ShowActionResult(ServiceCallResult result, string successMessage);
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel"
    );
}
