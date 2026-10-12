using Samitivity.Core;

namespace Samitivity.Tests;
public sealed class ShuffleTests : TempTest
{
    private readonly Guid category = Guid.NewGuid();
    [Fact] public void EveryNoteExactlyOncePerCycle()
    {
        var engine = Engine(); var notes = Enumerable.Range(0, 11).Select(x => x.ToString()).ToArray();
        for (var cycle = 0; cycle < 20; cycle++) Assert.Equal(notes.Order(), Enumerable.Range(0, notes.Length).Select(_ => engine.Select(category, notes)!).Order());
    }
    [Fact] public void RecentThreeAvoidedWheneverAnAlternativeRemains()
    {
        var engine = Engine(); string[] notes = ["a", "b", "c", "d", "e", "f", "g"];
        var recent = new Queue<string>();
        for (var n = 0; n < 140; n++)
        {
            var chosen = engine.Select(category, notes)!;
            // Cycle boundary and the first four entries necessarily have sufficient alternatives.
            if (n % notes.Length < 4) Assert.DoesNotContain(chosen, recent);
            recent.Enqueue(chosen); if (recent.Count > 3) recent.Dequeue();
        }
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void SmallLibraries(int count)
    {
        var engine = Engine(); var notes = Enumerable.Range(0, count).Select(x => x.ToString()).ToArray(); string? last = null;
        for (var n = 0; n < 10; n++)
        {
            var chosen = engine.Select(category, notes);
            if (count == 0) Assert.Null(chosen); else Assert.Contains(chosen, notes);
            if (count == 2) Assert.NotEqual(last, chosen); last = chosen;
        }
    }
    [Fact] public void RestartPreservesRemainingBagAndRecent()
    {
        string[] notes = ["a", "b", "c", "d", "e"]; var first = Engine().Select(category, notes);
        var restarted = Engine(99); var rest = Enumerable.Range(0, 4).Select(_ => restarted.Select(category, notes)).ToArray();
        Assert.DoesNotContain(first, rest); Assert.Equal(4, rest.Distinct().Count());
        Assert.NotEqual(rest[^1], Engine(42).Select(category, notes));
    }
    [Fact] public void CategoriesAreIndependent()
    {
        var engine = Engine(); var other = Guid.NewGuid(); string[] notes = ["a", "b", "c"];
        var first = engine.Select(category, notes);
        for (var i = 0; i < 20; i++) engine.Select(other, notes);
        Assert.DoesNotContain(first, new[] { engine.Select(category, notes), engine.Select(category, notes) });
    }
    [Fact] public void NewNotesJoinCurrentCycleWithoutReplayingConsumedNotes()
    {
        var engine = Engine(); string[] before = ["a", "b", "c"]; var consumed = engine.Select(category, before);
        string[] after = ["a", "b", "c", "d"];
        var rest = Enumerable.Range(0, 3).Select(_ => engine.Select(category, after)).ToArray();
        Assert.DoesNotContain(consumed, rest); Assert.Contains("d", rest); Assert.Equal(3, rest.Distinct().Count());
    }
    [Fact] public void RemovalsDoNotSelectMissingEntries()
    {
        var engine = Engine(); engine.Select(category, ["a", "b", "c"]);
        for (var i = 0; i < 5; i++) Assert.Equal("b", engine.Select(category, ["b"]));
    }
    [Fact] public void FailedCommitDoesNotConsumeAnotherSelection()
    {
        var engine = Engine(); string[] notes = ["a", "b", "c"];
        var consumed = engine.Select(category, notes);
        var path = Path.Combine(Root, "state.json"); File.Delete(path); Directory.CreateDirectory(path);
        Assert.True(Record.Exception(() => engine.Select(category, notes)) is IOException or UnauthorizedAccessException);
        Directory.Delete(path);
        var remaining = new[] { engine.Select(category, notes), engine.Select(category, notes) };
        Assert.DoesNotContain(consumed, remaining); Assert.Equal(2, remaining.Distinct().Count());
    }
    [Fact] public void InitialShuffleAllowsEveryPositionAcrossSeeds()
    {
        // Structural reachability check; randomness comes from bounded unbiased GetInt32 + Fisher-Yates.
        var seen = new HashSet<string>();
        for (var seed = 0; seed < 100; seed++)
        {
            var engine = new ShuffleEngine(new(Path.Combine(Root, seed + ".json"), () => new(), x => x.Validate()), new SeededRandom(seed));
            seen.Add(engine.Select(category, ["a", "b", "c", "d"])!);
        }
        Assert.Equal(4, seen.Count);
    }
}
