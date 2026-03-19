namespace TorrentCore.Avalonia.Infrastructure;

public interface IClipboardTextService
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
}
