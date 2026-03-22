namespace TorrentCore.Avalonia.Infrastructure;

public sealed class AppConnectionSettingsRecord
{
    public string         BaseUrl      { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
