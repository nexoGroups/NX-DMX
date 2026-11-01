using System.Globalization;
using NxDmx.Core;
using NxDmx.Core.Commands;
using NxDmx.Core.Engine;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Programming;

namespace NxDmx.App;

public sealed record FixtureRow(
    int Id,
    string Label,
    string Type,
    string Patch,
    string Dimmer,
    string Red,
    string Green,
    string Blue,
    string White,
    string Pan,
    string Tilt,
    string Programmer,
    bool InSelection);

public sealed record CueRow(string Marker, string Number, string Label, int ValueCount, bool IsCurrent);

public sealed record GroupRow(int Number, string Label, string Fixtures);

public sealed record LogEntry(string Text, bool IsEcho, bool IsError);

/// <summary>What the panels show, copied out of the console while its lock is held.</summary>
public sealed record ConsoleSnapshot(
    IReadOnlyList<FixtureRow> Fixtures,
    IReadOnlyList<CueRow> Cues,
    IReadOnlyList<GroupRow> Groups,
    string CuelistTitle,
    string Selection,
    int ProgrammerValueCount)
{
    public static ConsoleSnapshot Build(Show show, Programmer programmer, int cuelistNumber)
    {
        var resolved = FrameRenderer.ResolveValues(show, programmer);
        var selected = programmer.Selection.ToHashSet();
        var programmed = programmer.Values.Keys.Select(k => k.FixtureId).ToHashSet();

        string Percent(Fixture fixture, FixtureAttribute attribute) =>
            fixture.Type.Supports(attribute)
                ? Math.Round(FrameRenderer.ValueOf(fixture, attribute, resolved) * 100).ToString(CultureInfo.InvariantCulture)
                : "–";

        var fixtures = show.Patch.Fixtures
            .Select(f => new FixtureRow(
                f.Id,
                f.Label,
                f.Type.Name,
                $"{f.Universe}.{f.Address}",
                Percent(f, FixtureAttribute.Dimmer),
                Percent(f, FixtureAttribute.Red),
                Percent(f, FixtureAttribute.Green),
                Percent(f, FixtureAttribute.Blue),
                Percent(f, FixtureAttribute.White),
                Percent(f, FixtureAttribute.Pan),
                Percent(f, FixtureAttribute.Tilt),
                programmed.Contains(f.Id) ? "●" : "",
                selected.Contains(f.Id)))
            .ToList();

        var cuelist = show.Cuelists.GetValueOrDefault(cuelistNumber);
        var cues = cuelist is null
            ? []
            : cuelist.Cues
                .Select(c => new CueRow(
                    cuelist.CurrentNumber == c.Number ? "▶" : "",
                    c.Number.ToString("0.###", CultureInfo.InvariantCulture),
                    c.Label,
                    c.Values.Count,
                    cuelist.CurrentNumber == c.Number))
                .ToList();

        var title = cuelist is null
            ? $"Cuelist {cuelistNumber} – nothing recorded yet"
            : cuelist.Label == $"Cuelist {cuelistNumber}" ? cuelist.Label : $"Cuelist {cuelistNumber} · {cuelist.Label}";

        var groups = show.Groups.Values
            .Select(g => new GroupRow(g.Number, g.Label, SelectionFormatter.Format(g.FixtureIds)))
            .ToList();

        return new ConsoleSnapshot(fixtures, cues, groups, title, SelectionFormatter.Format(programmer.Selection), programmer.Values.Count);
    }
}
