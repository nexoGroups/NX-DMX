using System.Net;
using NxDmx.Core.Dmx;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;

namespace NxDmx.Core.Engine;

/// <summary>Turns the show state into DMX: playbacks first, then the programmer on top, then the output controls.</summary>
public static class FrameRenderer
{
    /// <summary>
    /// Merged attribute values from all active cuelists and the programmer. Intensity is highest-takes-precedence
    /// between cuelists, everything else latest-takes-precedence (higher cuelist number wins). Only attributes
    /// that something set appear in the result.
    /// </summary>
    public static Dictionary<ValueKey, double> ResolveValues(Show show, Programmer programmer, OutputState? output = null)
    {
        var resolved = new Dictionary<ValueKey, double>();

        foreach (var cuelist in show.Cuelists.Values)
        {
            var scale = CuelistScale(show, cuelist.Number, output);
            foreach (var (key, tracked) in cuelist.Tracked())
            {
                // A cuelist fader scales the intensity of its cuelist; colours and positions are not scaled.
                var value = key.Attribute.IsHtp() ? tracked * scale : tracked;
                resolved[key] = key.Attribute.IsHtp() && resolved.TryGetValue(key, out var existing)
                    ? Math.Max(existing, value)
                    : value;
            }
        }

        foreach (var (key, value) in programmer.Values)
            resolved[key] = value;

        return resolved;
    }

    /// <summary>Default position of an assigned fader: a cuelist starts at 0 (off), a group submaster at full.</summary>
    public static double DefaultLevel(FaderKind kind) => kind == FaderKind.Group ? 1 : 0;

    /// <summary>The level of the fader that controls the cuelist (the highest one if several do), or 1 when none does.</summary>
    private static double CuelistScale(Show show, int cuelist, OutputState? output)
    {
        if (output is null)
            return 1;

        var levels = show.Faders.Values
            .Where(f => f.Kind == FaderKind.Cuelist && f.Target == cuelist)
            .Select(f => output.FaderLevel(f.Number, DefaultLevel(FaderKind.Cuelist)))
            .ToList();
        return levels.Count == 0 ? 1 : levels.Max();
    }

    /// <summary>For every fixture in a group with a fader: the product of those fader levels.</summary>
    private static Dictionary<int, double> GroupFactors(Show show, OutputState? output)
    {
        var factors = new Dictionary<int, double>();
        if (output is null)
            return factors;

        foreach (var fader in show.Faders.Values.Where(f => f.Kind == FaderKind.Group))
        {
            if (!show.Groups.TryGetValue(fader.Target, out var group))
                continue;

            var level = output.FaderLevel(fader.Number, DefaultLevel(FaderKind.Group));
            foreach (var id in group.FixtureIds)
                factors[id] = factors.GetValueOrDefault(id, 1) * level;
        }

        return factors;
    }

    /// <summary>Value of one attribute, falling back to the fixture type's default.</summary>
    public static double ValueOf(Fixture fixture, FixtureAttribute attribute, IReadOnlyDictionary<ValueKey, double> resolved)
    {
        if (resolved.TryGetValue(new ValueKey(fixture.Id, attribute), out var value))
            return value;

        return fixture.Type.Channels.FirstOrDefault(c => c.Attribute == attribute)?.Default ?? 0;
    }

    public static DmxFrame Render(Show show, Programmer programmer, OutputState? output = null)
    {
        var resolved = ResolveValues(show, programmer, output);
        var groupFactors = GroupFactors(show, output);
        var factor = output?.IntensityFactor ?? 1;
        var frame = new DmxFrame();

        // Defined universes are always part of the frame, even without fixtures: a node expects to keep receiving data.
        foreach (var universe in show.Universes.Values)
        {
            frame.GetOrAdd(universe.Number);
            frame.SetRoute(universe.Number, RouteOf(universe));
        }

        foreach (var fixture in show.Patch.Fixtures)
        {
            var data = frame.GetOrAdd(fixture.Universe);
            var intensity = factor * groupFactors.GetValueOrDefault(fixture.Id, 1);
            var dimmer = fixture.Type.HasVirtualDimmer ? ValueOf(fixture, FixtureAttribute.Dimmer, resolved) * intensity : 1;

            foreach (var channel in fixture.Type.Channels)
            {
                var value = ValueOf(fixture, channel.Attribute, resolved);
                if (channel.Attribute.IsColor())
                    value *= dimmer;
                else if (channel.Attribute == FixtureAttribute.Dimmer)
                    value *= intensity;

                Write(data, fixture.Address, channel, Math.Clamp(value, 0, 1));
            }
        }

        return frame;
    }

    private static UniverseRoute RouteOf(UniverseConfig universe)
    {
        IPAddress? unicast = null;
        var usable = true;
        if (universe.Destination == UniverseDestination.Unicast)
        {
            // A unicast universe without a valid address would silently go to the wrong place; it sends nothing instead.
            usable = UniverseConfig.TryParseAddress(universe.UnicastAddress, out var address);
            unicast = usable ? address : null;
        }

        return new UniverseRoute(universe.Enabled && usable, unicast);
    }

    private static void Write(byte[] data, int address, ChannelDefinition channel, double value)
    {
        var coarse = address - 1 + channel.Offset - 1;

        if (channel.FineOffset is { } fineOffset)
        {
            var fine = address - 1 + fineOffset - 1;
            if (coarse >= data.Length || fine >= data.Length)
                return;

            var sixteenBit = (int)Math.Round(value * 65535, MidpointRounding.AwayFromZero);
            data[coarse] = (byte)(sixteenBit >> 8);
            data[fine] = (byte)(sixteenBit & 0xFF);
        }
        else if (coarse < data.Length)
        {
            data[coarse] = (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
        }
    }
}
