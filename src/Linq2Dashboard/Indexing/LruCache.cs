namespace Linq2Dashboard.Indexing;

/// <summary>
/// Small bounded least-recently-used cache (design §5). Safe for concurrent readers and writers.
/// The factory runs outside the lock, so a miss may be computed twice under contention; results
/// are immutable and equal, so that is harmless and the first one in wins.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> map;
    private readonly LinkedList<Entry> order = new();
    private readonly Lock gate = new();

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.capacity = capacity;
        map = new Dictionary<TKey, LinkedListNode<Entry>>(capacity);
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return map.Count;
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (gate)
        {
            if (map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                order.Remove(node);
                order.AddFirst(node);
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
        lock (gate)
        {
            if (map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                order.Remove(node);
                order.AddFirst(node);
                return node.Value.Value;
            }

            node = order.AddFirst(new Entry(key, created));
            map[key] = node;
            if (map.Count > capacity)
            {
                LinkedListNode<Entry> last = order.Last!;
                order.RemoveLast();
                map.Remove(last.Value.Key);
            }
        }

        return created;
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
