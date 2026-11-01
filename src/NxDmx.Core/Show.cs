using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;

namespace NxDmx.Core;

/// <summary>Everything that belongs to a show file: patch, groups and cuelists.</summary>
public sealed class Show
{
    public Patch Patch { get; } = new();

    public SortedDictionary<int, Group> Groups { get; } = new();

    public SortedDictionary<int, Cuelist> Cuelists { get; } = new();

    /// <summary>Takes over the contents of another show, keeping this instance (the interpreter holds on to it).</summary>
    internal void ReplaceWith(Show other)
    {
        Patch.Clear();
        foreach (var fixture in other.Patch.Fixtures)
            Patch.Add(fixture);

        Groups.Clear();
        foreach (var (number, group) in other.Groups)
            Groups[number] = group;

        Cuelists.Clear();
        foreach (var (number, cuelist) in other.Cuelists)
            Cuelists[number] = cuelist;
    }

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
