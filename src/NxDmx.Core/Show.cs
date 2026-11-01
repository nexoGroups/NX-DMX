using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;

namespace NxDmx.Core;

/// <summary>Everything that belongs to a show file: patch, groups and cuelists.</summary>
public sealed class Show
{
    public Patch Patch { get; } = new();

    public SortedDictionary<int, Group> Groups { get; } = new();

    public SortedDictionary<int, Cuelist> Cuelists { get; } = new();

    public Cuelist GetOrAddCuelist(int number)
    {
        if (!Cuelists.TryGetValue(number, out var cuelist))
        {
            cuelist = new Cuelist(number);
            Cuelists[number] = cuelist;
        }

        return cuelist;
    }
}
