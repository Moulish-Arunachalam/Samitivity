using Samitivity.Core;
namespace Samitivity.Tests;
public sealed class AudioCacheTests
{
    [Fact] public void LeastRecentlyUsedIsEvictedWithinByteBudget()
    {
        var cache = new AudioCache<string>(10);
        cache.Put("a", "a", 4); cache.Put("b", "b", 4); Assert.Equal("a", cache.Get("a"));
        cache.Put("c", "c", 4);
        Assert.Null(cache.Get("b")); Assert.Equal("a", cache.Get("a")); Assert.Equal(8, cache.Bytes);
    }
    [Fact] public void OversizedAudioIsNotCachedAndRevisionsDoNotAlias()
    {
        var cache = new AudioCache<string>(10);
        cache.Put("id:length:mtime1", "old", 4);
        Assert.Null(cache.Get("id:length:mtime2")); cache.Put("large", "x", 11);
        Assert.Null(cache.Get("large")); Assert.Equal(4, cache.Bytes);
    }
    [Fact] public void ReplacingKeyAndConcurrentUseRemainBounded()
    {
        var cache = new AudioCache<string>(20);
        Parallel.For(0, 1000, i => { cache.Put((i % 10).ToString(), "audio", 4); cache.Get((i % 10).ToString()); });
        Assert.InRange(cache.Bytes, 0, 20);
        cache.Put("same", "a", 4); cache.Put("same", "b", 4); Assert.Equal("b", cache.Get("same")); Assert.InRange(cache.Bytes, 4, 20);
    }
}
