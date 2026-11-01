namespace NxDmx.Core.Dmx;

/// <summary>One rendered output state: 512 channels for every universe that is defined or has patched fixtures.</summary>
public sealed class DmxFrame
{
    public const int UniverseSize = 512;

    private readonly SortedDictionary<int, byte[]> _universes = new();
    private readonly Dictionary<int, UniverseRoute> _routes = new();

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

    public void SetRoute(int universe, UniverseRoute route) => _routes[universe] = route;

    /// <summary>How to send the universe. A universe nobody configured goes out by broadcast.</summary>
    public UniverseRoute RouteOf(int universe) => _routes.GetValueOrDefault(universe) ?? UniverseRoute.Default;
}
