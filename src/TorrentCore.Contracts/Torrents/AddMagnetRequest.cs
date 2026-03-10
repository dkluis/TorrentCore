namespace TorrentCore.Contracts.Torrents;

public sealed class AddMagnetRequest
{
    public required string MagnetUri { get; init; }
}
