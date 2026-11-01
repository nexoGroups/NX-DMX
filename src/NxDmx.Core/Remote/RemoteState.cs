using System.Globalization;
using System.Text;
using System.Text.Json;
using NxDmx.Core.Commands;

namespace NxDmx.Core.Remote;

/// <summary>What a remote client can read: the console state as flat variables and as JSON.</summary>
public static class RemoteState
{
    /// <summary>
    /// Flat name/value pairs, easy to use as variables in Bitfocus Companion: <c>master</c>, <c>blackout</c>,
    /// <c>selection</c>, <c>cuelist.1.cue</c>, <c>cuelist.1.active</c>, <c>group.2.label</c>, ...
    /// </summary>
    public static SortedDictionary<string, string> Variables(LightingConsole console) =>
        console.Read((show, programmer, output) =>
        {
            var vars = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["blackout"] = output.Blackout ? "1" : "0",
                ["master"] = Percent(output.GrandMaster),
                ["selection"] = SelectionFormatter.Format(programmer.Selection),
                ["selection_count"] = programmer.Selection.Count.ToString(CultureInfo.InvariantCulture),
                ["programmer_values"] = programmer.Values.Count.ToString(CultureInfo.InvariantCulture),
                ["fixtures"] = show.Patch.Count.ToString(CultureInfo.InvariantCulture),
                ["universes"] = show.Universes.Count.ToString(CultureInfo.InvariantCulture),
            };

            foreach (var cuelist in show.Cuelists.Values)
            {
                var current = cuelist.CurrentNumber is { } number ? cuelist.Find(number) : null;
                var prefix = $"cuelist.{cuelist.Number}.";
                vars[prefix + "label"] = cuelist.Label;
                vars[prefix + "active"] = cuelist.IsActive ? "1" : "0";
                vars[prefix + "cue"] = current is null ? "" : Number(current.Number);
                vars[prefix + "cue_label"] = current?.Label ?? "";
                vars[prefix + "cues"] = cuelist.Cues.Count().ToString(CultureInfo.InvariantCulture);
            }

            foreach (var group in show.Groups.Values)
            {
                vars[$"group.{group.Number}.label"] = group.Label;
                vars[$"group.{group.Number}.count"] = group.FixtureIds.Count.ToString(CultureInfo.InvariantCulture);
            }

            foreach (var universe in show.Universes.Values)
            {
                vars[$"universe.{universe.Number}.label"] = universe.Label;
                vars[$"universe.{universe.Number}.enabled"] = universe.Enabled ? "1" : "0";
            }

            return vars;
        });

    /// <summary>The same information as nested JSON, for clients that can read it.</summary>
    public static string Json(LightingConsole console)
    {
        var text = console.Read((show, programmer, output) =>
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteBoolean("blackout", output.Blackout);
                json.WriteNumber("master", Math.Round(output.GrandMaster * 100, 2));
                json.WriteString("selection", SelectionFormatter.Format(programmer.Selection));
                json.WriteNumber("selectionCount", programmer.Selection.Count);
                json.WriteNumber("programmerValues", programmer.Values.Count);
                json.WriteNumber("fixtures", show.Patch.Count);

                json.WriteStartArray("cuelists");
                foreach (var cuelist in show.Cuelists.Values)
                {
                    var current = cuelist.CurrentNumber is { } number ? cuelist.Find(number) : null;
                    json.WriteStartObject();
                    json.WriteNumber("number", cuelist.Number);
                    json.WriteString("label", cuelist.Label);
                    json.WriteBoolean("active", cuelist.IsActive);
                    if (current is null)
                        json.WriteNull("cue");
                    else
                        json.WriteString("cue", Number(current.Number));
                    json.WriteString("cueLabel", current?.Label ?? "");
                    json.WriteNumber("cues", cuelist.Cues.Count());
                    json.WriteEndObject();
                }

                json.WriteEndArray();

                json.WriteStartArray("groups");
                foreach (var group in show.Groups.Values)
                {
                    json.WriteStartObject();
                    json.WriteNumber("number", group.Number);
                    json.WriteString("label", group.Label);
                    json.WriteNumber("fixtures", group.FixtureIds.Count);
                    json.WriteEndObject();
                }

                json.WriteEndArray();

                json.WriteStartArray("universes");
                foreach (var universe in show.Universes.Values)
                {
                    json.WriteStartObject();
                    json.WriteNumber("number", universe.Number);
                    json.WriteString("label", universe.Label);
                    json.WriteBoolean("enabled", universe.Enabled);
                    json.WriteString("destination", universe.Destination.ToString());
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        });

        return text;
    }

    private static string Percent(double value) => Math.Round(value * 100).ToString(CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
