using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using NxDmx.Core.ArtNet;
using NxDmx.Core.Dmx;
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
    private const int CurrentVersion = 2;

    /// <summary>Share of the progress of reading a show that goes to parsing the text; the rest is building the show.</summary>
    private const double ParseShare = 0.6;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // ---- file layout -----------------------------------------------------------------------------------------

    private sealed class FileData
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = CurrentVersion;
        public List<UniverseData> Universes { get; set; } = [];
        public List<ProfileFile.ProfileData> Profiles { get; set; } = [];
        public List<FixtureData> Fixtures { get; set; } = [];
        public List<GroupData> Groups { get; set; } = [];
        public List<CuelistData> Cuelists { get; set; } = [];
        public List<FaderData> Faders { get; set; } = [];
        public List<ColorFile.ColorData> Colors { get; set; } = [];
    }

    private sealed class UniverseData
    {
        public int Number { get; set; }
        public string Label { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string Destination { get; set; } = nameof(UniverseDestination.Broadcast);
        public string UnicastAddress { get; set; } = "";
    }

    private sealed class FaderData
    {
        public int Number { get; set; }
        public string Kind { get; set; } = "";
        public int Target { get; set; }
        public string Label { get; set; } = "";
        public string Color { get; set; } = "";
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

    /// <summary>
    /// The show as text. <paramref name="progress"/> reports 0 to 1 as items are written; every universe, fixture, group and
    /// colour counts one, a cue counts one plus the values it holds, so the figure follows the work that is really left.
    /// </summary>
    public static string Serialize(Show show, IProgress<double>? progress = null)
    {
        // Every universe a fixture is patched in is part of the show, even when nobody configured it yet.
        var universes = show.Universes.Values
            .Concat(show.Patch.Fixtures.Select(f => f.Universe).Distinct()
                .Where(number => !show.Universes.ContainsKey(number))
                .Select(number => new UniverseConfig(number)))
            .OrderBy(u => u.Number)
            .ToList();
        var profiles = show.CustomTypes.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var fixtures = show.Patch.Fixtures.ToList();
        var groups = show.Groups.Values.ToList();
        var cuelists = show.Cuelists.Values.ToList();
        var faders = show.Faders.Values.ToList();
        var colors = ColorFile.ToData(show.CustomColors.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase));

        var total = universes.Count + profiles.Count + fixtures.Count + groups.Count + faders.Count + colors.Count
            + cuelists.Sum(l => 1 + l.Cues.Sum(c => 1 + c.Values.Count));
        var meter = new ProgressMeter(progress, total);

        // The order of the properties is the one of FileData.
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(FileData.Format), FormatName);
            writer.WriteNumber(nameof(FileData.Version), CurrentVersion);

            WriteList(writer, nameof(FileData.Universes), universes.Select(u => new UniverseData
            {
                Number = u.Number,
                Label = u.Label,
                Enabled = u.Enabled,
                Destination = u.Destination.ToString(),
                UnicastAddress = u.UnicastAddress,
            }), meter);
            WriteList(writer, nameof(FileData.Profiles), profiles.Select(ProfileFile.ToData), meter);
            WriteList(writer, nameof(FileData.Fixtures), fixtures.Select(f => new FixtureData
            {
                Id = f.Id, Label = f.Label, Type = f.Type.Name, Universe = f.Universe, Address = f.Address,
            }), meter);
            WriteList(writer, nameof(FileData.Groups), groups.Select(g => new GroupData { Number = g.Number, Label = g.Label, Fixtures = [.. g.FixtureIds] }), meter);

            writer.WritePropertyName(nameof(FileData.Cuelists));
            writer.WriteStartArray();
            foreach (var list in cuelists)
            {
                writer.WriteStartObject();
                writer.WriteNumber(nameof(CuelistData.Number), list.Number);
                writer.WriteString(nameof(CuelistData.Label), list.Label);
                writer.WritePropertyName(nameof(CuelistData.Cues));
                writer.WriteStartArray();
                foreach (var cue in list.Cues)
                {
                    JsonSerializer.Serialize(writer, new CueData
                    {
                        Number = cue.Number,
                        Label = cue.Label,
                        Values = cue.Values
                            .OrderBy(v => v.Key.FixtureId).ThenBy(v => v.Key.Attribute)
                            .Select(v => new ValueData { Fixture = v.Key.FixtureId, Attribute = v.Key.Attribute.ToString(), Value = v.Value })
                            .ToList(),
                    }, Options);
                    meter.Add(1 + cue.Values.Count);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                meter.Add(1);
            }

            writer.WriteEndArray();

            WriteList(writer, nameof(FileData.Faders), faders.Select(x => new FaderData
            {
                Number = x.Number, Kind = x.Kind.ToString(), Target = x.Target, Label = x.Label, Color = x.Color,
            }), meter);
            WriteList(writer, nameof(FileData.Colors), colors, meter);
            writer.WriteEndObject();
        }

        progress?.Report(1);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteList<T>(Utf8JsonWriter writer, string name, IEnumerable<T> items, ProgressMeter meter)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var item in items)
        {
            JsonSerializer.Serialize(writer, item, Options);
            meter.Add(1);
        }

        writer.WriteEndArray();
    }

    // ---- reading ---------------------------------------------------------------------------------------------

    /// <summary>Builds a new show from the text. Nothing is applied anywhere unless the whole file is valid.</summary>
    public static Show Deserialize(string json, IProgress<double>? progress = null)
    {
        // Reading the text takes the first part of the progress (it is counted in the bytes read), building the show the rest.
        FileData? data;
        try
        {
            using var stream = new ProgressStream(new MemoryStream(Encoding.UTF8.GetBytes(json)), progress is null ? null : new ScaledProgress(progress, 0, ParseShare));
            data = JsonSerializer.Deserialize<FileData>(stream, Options);
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
        var meter = new ProgressMeter(progress is null ? null : new ScaledProgress(progress, ParseShare, 1),
            data.Profiles.Count + data.Universes.Count + data.Fixtures.Count + data.Groups.Count + (data.Faders?.Count ?? 0) + (data.Colors?.Count ?? 0)
            + data.Cuelists.Sum(l => 1 + l.Cues.Sum(c => 1 + c.Values.Count)));

        foreach (var p in data.Profiles)
        {
            meter.Add(1);
            var profile = ProfileFile.FromData(p);
            if (FixtureTypeLibrary.Find(profile.Name) is not null || !show.CustomTypes.TryAdd(profile.Name, profile))
                throw new ShowFileException($"The profile \"{profile.Name}\" appears twice or has the name of a built-in profile.");
        }

        foreach (var u in data.Universes)
        {
            meter.Add(1);
            if (u.Number < 1 || u.Number > ArtNetPacket.MaxUniverse || show.Universes.ContainsKey(u.Number))
                throw new ShowFileException($"Universe number {u.Number} is invalid or appears twice.");
            if (!Enum.TryParse<UniverseDestination>(u.Destination, out var destination) || !Enum.IsDefined(destination))
                throw new ShowFileException($"Universe {u.Number} has the unknown destination '{u.Destination}'.");
            if (destination == UniverseDestination.Unicast && !UniverseConfig.TryParseAddress(u.UnicastAddress, out _))
                throw new ShowFileException($"Universe {u.Number} has the invalid node address '{u.UnicastAddress}'.");

            show.Universes[u.Number] = new UniverseConfig(u.Number)
            {
                Label = u.Label,
                Enabled = u.Enabled,
                Destination = destination,
                UnicastAddress = u.UnicastAddress,
            };
        }

        foreach (var f in data.Fixtures)
        {
            meter.Add(1);
            var type = show.FindType(f.Type)
                ?? throw new ShowFileException($"Fixture {f.Id} has the unknown type '{f.Type}'.");
            if (f.Id < 1)
                throw new ShowFileException($"Fixture number {f.Id} is not valid.");
            if (show.Patch.Find(f.Id) is not null)
                throw new ShowFileException($"Fixture {f.Id} appears twice.");
            if (f.Universe < 1 || f.Universe > ArtNetPacket.MaxUniverse
                || f.Address < 1 || f.Address + type.Footprint - 1 > 512)
                throw new ShowFileException($"Fixture {f.Id} has an invalid address {f.Universe}.{f.Address}.");

            show.Patch.Add(new Fixture(f.Id, type, f.Universe, f.Address, f.Label));
            show.EnsureUniverse(f.Universe);
        }

        foreach (var g in data.Groups)
        {
            meter.Add(1);
            if (g.Number < 1 || show.Groups.ContainsKey(g.Number))
                throw new ShowFileException($"Group number {g.Number} is invalid or appears twice.");

            show.Groups[g.Number] = new Group(g.Number, g.Label, [.. g.Fixtures.Distinct()]);
        }

        foreach (var l in data.Cuelists)
        {
            meter.Add(1);
            if (l.Number < 1 || show.Cuelists.ContainsKey(l.Number))
                throw new ShowFileException($"Cuelist number {l.Number} is invalid or appears twice.");

            var cuelist = new Cuelist(l.Number) { Label = l.Label };
            foreach (var c in l.Cues)
            {
                meter.Add(1 + c.Values.Count);
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

        foreach (var x in data.Faders ?? [])
        {
            meter.Add(1);
            if (x.Number is < 1 or > FaderConfig.MaxFaders || show.Faders.ContainsKey(x.Number))
                throw new ShowFileException($"Fader number {x.Number} is invalid or appears twice.");
            if (!Enum.TryParse<FaderKind>(x.Kind, out var kind) || !Enum.IsDefined(kind))
                throw new ShowFileException($"Fader {x.Number} has the unknown kind '{x.Kind}'.");
            if (x.Color is { Length: > 0 } && !ColorPreset.TryParseHex(x.Color, out _, out _, out _))
                throw new ShowFileException($"Fader {x.Number} has the invalid colour '{x.Color}'.");

            if (kind != FaderKind.None)
                show.Faders[x.Number] = new FaderConfig(x.Number) { Kind = kind, Target = x.Target, Label = x.Label ?? "", Color = x.Color ?? "" };
        }

        foreach (var c in data.Colors ?? [])
        {
            meter.Add(1);
            var color = ColorPreset.FromHex(c.Name ?? "", c.Hex ?? "");
            if (color is null || color.Name.Length is 0 or > 40 || color.Name.Contains('"'))
                throw new ShowFileException($"The colour \"{c.Name}\" ('{c.Hex}') is not valid.");

            show.CustomColors[color.Name] = color;
        }

        progress?.Report(1);
        return show;
    }
}
