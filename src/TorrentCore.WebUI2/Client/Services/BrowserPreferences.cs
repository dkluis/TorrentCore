using Microsoft.JSInterop;

namespace TorrentCore.WebUI2.Client.Services;

public sealed class BrowserPreferences(IJSRuntime js)
{
    private const string Key = "torrentcore.webui2.preferences";
    public string Appearance { get; private set; } = "system";
    public bool AutoRefresh { get; private set; } = true;
    public int RefreshSeconds { get; private set; } = 15;
    public event Action? Changed;
    public async Task LoadAsync()
    {
        var saved = await js.InvokeAsync<PreferenceValues?>("tcBrowser.load", Key);
        if (saved is null) return;
        Apply(saved);
    }
    public async Task SaveAsync(string appearance, bool autoRefresh, int seconds)
    {
        if (appearance is not ("system" or "light" or "dark") || seconds is not (5 or 10 or 15))
            throw new InvalidOperationException("Select an appearance and a 5, 10, or 15 second refresh interval.");
        var values = new PreferenceValues(appearance, autoRefresh, seconds);
        await js.InvokeVoidAsync("tcBrowser.store", Key, values);
        Apply(values);
    }
    private void Apply(PreferenceValues values)
    {
        Appearance = values.Appearance is "system" or "light" or "dark" ? values.Appearance : "system";
        AutoRefresh = values.AutoRefresh;
        RefreshSeconds = values.RefreshSeconds is 5 or 10 or 15 ? values.RefreshSeconds : 15;
        Changed?.Invoke();
    }
    public sealed record PreferenceValues(string Appearance, bool AutoRefresh, int RefreshSeconds);
}
