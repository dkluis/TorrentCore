using TorrentCore.Client;

namespace TorrentCore.Avalonia.Models;

public sealed class AppConfiguration
{
    public TorrentCoreClientOptions TorrentCoreService { get; init; } = new();
}
