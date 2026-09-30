#if NETWASM
// NetWasm (netwasm0.1) proof of concept: CoreLib has no ConditionalWeakTable (and no weak GC handles).
// This is a strongly-referencing, lock-protected stand-in that covers the members PolyType uses.
// Entries are never collected, which is acceptable for caches keyed on types/providers that live for the app lifetime.
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.CompilerServices;

internal sealed class ConditionalWeakTable<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : class
    where TValue : class?
{
    private readonly Dictionary<TKey, TValue> _map = new(ReferenceEqualityComparer.Instance);

    public delegate TValue CreateValueCallback(TKey key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_map)
        {
            return _map.TryGetValue(key, out value);
        }
    }

    public void Add(TKey key, TValue value)
    {
        lock (_map)
        {
            _map.Add(key, value);
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        lock (_map)
        {
            return _map.TryAdd(key, value);
        }
    }

    public void AddOrUpdate(TKey key, TValue value)
    {
        lock (_map)
        {
            _map[key] = value;
        }
    }

    public bool Remove(TKey key)
    {
        lock (_map)
        {
            return _map.Remove(key);
        }
    }

    public TValue GetValue(TKey key, CreateValueCallback createValueCallback)
    {
        lock (_map)
        {
            if (!_map.TryGetValue(key, out TValue? value))
            {
                value = createValueCallback(key);
                _map.Add(key, value);
            }

            return value;
        }
    }

    public TValue GetOrCreateValue(TKey key) => TryGetValue(key, out TValue? value) ? value : throw new KeyNotFoundException();

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        KeyValuePair<TKey, TValue>[] snapshot;
        lock (_map)
        {
            snapshot = new KeyValuePair<TKey, TValue>[_map.Count];
            int i = 0;
            foreach (KeyValuePair<TKey, TValue> pair in _map)
            {
                snapshot[i++] = pair;
            }
        }

        return ((IEnumerable<KeyValuePair<TKey, TValue>>)snapshot).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
#endif
