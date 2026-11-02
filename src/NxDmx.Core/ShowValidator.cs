using NxDmx.Core.ArtNet;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;

namespace NxDmx.Core;

/// <summary>One thing wrong in a show. The <see cref="Key"/> names the problem, so two checks can tell which problems are new.</summary>
public sealed record ShowIssue(string Key, string Message);

/// <summary>
/// The rules a show must always satisfy: fixtures inside their universe and not on top of each other, faders pointing at
/// something that exists, a selection of fixtures that are patched. After every change the console checks these again;
/// a change that makes a rule fail is taken back (see <see cref="Commands.CommandInterpreter"/>).
/// </summary>
public static class ShowValidator
{
    public static IReadOnlyList<ShowIssue> Check(Show show, Programmer? programmer = null)
    {
        var issues = new List<ShowIssue>();

        foreach (var fixture in show.Patch.Fixtures)
        {
            if (fixture.Universe < 1 || fixture.Universe > ArtNetPacket.MaxUniverse
                || fixture.Address < 1 || fixture.EndAddress > 512)
            {
                issues.Add(new ShowIssue($"range:{fixture.Id}",
                    $"Fixture {fixture.Id} at {fixture.Universe}.{fixture.Address} does not fit into a universe of 512 channels"));
            }
        }

        foreach (var id in show.Patch.FindConflicts())
            issues.Add(new ShowIssue($"overlap:{id}", $"Fixture {id} shares DMX channels with another fixture"));

        foreach (var fader in show.Faders.Values)
        {
            if (fader.Kind == FaderKind.Cuelist && !show.Cuelists.ContainsKey(fader.Target))
                issues.Add(new ShowIssue($"fader:{fader.Number}", $"Fader {fader.Number} points at Cuelist {fader.Target}, which does not exist"));
            else if (fader.Kind == FaderKind.Group && !show.Groups.ContainsKey(fader.Target))
                issues.Add(new ShowIssue($"fader:{fader.Number}", $"Fader {fader.Number} points at Group {fader.Target}, which does not exist"));
        }

        if (programmer is not null)
        {
            foreach (var id in programmer.Selection.Where(id => show.Patch.Find(id) is null))
                issues.Add(new ShowIssue($"selection:{id}", $"Fixture {id} is selected but not patched"));
        }

        return issues;
    }
}
