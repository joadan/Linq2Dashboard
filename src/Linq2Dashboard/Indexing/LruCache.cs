namespace Linq2Dashboard.Indexing;

/// <summary>
/// Small bounded least-recently-used cache (design §5). Safe for concurrent readers and writers.
/// The factory runs outside the lock, so a miss may be computed twice under contention; results
/// are immutable and equal, so that is harmless and the first one in wins.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _order = new();
    private readonly Lock _lock = new();

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(capacity);
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        if (TryGet(key, out TValue existing))
        {
            return existing;
        }

        TValue created = factory(key);
        lock (_lock)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Value;
            }

            node = _order.AddFirst(new Entry(key, created));
            _map[key] = node;
            if (_map.Count > _capacity)
            {
                LinkedListNode<Entry> last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }

        return created;
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
