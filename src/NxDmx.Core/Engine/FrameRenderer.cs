using NxDmx.Core.Dmx;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Programming;

namespace NxDmx.Core.Engine;

/// <summary>Turns the show state into DMX: playbacks first, then the programmer on top.</summary>
public static class FrameRenderer
{
    /// <summary>
    /// Merged attribute values from all active cuelists and the programmer. Intensity is highest-takes-precedence
    /// between cuelists, everything else latest-takes-precedence (higher cuelist number wins). Only attributes
    /// that something set appear in the result.
    /// </summary>
    public static Dictionary<ValueKey, double> ResolveValues(Show show, Programmer programmer)
    {
        var resolved = new Dictionary<ValueKey, double>();

        foreach (var cuelist in show.Cuelists.Values)
        {
            foreach (var (key, value) in cuelist.Tracked())
            {
                resolved[key] = key.Attribute.IsHtp() && resolved.TryGetValue(key, out var existing)
                    ? Math.Max(existing, value)
                    : value;
            }
        }

        foreach (var (key, value) in programmer.Values)
            resolved[key] = value;

        return resolved;
    }

    /// <summary>Value of one attribute, falling back to the fixture type's default.</summary>
    public static double ValueOf(Fixture fixture, FixtureAttribute attribute, IReadOnlyDictionary<ValueKey, double> resolved)
    {
        if (resolved.TryGetValue(new ValueKey(fixture.Id, attribute), out var value))
            return value;

        return fixture.Type.Channels.FirstOrDefault(c => c.Attribute == attribute)?.Default ?? 0;
    }

    public static DmxFrame Render(Show show, Programmer programmer)
    {
        var resolved = ResolveValues(show, programmer);
        var frame = new DmxFrame();

        foreach (var fixture in show.Patch.Fixtures)
        {
            var data = frame.GetOrAdd(fixture.Universe);
            var dimmer = fixture.Type.HasVirtualDimmer ? ValueOf(fixture, FixtureAttribute.Dimmer, resolved) : 1;

            foreach (var channel in fixture.Type.Channels)
            {
                var value = ValueOf(fixture, channel.Attribute, resolved);
                if (channel.Attribute.IsColor())
                    value *= dimmer;

                Write(data, fixture.Address, channel, Math.Clamp(value, 0, 1));
            }
        }

        return frame;
    }

    private static void Write(byte[] data, int address, ChannelDefinition channel, double value)
    {
        var coarse = address - 1 + channel.Offset - 1;

        if (channel.FineOffset is { } fineOffset)
        {
            var sixteenBit = (int)Math.Round(value * 65535, MidpointRounding.AwayFromZero);
            data[coarse] = (byte)(sixteenBit >> 8);
            data[address - 1 + fineOffset - 1] = (byte)(sixteenBit & 0xFF);
        }
        else
        {
            data[coarse] = (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
        }
    }
}
