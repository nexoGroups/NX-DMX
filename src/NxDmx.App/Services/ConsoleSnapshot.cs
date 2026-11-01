using System.Globalization;
using NxDmx.Core;
using NxDmx.Core.Commands;
using NxDmx.Core.Dmx;
using NxDmx.Core.Engine;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;

namespace NxDmx.App.Services;

/// <summary>One fixture as the views show it.</summary>
public sealed record FixtureInfo(
    int Id,
    string Label,
    string TypeName,
    int Universe,
    int Address,
    int EndAddress,
    int Footprint,
    bool Selected,
    bool Programmed,
    bool Conflict,
    double? Intensity,
    byte Red,
    byte Green,
    byte Blue,
    string Detail)
{
    public string Patch => $"{Universe}.{Address}";
}

public sealed record GroupInfo(int Number, string Label, int Count, string Fixtures, bool IsSelected);

public sealed record CueInfo(decimal Number, string NumberText, string Label, int ValueCount, bool IsCurrent);

public sealed record CuelistInfo(int Number, string Label, bool Active, string CurrentText, IReadOnlyList<CueInfo> Cues);

/// <summary>A block of DMX channels taken by one fixture, for the address map.</summary>
public sealed record AddressBlock(int FixtureId, string Label, int Start, int End, bool Conflict);

public sealed record UniverseInfo(
    int Number,
    string Label,
    bool Enabled,
    UniverseDestination Destination,
    string UnicastAddress,
    int FixtureCount,
    int ChannelsUsed,
    IReadOnlyList<AddressBlock> Blocks);

public sealed record ProfileInfo(FixtureType Type, int UsedBy);

/// <summary>
/// One attribute of the current selection: its value (or "mixed" when the fixtures disagree) and the named functions
/// of its channel. This is what the attribute panel offers as clickable values.
/// </summary>
public sealed record SelectedAttribute(
    FixtureAttribute Attribute,
    double? Value,
    bool Mixed,
    bool Programmed,
    IReadOnlyList<ChannelFunction> Functions,
    string? ActiveFunction)
{
    public string Text => Mixed ? "mixed" : Value is { } v ? $"{Math.Round(v * 100):0} %" : "–";
}

public sealed record ProgrammerRow(int Id, string Label, IReadOnlyList<ProgrammerCell> Cells);

public sealed record ProgrammerCell(string Text, bool Programmed);

/// <summary>Everything the views need, copied out of the console while its lock is held.</summary>
public sealed record ConsoleSnapshot(
    IReadOnlyList<FixtureInfo> Fixtures,
    IReadOnlyList<GroupInfo> Groups,
    IReadOnlyList<CuelistInfo> Cuelists,
    IReadOnlyList<UniverseInfo> Universes,
    IReadOnlyList<ProfileInfo> Profiles,
    IReadOnlyList<SelectedAttribute> Attributes,
    IReadOnlyList<FixtureAttribute> ProgrammerColumns,
    IReadOnlyList<ProgrammerRow> ProgrammerRows,
    IReadOnlySet<int> Selection,
    string SelectionText,
    int ProgrammerValueCount,
    bool Blackout,
    double Master)
{
    public static ConsoleSnapshot Build(Show show, Programmer programmer, OutputState output)
    {
        var resolved = FrameRenderer.ResolveValues(show, programmer);
        var selection = programmer.Selection.ToHashSet();
        var programmed = programmer.Values.Keys.Select(k => k.FixtureId).ToHashSet();
        var conflicts = show.Patch.FindConflicts();

        var fixtures = show.Patch.Fixtures
            .Select(f => FixtureOf(f, resolved, selection.Contains(f.Id), programmed.Contains(f.Id), conflicts.Contains(f.Id)))
            .ToList();

        var groups = show.Groups.Values
            .Select(g => new GroupInfo(
                g.Number, g.Label, g.FixtureIds.Count, SelectionFormatter.Format(g.FixtureIds),
                g.FixtureIds.Count > 0 && selection.SetEquals(g.FixtureIds)))
            .ToList();

        var cuelists = show.Cuelists.Values.Select(l => CuelistOf(l)).ToList();

        var universeNumbers = show.Universes.Keys.Concat(show.Patch.Fixtures.Select(f => f.Universe)).Distinct().Order().ToList();
        var universes = universeNumbers.Select(number =>
        {
            var config = show.Universes.GetValueOrDefault(number) ?? new UniverseConfig(number);
            var inside = show.Patch.Fixtures.Where(f => f.Universe == number).OrderBy(f => f.Address).ToList();
            var blocks = inside
                .Select(f => new AddressBlock(f.Id, f.Label, f.Address, f.EndAddress, conflicts.Contains(f.Id)))
                .ToList();
            return new UniverseInfo(
                number, config.Label, config.Enabled, config.Destination, config.UnicastAddress,
                inside.Count, CountChannels(inside), blocks);
        }).ToList();

        var profiles = show.AllTypes
            .Select(t => new ProfileInfo(t, show.Patch.Fixtures.Count(f => ReferenceEquals(f.Type, t))))
            .ToList();

        var attributes = SelectedAttributes(show, programmer, resolved);
        var (columns, rows) = ProgrammerTable(show, programmer, resolved, selection, programmed);

        return new ConsoleSnapshot(
            fixtures, groups, cuelists, universes, profiles, attributes, columns, rows,
            selection, SelectionFormatter.Format(programmer.Selection), programmer.Values.Count,
            output.Blackout, output.GrandMaster);
    }

    private static int CountChannels(List<Fixture> fixtures)
    {
        var used = new bool[513];
        foreach (var fixture in fixtures)
        {
            for (var channel = fixture.Address; channel <= Math.Min(fixture.EndAddress, 512); channel++)
                used[channel] = true;
        }

        return used.Count(u => u);
    }

    private static CuelistInfo CuelistOf(Cuelist list)
    {
        var cues = list.Cues
            .Select(c => new CueInfo(
                c.Number, c.Number.ToString("0.###", CultureInfo.InvariantCulture), c.Label, c.Values.Count,
                list.CurrentNumber == c.Number))
            .ToList();
        var current = cues.FirstOrDefault(c => c.IsCurrent);
        var text = current is null ? "Off" : $"Cue {current.NumberText}{(current.Label.Length > 0 ? " " + current.Label : "")}";
        return new CuelistInfo(list.Number, list.Label, list.IsActive, text, cues);
    }

    private static FixtureInfo FixtureOf(Fixture fixture, IReadOnlyDictionary<ValueKey, double> resolved, bool selected, bool programmed, bool conflict)
    {
        double Get(FixtureAttribute a) => FrameRenderer.ValueOf(fixture, a, resolved);
        bool Has(FixtureAttribute a) => fixture.Type.HasChannel(a);

        var intensity = fixture.Type.Supports(FixtureAttribute.Dimmer) ? Get(FixtureAttribute.Dimmer) : (double?)null;

        // Colour of the light at full brightness, so a closed fixture still shows what colour it is set to.
        double r = 1, g = 1, b = 1;
        var anyColor = false;
        if (Has(FixtureAttribute.Red) || Has(FixtureAttribute.Green) || Has(FixtureAttribute.Blue))
        {
            anyColor = true;
            r = Has(FixtureAttribute.Red) ? Get(FixtureAttribute.Red) : 0;
            g = Has(FixtureAttribute.Green) ? Get(FixtureAttribute.Green) : 0;
            b = Has(FixtureAttribute.Blue) ? Get(FixtureAttribute.Blue) : 0;
        }
        else if (Has(FixtureAttribute.Cyan) || Has(FixtureAttribute.Magenta) || Has(FixtureAttribute.Yellow))
        {
            anyColor = true;
            r = 1 - (Has(FixtureAttribute.Cyan) ? Get(FixtureAttribute.Cyan) : 0);
            g = 1 - (Has(FixtureAttribute.Magenta) ? Get(FixtureAttribute.Magenta) : 0);
            b = 1 - (Has(FixtureAttribute.Yellow) ? Get(FixtureAttribute.Yellow) : 0);
        }

        if (Has(FixtureAttribute.White))
        {
            var w = Get(FixtureAttribute.White);
            (r, g, b) = (Math.Min(1, r + w), Math.Min(1, g + w), Math.Min(1, b + w));
            anyColor = true;
        }

        if (Has(FixtureAttribute.Amber))
        {
            var a = Get(FixtureAttribute.Amber);
            r = Math.Min(1, r + a);
            g = Math.Min(1, g + a * 0.7);
            anyColor = true;
        }

        var wheel = fixture.Type.Find(FixtureAttribute.ColorWheel);
        if (!anyColor && wheel is not null && ColorPresets.Find(wheel.FunctionAt(Get(FixtureAttribute.ColorWheel))?.Name ?? "") is { } preset)
            (r, g, b) = (preset.Red, preset.Green, preset.Blue);

        // A fixture whose output is closed is drawn dim; the tile still shows the colour it will have.
        var level = intensity ?? 1;
        var shown = 0.18 + 0.82 * level;
        byte Channel(double v) => (byte)Math.Round(Math.Clamp(v * shown, 0, 1) * 255);

        var detail = Detail(fixture, resolved);
        return new FixtureInfo(
            fixture.Id, fixture.Label, fixture.Type.Name, fixture.Universe, fixture.Address, fixture.EndAddress,
            fixture.Type.Footprint, selected, programmed, conflict, intensity, Channel(r), Channel(g), Channel(b), detail);
    }

    /// <summary>Pan/tilt and the like as short text on the tile.</summary>
    private static string Detail(Fixture fixture, IReadOnlyDictionary<ValueKey, double> resolved)
    {
        if (fixture.Type.HasChannel(FixtureAttribute.Pan) && fixture.Type.HasChannel(FixtureAttribute.Tilt))
        {
            var pan = FrameRenderer.ValueOf(fixture, FixtureAttribute.Pan, resolved);
            var tilt = FrameRenderer.ValueOf(fixture, FixtureAttribute.Tilt, resolved);
            return $"P{Math.Round(pan * 100):0} T{Math.Round(tilt * 100):0}";
        }

        return "";
    }

    private static List<SelectedAttribute> SelectedAttributes(Show show, Programmer programmer, IReadOnlyDictionary<ValueKey, double> resolved)
    {
        var selected = programmer.Selection.Select(show.Patch.Find).OfType<Fixture>().ToList();
        var result = new List<SelectedAttribute>();

        foreach (var attribute in selected.SelectMany(f => f.Type.Attributes).Distinct().Order())
        {
            var owners = selected.Where(f => f.Type.Supports(attribute)).ToList();
            var values = owners.Select(f => FrameRenderer.ValueOf(f, attribute, resolved)).ToList();
            var mixed = values.Max() - values.Min() > 0.0005;
            var isProgrammed = owners.Any(f => programmer.Values.ContainsKey(new ValueKey(f.Id, attribute)));

            // Functions are offered when every selected fixture that has the attribute names the same ones.
            var channels = owners.Select(f => f.Type.Find(attribute)).OfType<ChannelDefinition>().ToList();
            IReadOnlyList<ChannelFunction> functions = [];
            if (channels.Count == owners.Count && channels.Count > 0 && channels[0].FunctionList.Count > 0
                && channels.All(c => c.FunctionList.SequenceEqual(channels[0].FunctionList)))
            {
                functions = channels[0].FunctionList;
            }

            var active = !mixed && functions.Count > 0 ? channels[0].FunctionAt(values[0])?.Name : null;
            result.Add(new SelectedAttribute(attribute, mixed ? null : values[0], mixed, isProgrammed, functions, active));
        }

        return result;
    }

    private static (List<FixtureAttribute>, List<ProgrammerRow>) ProgrammerTable(
        Show show, Programmer programmer, IReadOnlyDictionary<ValueKey, double> resolved, HashSet<int> selection, HashSet<int> programmed)
    {
        var shown = show.Patch.Fixtures.Where(f => selection.Contains(f.Id) || programmed.Contains(f.Id)).ToList();
        var columns = shown.SelectMany(f => f.Type.Attributes).Distinct().Order().ToList();

        var rows = shown.Select(f => new ProgrammerRow(
            f.Id,
            f.Label,
            columns.Select(attribute =>
            {
                if (!f.Type.Supports(attribute))
                    return new ProgrammerCell("", false);

                var value = FrameRenderer.ValueOf(f, attribute, resolved);
                var function = f.Type.Find(attribute)?.FunctionAt(value)?.Name;
                var text = function is not null ? function : $"{Math.Round(value * 100):0}";
                return new ProgrammerCell(text, programmer.Values.ContainsKey(new ValueKey(f.Id, attribute)));
            }).ToList())).ToList();

        return (columns, rows);
    }
}
