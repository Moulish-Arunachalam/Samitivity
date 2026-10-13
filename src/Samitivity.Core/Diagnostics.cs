using System.Diagnostics;
using System.Threading.Channels;

namespace Samitivity.Core;

/// <summary>Bounded, nonblocking timing events. Only fixed stage names and numeric IDs belong here.</summary>
public static class Diagnostics
{
    private static readonly long origin = Stopwatch.GetTimestamp();
    private static readonly Channel<string> events = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private static Task? writer;
    public static long Now => Stopwatch.GetTimestamp();
    public static void Mark(string stage, long started = 0, long request = 0) => events.Writer.TryWrite(
        FormattableString.Invariant($"{Stopwatch.GetElapsedTime(origin).TotalMilliseconds:F2}\t{request}\t{stage}\t{(started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalMilliseconds):F2}"));
    public static void Start(string directory)
    {
        writer ??= Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "performance.tsv");
                if (File.Exists(path)) File.Move(path, path + ".previous", true);
                await using var file = new StreamWriter(path);
                await file.WriteLineAsync("session_ms\trequest\tstage\tduration_ms");
                var lines = 0;
                await foreach (var entry in events.Reader.ReadAllAsync())
                {
                    await file.WriteLineAsync(entry);
                    await file.FlushAsync();
                    // Bound each session log; keep the writer draining after the cap.
                    if (++lines == 20000) break;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Diagnostics never prevent playback. */ }
        });
    }
}
