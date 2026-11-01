namespace NxDmx.Core.Fixtures;

/// <summary>
/// One DMX channel, or a coarse/fine pair, of a fixture. Offsets are 1-based inside the fixture's footprint.
/// <paramref name="Default"/> is the normalised value (0..1) used while nothing sets the attribute.
/// </summary>
public sealed record ChannelDefinition(FixtureAttribute Attribute, int Offset, int? FineOffset = null, double Default = 0);

public sealed class FixtureType
{
    public FixtureType(string name, IReadOnlyList<ChannelDefinition> channels)
    {
        Name = name;
        Channels = channels;
        Footprint = channels.Max(c => Math.Max(c.Offset, c.FineOffset ?? 0));
    }

    public string Name { get; }

    public IReadOnlyList<ChannelDefinition> Channels { get; }

    /// <summary>Number of consecutive DMX channels the fixture occupies.</summary>
    public int Footprint { get; }

    public bool HasChannel(FixtureAttribute attribute) => Channels.Any(c => c.Attribute == attribute);

    /// <summary>Fixtures with colour but no dimmer channel get an intensity master that scales their emitters.</summary>
    public bool HasVirtualDimmer => !HasChannel(FixtureAttribute.Dimmer) && Channels.Any(c => c.Attribute.IsColor());

    public bool Supports(FixtureAttribute attribute) =>
        HasChannel(attribute) || (attribute == FixtureAttribute.Dimmer && HasVirtualDimmer);
}
