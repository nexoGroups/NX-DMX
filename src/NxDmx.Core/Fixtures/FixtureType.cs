using NxDmx.Core.Commands;

namespace NxDmx.Core.Fixtures;

/// <summary>A named stretch of a channel's DMX range, e.g. "Gobo 3" at 40..49. Values are 0..255 on the coarse byte.</summary>
public sealed record ChannelFunction(string Name, int From, int To)
{
    public bool Contains(int dmx) => dmx >= From && dmx <= To;

    /// <summary>Normalised value (0..1) that puts the channel at the start of this function.</summary>
    public double StartValue => From / 255.0;
}

/// <summary>
/// One DMX channel, or a coarse/fine pair, of a fixture. Offsets are 1-based inside the fixture's footprint.
/// <paramref name="Default"/> is the normalised value (0..1) used while nothing sets the attribute.
/// </summary>
public sealed record ChannelDefinition(
    FixtureAttribute Attribute,
    int Offset,
    int? FineOffset = null,
    double Default = 0,
    IReadOnlyList<ChannelFunction>? Functions = null)
{
    public IReadOnlyList<ChannelFunction> FunctionList => Functions ?? [];

    /// <summary>The function the channel is in at the given value, if the profile names one.</summary>
    public ChannelFunction? FunctionAt(double value)
    {
        var dmx = (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
        return FunctionList.FirstOrDefault(f => f.Contains(dmx));
    }
}

/// <summary>
/// A fixture profile: which DMX channel does what. Built-in profiles are fixed; the operator creates the others in
/// the Fixture Manager and they are saved inside the show.
/// </summary>
public sealed class FixtureType
{
    public const int MaxChannels = 512;

    public FixtureType(string name, IReadOnlyList<ChannelDefinition> channels, string manufacturer = "",
        int? channelCount = null, bool isBuiltIn = false)
    {
        Name = name;
        Channels = channels;
        Manufacturer = manufacturer;
        IsBuiltIn = isBuiltIn;
        var highest = channels.Count == 0 ? 0 : channels.Max(c => Math.Max(c.Offset, c.FineOffset ?? 0));
        Footprint = Math.Max(highest, channelCount ?? 0);
    }

    public string Name { get; }

    public string Manufacturer { get; }

    public bool IsBuiltIn { get; }

    /// <summary>The name as shown in lists: built-in profiles are marked, so they are told apart from the operator's own.</summary>
    public string DisplayName => IsBuiltIn ? $"{Name} (PreBuild)" : Name;

    public IReadOnlyList<ChannelDefinition> Channels { get; }

    /// <summary>Number of consecutive DMX channels the fixture occupies, including channels the profile leaves unused.</summary>
    public int Footprint { get; }

    public bool HasChannel(FixtureAttribute attribute) => Channels.Any(c => c.Attribute == attribute);

    public ChannelDefinition? Find(FixtureAttribute attribute) => Channels.FirstOrDefault(c => c.Attribute == attribute);

    /// <summary>Fixtures with colour but no dimmer channel get an intensity master that scales their emitters.</summary>
    public bool HasVirtualDimmer => !HasChannel(FixtureAttribute.Dimmer) && Channels.Any(c => c.Attribute.IsColor());

    public bool Supports(FixtureAttribute attribute) =>
        HasChannel(attribute) || (attribute == FixtureAttribute.Dimmer && HasVirtualDimmer);

    /// <summary>All attributes a fixture of this type can be programmed with, in attribute order.</summary>
    public IEnumerable<FixtureAttribute> Attributes =>
        Channels.Select(c => c.Attribute)
            .Concat(HasVirtualDimmer ? [FixtureAttribute.Dimmer] : [])
            .Distinct()
            .Order();

    /// <summary>The problems that would make this profile unusable; empty when it is fine.</summary>
    public IReadOnlyList<string> Validate() => Validate(Name, Channels, Footprint);

    public static IReadOnlyList<string> Validate(string name, IReadOnlyList<ChannelDefinition> channels, int footprint)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
            problems.Add("The profile needs a name.");
        else if (name.Trim().Length > 60 || name.Any(char.IsControl))
            problems.Add("The name is too long or has invalid characters (60 characters at most).");

        if (footprint is < 1 or > MaxChannels)
            problems.Add($"The channel count must be between 1 and {MaxChannels}.");

        if (channels.Count == 0)
            problems.Add("Assign at least one channel to an attribute.");

        var usedOffsets = new Dictionary<int, string>();
        var usedAttributes = new HashSet<FixtureAttribute>();

        foreach (var channel in channels)
        {
            var label = $"{channel.Attribute} (channel {channel.Offset})";

            if (!usedAttributes.Add(channel.Attribute))
                problems.Add($"{channel.Attribute} is assigned twice. Every attribute can only be used once per profile.");
            if (channel.Offset < 1 || channel.Offset > footprint)
                problems.Add($"{label}: the channel is outside the channel count.");
            else if (!usedOffsets.TryAdd(channel.Offset, channel.Attribute.ToString()))
                problems.Add($"Channel {channel.Offset} is used by {usedOffsets[channel.Offset]} and {channel.Attribute}.");

            if (channel.FineOffset is { } fine)
            {
                if (fine < 1 || fine > footprint)
                    problems.Add($"{label}: the fine channel {fine} is outside the channel count.");
                else if (fine == channel.Offset)
                    problems.Add($"{label}: the fine channel must differ from the coarse channel.");
                else if (!usedOffsets.TryAdd(fine, channel.Attribute + " (fine)"))
                    problems.Add($"Channel {fine} is used by {usedOffsets[fine]} and {channel.Attribute} (fine).");
            }

            if (channel.Default is < 0 or > 1 || double.IsNaN(channel.Default))
                problems.Add($"{label}: the default must be between 0 and 100 %.");

            var ordered = channel.FunctionList.OrderBy(f => f.From).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var function = ordered[i];
                if (string.IsNullOrWhiteSpace(function.Name) || function.Name.Contains('"'))
                    problems.Add($"{label}: every function needs a name without quotation marks.");
                if (function.From < 0 || function.To > 255 || function.From > function.To)
                    problems.Add($"{label}: function \"{function.Name}\" needs a range inside 0..255 with From <= To.");
                else if (i > 0 && function.From <= ordered[i - 1].To)
                    problems.Add($"{label}: the functions \"{ordered[i - 1].Name}\" and \"{function.Name}\" overlap.");
            }

            if (channel.FunctionList.Select(f => f.Name.Trim().ToLowerInvariant()).Distinct().Count() != channel.FunctionList.Count)
                problems.Add($"{label}: function names must be unique within a channel.");
        }

        return problems;
    }

    /// <summary>Throws a <see cref="CommandException"/> with all problems when the profile is not valid.</summary>
    public void EnsureValid()
    {
        var problems = Validate();
        if (problems.Count > 0)
            throw new CommandException(string.Join(" ", problems));
    }
}
