namespace TorrentCore.WebUI.State;

public interface IPageStateStore
{
    bool TryGet<TState>(string key, out TState? state) where TState : class;
    TState GetOrCreate<TState>(string key, Func<TState> factory) where TState : class;
    void Save<TState>(string key, TState state) where TState : class;
    void Clear(string key);
}
