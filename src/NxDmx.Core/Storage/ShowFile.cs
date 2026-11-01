using System.Text.Json;
using NxDmx.Core.ArtNet;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;

namespace NxDmx.Core.Storage;

/// <summary>The text is not a show this version can read. The message is meant for the operator.</summary>
public sealed class ShowFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads and writes a show (patch, groups, cuelists) as JSON. The programmer and the live cuelist positions are not
/// part of a show: after loading, everything is off and the programmer is empty. The text is stable for an unchanged
/// show, so comparing two texts tells whether anything changed.
/// </summary>
public static class ShowFile
{
    public const string Extension = ".nxshow";

    private const string FormatName = "nx-dmx-show";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // ---- file layout -----------------------------------------------------------------------------------------

    private sealed class FileData
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = CurrentVersion;
        public List<FixtureData> Fixtures { get; set; } = [];
        public List<GroupData> Groups { get; set; } = [];
        public List<CuelistData> Cuelists { get; set; } = [];
    }

    private sealed class FixtureData
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public string Type { get; set; } = "";
        public int Universe { get; set; }
        public int Address { get; set; }
    }

    private sealed class GroupData
    {
        public int Number { get; set; }
        public string Label { get; set; } = "";
        public List<int> Fixtures { get; set; } = [];
    }

    private sealed class CuelistData
    {
        public int Number { get; set; }
        public string Label { get; set; } = "";
        public List<CueData> Cues { get; set; } = [];
    }

    private sealed class CueData
    {
        public decimal Number { get; set; }
        public string Label { get; set; } = "";
        public List<ValueData> Values { get; set; } = [];
    }

    private sealed class ValueData
    {
        public int Fixture { get; set; }
        public string Attribute { get; set; } = "";
        public double Value { get; set; }
    }

    // ---- writing ---------------------------------------------------------------------------------------------

    public static string Serialize(Show show)
    {
        var data = new FileData
        {
            Fixtures = show.Patch.Fixtures
                .Select(f => new FixtureData { Id = f.Id, Label = f.Label, Type = f.Type.Name, Universe = f.Universe, Address = f.Address })
                .ToList(),
            Groups = show.Groups.Values
                .Select(g => new GroupData { Number = g.Number, Label = g.Label, Fixtures = [.. g.FixtureIds] })
                .ToList(),
            Cuelists = show.Cuelists.Values
                .Select(l => new CuelistData
                {
                    Number = l.Number,
                    Label = l.Label,
                    Cues = l.Cues
                        .Select(c => new CueData
                        {
                            Number = c.Number,
                            Label = c.Label,
                            Values = c.Values
                                .OrderBy(v => v.Key.FixtureId).ThenBy(v => v.Key.Attribute)
                                .Select(v => new ValueData { Fixture = v.Key.FixtureId, Attribute = v.Key.Attribute.ToString(), Value = v.Value })
                                .ToList(),
                        })
                        .ToList(),
                })
                .ToList(),
        };

        return JsonSerializer.Serialize(data, Options);
    }

    // ---- reading ---------------------------------------------------------------------------------------------

    /// <summary>Builds a new show from the text. Nothing is applied anywhere unless the whole file is valid.</summary>
    public static Show Deserialize(string json)
    {
        FileData? data;
        try
        {
            data = JsonSerializer.Deserialize<FileData>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ShowFileException("This is not a readable NX-DMX show file.", ex);
        }

        if (data is null || data.Format != FormatName)
            throw new ShowFileException("This is not an NX-DMX show file.");
        if (data.Version > CurrentVersion)
            throw new ShowFileException($"This show was saved by a newer NX-DMX (file version {data.Version}). Update NX-DMX to open it.");
        if (data.Version < 1)
            throw new ShowFileException($"Unsupported show file version {data.Version}.");

        var show = new Show();

        foreach (var f in data.Fixtures)
        {
            var type = FixtureTypeLibrary.Find(f.Type)
                ?? throw new ShowFileException($"Fixture {f.Id} has the unknown type '{f.Type}'.");
            if (f.Id < 1)
                throw new ShowFileException($"Fixture number {f.Id} is not valid.");
            if (show.Patch.Find(f.Id) is not null)
                throw new ShowFileException($"Fixture {f.Id} appears twice.");
            if (f.Universe < 1 || f.Universe > ArtNetPacket.MaxUniverse
                || f.Address < 1 || f.Address + type.Footprint - 1 > 512)
                throw new ShowFileException($"Fixture {f.Id} has an invalid address {f.Universe}.{f.Address}.");

            show.Patch.Add(new Fixture(f.Id, type, f.Universe, f.Address, f.Label));
        }

        foreach (var g in data.Groups)
        {
            if (g.Number < 1 || show.Groups.ContainsKey(g.Number))
                throw new ShowFileException($"Group number {g.Number} is invalid or appears twice.");

            show.Groups[g.Number] = new Group(g.Number, g.Label, [.. g.Fixtures.Distinct()]);
        }

        foreach (var l in data.Cuelists)
        {
            if (l.Number < 1 || show.Cuelists.ContainsKey(l.Number))
                throw new ShowFileException($"Cuelist number {l.Number} is invalid or appears twice.");

            var cuelist = new Cuelist(l.Number) { Label = l.Label };
            foreach (var c in l.Cues)
            {
                if (c.Number < 0 || cuelist.Find(c.Number) is not null)
                    throw new ShowFileException($"Cue {c.Number} in Cuelist {l.Number} is invalid or appears twice.");

                var values = new Dictionary<ValueKey, double>();
                foreach (var v in c.Values)
                {
                    if (!FixtureAttributeExtensions.TryParse(v.Attribute, out var attribute))
                        throw new ShowFileException($"Cue {c.Number} in Cuelist {l.Number} has the unknown attribute '{v.Attribute}'.");
                    if (!(v.Value is >= 0 and <= 1))
                        throw new ShowFileException($"Cue {c.Number} in Cuelist {l.Number} has a value outside 0..100 %.");

                    values[new ValueKey(v.Fixture, attribute)] = v.Value;
                }

                cuelist.Set(new Cue(c.Number, c.Label, values));
            }

            show.Cuelists[l.Number] = cuelist;
        }

        return show;
    }
}
