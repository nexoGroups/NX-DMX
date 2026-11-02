using NxDmx.Core.Dmx;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;

namespace NxDmx.Core.Commands;

/// <summary>
/// A deep copy of everything an operator can edit: universes, profiles, patch, groups, cuelists and the programmer.
/// Backs Oops and rolls back commands that fail half-way. Restoring keeps the live playback position of cuelists,
/// because Go, Back and Off are not programming steps.
/// </summary>
internal sealed class ShowSnapshot
{
    private sealed record FixtureData(int Id, FixtureType Type, int Universe, int Address, string Label);

    private sealed record GroupData(int Number, string Label, List<int> FixtureIds);

    private sealed record CueData(decimal Number, string Label, Dictionary<ValueKey, double> Values);

    private sealed record CuelistData(int Number, string Label, List<CueData> Cues, decimal? Current);

    private readonly List<UniverseConfig> _universes;
    private readonly List<FaderConfig> _faders;
    private readonly List<ColorPreset> _colors;
    private readonly List<FixtureType> _customTypes;
    private readonly List<FixtureData> _fixtures;
    private readonly List<GroupData> _groups;
    private readonly List<CuelistData> _cuelists;
    private readonly List<int> _selection;
    private readonly Dictionary<ValueKey, double> _values;

    private ShowSnapshot(Show show, Programmer programmer)
    {
        _universes = show.Universes.Values.Select(u => u.Clone()).ToList();
        _faders = show.Faders.Values.Select(x => x.Clone()).ToList();
        _colors = [.. show.CustomColors.Values];
        _customTypes = [.. show.CustomTypes.Values];
        _fixtures = show.Patch.Fixtures
            .Select(f => new FixtureData(f.Id, f.Type, f.Universe, f.Address, f.Label))
            .ToList();
        _groups = show.Groups.Values
            .Select(g => new GroupData(g.Number, g.Label, [.. g.FixtureIds]))
            .ToList();
        _cuelists = show.Cuelists.Values
            .Select(l => new CuelistData(
                l.Number,
                l.Label,
                l.Cues.Select(c => new CueData(c.Number, c.Label, new Dictionary<ValueKey, double>(c.Values))).ToList(),
                l.CurrentNumber))
            .ToList();
        _selection = [.. programmer.Selection];
        _values = new Dictionary<ValueKey, double>(programmer.Values);
    }

    public static ShowSnapshot Capture(Show show, Programmer programmer) => new(show, programmer);

    public void Restore(Show show, Programmer programmer)
    {
        var livePositions = show.Cuelists.ToDictionary(pair => pair.Key, pair => pair.Value.CurrentNumber);

        show.Universes.Clear();
        foreach (var u in _universes)
            show.Universes[u.Number] = u.Clone();

        show.Faders.Clear();
        foreach (var x in _faders)
            show.Faders[x.Number] = x.Clone();

        show.CustomColors.Clear();
        foreach (var c in _colors)
            show.CustomColors[c.Name] = c;

        show.CustomTypes.Clear();
        foreach (var type in _customTypes)
            show.CustomTypes[type.Name] = type;

        show.Patch.Clear();
        foreach (var f in _fixtures)
            show.Patch.Add(new Fixture(f.Id, f.Type, f.Universe, f.Address, f.Label));

        show.Groups.Clear();
        foreach (var g in _groups)
            show.Groups[g.Number] = new Group(g.Number, g.Label, [.. g.FixtureIds]);

        show.Cuelists.Clear();
        foreach (var l in _cuelists)
        {
            var cuelist = new Cuelist(l.Number) { Label = l.Label };
            foreach (var c in l.Cues)
                cuelist.Set(new Cue(c.Number, c.Label, new Dictionary<ValueKey, double>(c.Values)));

            cuelist.RestorePosition(livePositions.TryGetValue(l.Number, out var live) ? live : l.Current);
            show.Cuelists[l.Number] = cuelist;
        }

        programmer.Select(_selection);
        programmer.ClearValues();
        foreach (var (key, value) in _values)
            programmer.Set(key.FixtureId, key.Attribute, value);
    }
}
