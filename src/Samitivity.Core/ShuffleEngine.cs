using System.Security.Cryptography;
using System.Text.Json;

namespace Samitivity.Core;

public interface IRandomSource { int Next(int exclusiveMax); }
public sealed class SecureRandomSource : IRandomSource { public int Next(int exclusiveMax) => RandomNumberGenerator.GetInt32(exclusiveMax); }
public sealed class CategoryState
{
    public List<string> Known { get; set; } = [];
    public List<string> Remaining { get; set; } = [];
    public List<string> Recent { get; set; } = [];
}
public sealed class PlaylistState
{
    public int Version { get; set; } = 1;
    public Dictionary<Guid, CategoryState> Categories { get; set; } = [];
    public void Validate()
    {
        if (Version != 1 || Categories is null || Categories.Count > 4096) throw new InvalidDataException("Invalid playlist state.");
        foreach (var (id, s) in Categories)
            if (id == Guid.Empty || s is null || s.Known is null || s.Remaining is null || s.Recent is null ||
                s.Known.Count > 100000 || s.Recent.Count > 3 ||
                s.Known.Concat(s.Remaining).Concat(s.Recent).Any(string.IsNullOrWhiteSpace) ||
                s.Known.Distinct().Count() != s.Known.Count || s.Remaining.Distinct().Count() != s.Remaining.Count ||
                s.Remaining.Except(s.Known).Any()) throw new InvalidDataException("Invalid category state.");
    }
}
public sealed class ShuffleEngine
{
    private readonly object gate = new();
    private readonly IRandomSource random;
    private readonly AtomicJsonStore<PlaylistState> store;
    private PlaylistState state;
    public string? RecoveryWarning { get; }
    public ShuffleEngine(AtomicJsonStore<PlaylistState> store, IRandomSource? random = null)
    {
        this.store = store; this.random = random ?? new SecureRandomSource();
        var loaded = store.Load(); state = loaded.Value; RecoveryWarning = loaded.Warning;
    }
    public string? Select(Guid category, IEnumerable<string> eligible)
    {
        lock (gate)
        {
            if (category == Guid.Empty) throw new ArgumentException("Category ID is required.");
            var all = eligible.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
            if (all.Count == 0) return null;
            // Commit consumption before audio starts. Failed persistence must not mutate in-memory state.
            var next = JsonSerializer.Deserialize<PlaylistState>(JsonSerializer.Serialize(state))!;
            if (!next.Categories.TryGetValue(category, out var bag)) next.Categories[category] = bag = new();
            var set = all.ToHashSet(StringComparer.Ordinal);
            bag.Remaining.RemoveAll(x => !set.Contains(x));
            bag.Recent.RemoveAll(x => !set.Contains(x));
            var added = all.Except(bag.Known, StringComparer.Ordinal).ToList();
            bag.Known = all;
            foreach (var id in added) bag.Remaining.Insert(random.Next(bag.Remaining.Count + 1), id);
            if (bag.Remaining.Count == 0)
            {
                bag.Remaining = [.. all];
                for (var i = bag.Remaining.Count - 1; i > 0; i--)
                { var j = random.Next(i + 1); (bag.Remaining[i], bag.Remaining[j]) = (bag.Remaining[j], bag.Remaining[i]); }
            }
            // Preserve the shuffled order; relax oldest history first if the bag is too small.
            var avoid = bag.Recent.ToList();
            var index = bag.Remaining.FindIndex(x => !avoid.Contains(x));
            while (index < 0 && avoid.Count > 0)
            { avoid.RemoveAt(0); index = bag.Remaining.FindIndex(x => !avoid.Contains(x)); }
            if (index < 0) index = 0;
            var selected = bag.Remaining[index]; bag.Remaining.RemoveAt(index);
            bag.Recent.Add(selected);
            if (bag.Recent.Count > 3) bag.Recent.RemoveAt(0);
            store.Save(next); state = next; return selected;
        }
    }
}
