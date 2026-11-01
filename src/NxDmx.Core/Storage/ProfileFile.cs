using System.Text.Json;
using NxDmx.Core.Fixtures;

namespace NxDmx.Core.Storage;

/// <summary>
/// A fixture profile as JSON. The same layout is used inside show files and for the stand-alone profile files
/// (<c>.nxfixture</c>) that the Fixture Manager imports and exports.
/// </summary>
public static class ProfileFile
{
    public const string Extension = ".nxfixture";

    private const string FormatName = "nx-dmx-fixture";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // ---- layout ----------------------------------------------------------------------------------------------

    internal sealed class ProfileData
    {
        public string Name { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public int ChannelCount { get; set; }
        public List<ChannelData> Channels { get; set; } = [];
    }

    internal sealed class ChannelData
    {
        public string Attribute { get; set; } = "";
        public int Offset { get; set; }
        public int? FineOffset { get; set; }
        public double Default { get; set; }
        public List<FunctionData> Functions { get; set; } = [];
    }

    internal sealed class FunctionData
    {
        public string Name { get; set; } = "";
        public int From { get; set; }
        public int To { get; set; }
    }

    private sealed class FileData
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = CurrentVersion;
        public ProfileData Profile { get; set; } = new();
    }

    // ---- conversion ------------------------------------------------------------------------------------------

    internal static ProfileData ToData(FixtureType type) => new()
    {
        Name = type.Name,
        Manufacturer = type.Manufacturer,
        ChannelCount = type.Footprint,
        Channels = type.Channels
            .OrderBy(c => c.Offset)
            .Select(c => new ChannelData
            {
                Attribute = c.Attribute.ToString(),
                Offset = c.Offset,
                FineOffset = c.FineOffset,
                Default = c.Default,
                Functions = c.FunctionList.OrderBy(f => f.From).Select(f => new FunctionData { Name = f.Name, From = f.From, To = f.To }).ToList(),
            })
            .ToList(),
    };

    /// <summary>Builds the profile, or throws a <see cref="ShowFileException"/> that says what is wrong with it.</summary>
    internal static FixtureType FromData(ProfileData data)
    {
        var channels = new List<ChannelDefinition>();
        foreach (var c in data.Channels)
        {
            if (!FixtureAttributeExtensions.TryParse(c.Attribute, out var attribute))
                throw new ShowFileException($"Profile \"{data.Name}\" has the unknown attribute '{c.Attribute}'.");

            var functions = c.Functions.Select(f => new ChannelFunction(f.Name, f.From, f.To)).ToList();
            channels.Add(new ChannelDefinition(attribute, c.Offset, c.FineOffset, c.Default, functions));
        }

        var type = new FixtureType(data.Name.Trim(), channels, data.Manufacturer.Trim(), data.ChannelCount);
        var problems = type.Validate();
        if (problems.Count > 0)
            throw new ShowFileException($"Profile \"{data.Name}\" is not valid: {string.Join(" ", problems)}");

        return type;
    }

    // ---- stand-alone files -----------------------------------------------------------------------------------

    public static string Serialize(FixtureType type) =>
        JsonSerializer.Serialize(new FileData { Profile = ToData(type) }, Options);

    public static FixtureType Deserialize(string json)
    {
        FileData? data;
        try
        {
            data = JsonSerializer.Deserialize<FileData>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ShowFileException("This is not a readable NX-DMX fixture profile.", ex);
        }

        if (data is null || data.Format != FormatName)
            throw new ShowFileException("This is not an NX-DMX fixture profile.");
        if (data.Version > CurrentVersion)
            throw new ShowFileException("This profile was saved by a newer NX-DMX. Update NX-DMX to open it.");

        return FromData(data.Profile);
    }
}
