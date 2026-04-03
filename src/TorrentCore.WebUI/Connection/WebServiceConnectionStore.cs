using System.Text.Json;

namespace TorrentCore.WebUI.Connection;

public sealed class WebServiceConnectionStore(IHostEnvironment hostEnvironment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _filePath = Path.Combine(
        hostEnvironment.ContentRootPath, "Config", "service-connection.json"
    );

    public async Task<WebServiceConnectionRecord?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<WebServiceConnectionRecord>(
            stream, JsonOptions, cancellationToken
        );
    }

    public async Task SaveAsync(WebServiceConnectionRecord record, CancellationToken cancellationToken = default)
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
