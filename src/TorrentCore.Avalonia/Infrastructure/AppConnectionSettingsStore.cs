#region

using System.Text.Json;

#endregion

namespace TorrentCore.Avalonia.Infrastructure;

public sealed class AppConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TorrentCore.Avalonia",
        "service-connection.json"
    );

    public async Task<AppConnectionSettingsRecord?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<AppConnectionSettingsRecord>(
            stream, JsonOptions, cancellationToken
        );
    }

    public async Task SaveAsync(AppConnectionSettingsRecord record, CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
    }
}
