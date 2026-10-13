namespace Samitivity.Core;

/// <summary>Byte-bounded LRU. Keys include a file revision; active callers retain their own immutable value.</summary>
public sealed class AudioCache<T>(long capacity) where T : class
{
    private readonly object gate = new();
    private readonly LinkedList<(string Key, T Value, long Size)> entries = new();
    public long Bytes { get { lock (gate) return bytes; } }
    private long bytes;
    public T? Get(string key)
    {
        lock (gate)
        {
            var node = entries.First;
            while (node is not null)
            {
                if (node.Value.Key == key) { entries.Remove(node); entries.AddFirst(node); return node.Value.Value; }
                node = node.Next;
            }
            return null;
        }
    }
    public void Put(string key, T value, long size)
    {
        if (size <= 0 || size > capacity) return;
        lock (gate)
        {
            var existing = entries.First;
            while (existing is not null)
            {
                var next = existing.Next;
                if (existing.Value.Key == key) { bytes -= existing.Value.Size; entries.Remove(existing); }
                existing = next;
            }
            while (bytes + size > capacity && entries.Last is { } last) { bytes -= last.Value.Size; entries.RemoveLast(); }
            entries.AddFirst((key, value, size)); bytes += size;
        }
    }
}
