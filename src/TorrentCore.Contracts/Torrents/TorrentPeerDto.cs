namespace TorrentCore.Contracts.Torrents;

public sealed class TorrentPeerDto
{
    public required string Endpoint                     { get; init; }
    public required string Client                       { get; init; }
    public required string Direction                    { get; init; }
    public required bool   IsConnected                  { get; init; }
    public required bool   IsSeeder                     { get; init; }
    public required long   DownloadRateBytesPerSecond   { get; init; }
    public required long   UploadRateBytesPerSecond     { get; init; }
    public required long   DownloadedBytes              { get; init; }
    public required long   UploadedBytes                { get; init; }
    public required string Encryption                   { get; init; }
}
