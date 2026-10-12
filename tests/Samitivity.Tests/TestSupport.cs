using Samitivity.Core;

namespace Samitivity.Tests;
public abstract class TempTest : IDisposable
{
    protected string Root { get; } = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "SamitivityTests-" + Guid.NewGuid().ToString("N"));
    protected TempTest() => Directory.CreateDirectory(Root);
    protected ShuffleEngine Engine(int seed = 1) => new(new(Path.Combine(Root, "state.json"), () => new(), x => x.Validate()), new SeededRandom(seed));
    public void Dispose() => Directory.Delete(Root, true);
    protected sealed class SeededRandom(int seed) : IRandomSource { private readonly Random random = new(seed); public int Next(int max) => random.Next(max); }
    protected sealed class Validator : IAudioValidator
    {
        public int Calls;
        public TimeSpan Validate(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            if (File.ReadAllText(path).StartsWith("bad")) throw new InvalidDataException("Corrupt synthetic fixture");
            return TimeSpan.FromSeconds(1);
        }
    }
}
