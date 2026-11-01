namespace NxDmx.Core.Dmx;

/// <summary>One rendered output state: 512 channels for every universe that has patched fixtures.</summary>
public sealed class DmxFrame
{
    public const int UniverseSize = 512;

    private readonly SortedDictionary<int, byte[]> _universes = new();

    /// <summary>Universe number (1-based) to its 512 channel values.</summary>
    public IReadOnlyDictionary<int, byte[]> Universes => _universes;

    public byte[] GetOrAdd(int universe)
    {
        if (!_universes.TryGetValue(universe, out var data))
        {
            data = new byte[UniverseSize];
            _universes[universe] = data;
        }

        return data;
    }
}
