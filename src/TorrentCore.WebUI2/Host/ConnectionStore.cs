using System.Collections.Concurrent;
using System.Text.Json;
using TorrentCore.WebUI2.Shared;

namespace TorrentCore.WebUI2.Host;

public interface IConnectionFiles
{
    Task<string?> ReadAsync(Guid browser, CancellationToken token);
    Task WriteAsync(Guid browser, string content, CancellationToken token);
}

public sealed class ConnectionFiles(string directory) : IConnectionFiles
{
    public async Task<string?> ReadAsync(Guid browser, CancellationToken token)
    {
        var path = Path.Combine(directory, browser.ToString("D") + ".json");
        return File.Exists(path) ? await File.ReadAllTextAsync(path, token) : null;
    }
    public async Task WriteAsync(Guid browser, string content, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, browser.ToString("D") + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class ConnectionStore(IConnectionFiles files)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    public async Task<ConnectionState> ReadAsync(Guid browser, CancellationToken token = default)
    {
        var gate = _locks.GetOrAdd(browser, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try { return await ReadCoreAsync(browser, token); }
        finally { gate.Release(); }
    }
    public async Task<ConnectionState> ChangeAsync(Guid browser, Func<ConnectionState, ConnectionState> change, CancellationToken token = default)
    {
        var gate = _locks.GetOrAdd(browser, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var current = await ReadCoreAsync(browser, token);
            var updated = change(current);
            await files.WriteAsync(browser, JsonSerializer.Serialize(updated, Json), token);
            return updated;
        }
        finally { gate.Release(); }
    }
    private async Task<ConnectionState> ReadCoreAsync(Guid browser, CancellationToken token)
    {
        var text = await files.ReadAsync(browser, token);
        if (text is null) return new([], null, 0);
        ConnectionState? state;
        try { state = JsonSerializer.Deserialize<ConnectionState>(text, Json); }
        catch (JsonException) { throw new InvalidOperationException("Saved connection settings could not be read. The saved file has not been changed."); }
        if (state is null || state.Profiles is null || state.Revision < 0 || state.Profiles.Any(p => p is null || p.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Address) || p.Environment is not ("Production" or "Test" or "Unclassified"))
            || state.Profiles.DistinctBy(p => p.Id).Count() != state.Profiles.Count
            || state.ActiveProfileId.HasValue && state.Profiles.All(p => p.Id != state.ActiveProfileId))
            throw new InvalidOperationException("Saved connection settings are invalid. The saved file has not been changed.");
        foreach (var profile in state.Profiles) ConnectionRules.NormalizeAddress(profile.Address);
        return state;
    }
}
