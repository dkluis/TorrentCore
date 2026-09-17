using TorrentCore.WebUI2.Host;
using TorrentCore.WebUI2.Shared;

namespace TorrentCore.WebUI2.Tests;

public sealed class ConnectionStoreTests
{
    [Fact]
    public async Task RestartKeepsProfilesAndSelectionAndSeparatesBrowsers()
    {
        var files = new MemoryFiles();
        var owner = Guid.NewGuid();
        var saved = await new ConnectionStore(files).ChangeAsync(owner,
            state => ConnectionRules.Save(state, new(null, "Test", "http://localhost:7033", "Test", 0)));
        var restarted = new ConnectionStore(files);
        var restored = await restarted.ReadAsync(owner);
        Assert.Equal(saved.ActiveProfileId, restored.ActiveProfileId);
        Assert.Equal(saved.Profiles, restored.Profiles);
        Assert.Empty((await restarted.ReadAsync(Guid.NewGuid())).Profiles);
    }

    [Fact]
    public async Task InvalidExistingFileIsNotOverwritten()
    {
        var files = new MemoryFiles();
        var owner = Guid.NewGuid();
        files.Values[owner] = "invalid content";
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConnectionStore(files).ChangeAsync(owner,
            state => ConnectionRules.Save(state, new(null, "Test", "localhost:7033", "Test", 0))));
        Assert.Equal("invalid content", files.Values[owner]);
    }

    [Fact]
    public async Task FailedWriteLeavesPreviousSelectedConnectionIntact()
    {
        var files = new MemoryFiles();
        var owner = Guid.NewGuid();
        var store = new ConnectionStore(files);
        var saved = await store.ChangeAsync(owner, state => ConnectionRules.Save(state, new(null, "Test", "localhost:7033", "Test", 0)));
        files.FailWrite = true;
        await Assert.ThrowsAsync<IOException>(() => store.ChangeAsync(owner, state => ConnectionRules.Delete(state, new(saved.ActiveProfileId!.Value, 1))));
        Assert.Equal(saved.ActiveProfileId, (await store.ReadAsync(owner)).ActiveProfileId);
    }

    private sealed class MemoryFiles : IConnectionFiles
    {
        public Dictionary<Guid, string> Values { get; } = [];
        public bool FailWrite { get; set; }
        public Task<string?> ReadAsync(Guid browser, CancellationToken token) => Task.FromResult(Values.GetValueOrDefault(browser));
        public Task WriteAsync(Guid browser, string content, CancellationToken token)
        {
            if (FailWrite) throw new IOException("Simulated write failure");
            Values[browser] = content;
            return Task.CompletedTask;
        }
    }
}
