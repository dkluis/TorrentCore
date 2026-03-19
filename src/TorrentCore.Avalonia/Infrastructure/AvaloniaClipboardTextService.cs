using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace TorrentCore.Avalonia.Infrastructure;

public sealed class AvaloniaClipboardTextService : IClipboardTextService
{
    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { Clipboard: not null } mainWindow })
        {
            return null;
        }

        return await mainWindow.Clipboard.TryGetTextAsync();
    }
}
