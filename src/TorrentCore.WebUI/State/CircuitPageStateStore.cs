namespace TorrentCore.WebUI.State;

public sealed class CircuitPageStateStore : IPageStateStore
{
    private readonly Dictionary<string, object> _stateByKey = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public bool TryGet<TState>(string key, out TState? state)
        where TState : class
    {
        lock (_gate)
        {
            if (_stateByKey.TryGetValue(key, out var entry) && entry is TState typedState)
            {
                state = typedState;
                return true;
            }
        }

        state = null;
        return false;
    }

    public TState GetOrCreate<TState>(string key, Func<TState> factory)
        where TState : class
    {
        lock (_gate)
        {
            if (_stateByKey.TryGetValue(key, out var entry) && entry is TState typedState)
            {
                return typedState;
            }

            var createdState = factory();
            _stateByKey[key] = createdState;
            return createdState;
        }
    }

    public void Save<TState>(string key, TState state)
        where TState : class
    {
        lock (_gate)
        {
            _stateByKey[key] = state;
        }
    }

    public void Clear(string key)
    {
        lock (_gate)
        {
            _stateByKey.Remove(key);
        }
    }
}
