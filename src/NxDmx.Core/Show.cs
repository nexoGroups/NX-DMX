using NxDmx.Core.Dmx;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;

namespace NxDmx.Core;

/// <summary>Everything that belongs to a show file: universes, fixture profiles, patch, groups and cuelists.</summary>
public sealed class Show
{
    public Patch Patch { get; } = new();

    public SortedDictionary<int, UniverseConfig> Universes { get; } = new();

    /// <summary>Profiles the operator made. They travel with the show, so a show opens the same on any computer.</summary>
    public Dictionary<string, FixtureType> CustomTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SortedDictionary<int, Group> Groups { get; } = new();

    public SortedDictionary<int, Cuelist> Cuelists { get; } = new();

    /// <summary>What is assigned to the faders of the fader bar. Only assigned faders are listed.</summary>
    public SortedDictionary<int, FaderConfig> Faders { get; } = new();

    /// <summary>Colours the operator added or imported. They take part in <c>Color</c> next to the built-in ones.</summary>
    public Dictionary<string, ColorPreset> CustomColors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Built-in profiles first, then the show's own, each in name order.</summary>
    public IEnumerable<FixtureType> AllTypes =>
        FixtureTypeLibrary.All.Concat(CustomTypes.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));

    /// <summary>Built-in colours (minus those the operator redefined) first, then the custom ones.</summary>
    public IEnumerable<ColorPreset> AllColors =>
        ColorPresets.All.Where(c => !CustomColors.ContainsKey(c.Name)).Concat(CustomColors.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase));

    /// <summary>A custom colour wins over a built-in one of the same name.</summary>
    public ColorPreset? FindColor(string name) =>
        CustomColors.GetValueOrDefault(name.Trim()) ?? ColorPresets.Find(name);

    /// <summary>A profile of the show wins over a built-in one of the same name.</summary>
    public FixtureType? FindType(string name) =>
        CustomTypes.GetValueOrDefault(name.Trim()) ?? FixtureTypeLibrary.Find(name.Trim());

    /// <summary>The universe, created with default settings when the show does not have it yet.</summary>
    public UniverseConfig EnsureUniverse(int number)
    {
        if (!Universes.TryGetValue(number, out var universe))
        {
            universe = new UniverseConfig(number);
            Universes[number] = universe;
        }

        return universe;
    }

    /// <summary>Takes over the contents of another show, keeping this instance (the interpreter holds on to it).</summary>
    internal void ReplaceWith(Show other)
    {
        CustomTypes.Clear();
        foreach (var (name, type) in other.CustomTypes)
            CustomTypes[name] = type;

        Universes.Clear();
        foreach (var (number, universe) in other.Universes)
            Universes[number] = universe;

        Patch.Clear();
        foreach (var fixture in other.Patch.Fixtures)
            Patch.Add(fixture);

        Groups.Clear();
        foreach (var (number, group) in other.Groups)
            Groups[number] = group;

        Cuelists.Clear();
        foreach (var (number, cuelist) in other.Cuelists)
            Cuelists[number] = cuelist;

        Faders.Clear();
        foreach (var (number, fader) in other.Faders)
            Faders[number] = fader;

        CustomColors.Clear();
        foreach (var (name, color) in other.CustomColors)
            CustomColors[name] = color;
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
