using NxDmx.Core.ArtNet;
using NxDmx.Core.Dmx;
using NxDmx.Core.Fixtures;

namespace NxDmx.Core.Commands;

public sealed partial class CommandInterpreter
{
    // ---- patch -----------------------------------------------------------------------------------------------

    private sealed record Placement(int Id, FixtureType Type, int Universe, int Address, string? Label);

    /// <summary>
    /// <c>Patch Fixture 1 Thru 8 Type RGBDim At 1.1</c> creates fixtures one after the other, moving on to the next
    /// universe when one is full. For fixtures that exist already, <c>Type</c>, <c>At</c> and <c>Label</c> are all
    /// optional and change just what is given. A patch that would put fixtures on top of others is refused.
    /// </summary>
    private CommandResult PatchFixtures(TokenStream t)
    {
        t.ExpectWord("fixture");
        var (from, to) = ParseRange(t);

        string? typeName = null;
        string? addressText = null;
        string? label = null;
        while (!t.AtEnd)
        {
            if (t.AcceptWord("type"))
                typeName = t.AcceptName("at", "label") ?? throw new CommandException("Fixture type expected");
            else if (t.AcceptWord("at"))
                addressText = t.ExpectNumberText("Address");
            else if (t.AcceptWord("label"))
                label = t.AcceptText() ?? throw new CommandException("Label text in quotes expected");
            else
                t.ExpectEnd();
        }

        if (to - from + 1 > MaxPatchRange)
            throw new CommandException($"Patch at most {MaxPatchRange} fixtures at once");

        FixtureType? type = null;
        if (typeName is not null)
        {
            type = show.FindType(typeName)
                ?? throw new CommandException($"Unknown fixture type '{typeName}'. Available: {string.Join(", ", show.AllTypes.Select(x => x.Name))}");
        }

        var existing = Enumerable.Range(from, to - from + 1).Where(id => show.Patch.Find(id) is not null).ToList();
        var isNew = to - from + 1 > existing.Count;

        if (typeName is null && addressText is null && label is null)
            throw new CommandException("Patch what? Add Type <profile> and At <universe.address>, e.g. Patch Fixture 1 Thru 8 Type RGB At 1.1");
        if (isNew && type is null)
            throw new CommandException("New fixtures need a profile: add Type <name>");
        if (isNew && addressText is null)
            throw new CommandException("New fixtures need an address: add At <universe.address>, e.g. At 1.1");

        var (universe, address) = addressText is null ? (0, 0) : ParseAddress(addressText);
        var placements = new List<Placement>();
        for (var id = from; id <= to; id++)
        {
            var current = show.Patch.Find(id);
            var fixtureType = type ?? current!.Type;

            int placedUniverse, placedAddress;
            if (addressText is null)
            {
                (placedUniverse, placedAddress) = (current!.Universe, current.Address);
            }
            else
            {
                if (address + fixtureType.Footprint - 1 > 512)
                {
                    universe++;
                    address = 1;
                }

                (placedUniverse, placedAddress) = (universe, address);
                address += fixtureType.Footprint;
            }

            if (placedUniverse > ArtNetPacket.MaxUniverse)
                throw new CommandException("Not enough universes for this patch");
            if (placedAddress + fixtureType.Footprint - 1 > 512)
                throw new CommandException($"Fixture {id} does not fit into universe {placedUniverse} at address {placedAddress} ({fixtureType.Name} needs {fixtureType.Footprint} channels)");

            placements.Add(new Placement(id, fixtureType, placedUniverse, placedAddress, label is null ? null : Labelled(label, id, from == to)));
        }

        CheckForOverlaps(placements);

        foreach (var placement in placements)
        {
            show.EnsureUniverse(placement.Universe);

            if (show.Patch.Find(placement.Id) is { } fixture)
            {
                // A label the operator never chose follows the profile; a chosen one stays.
                if (placement.Label is null && fixture.Label == $"{fixture.Type.Name} {fixture.Id}")
                    fixture.Label = $"{placement.Type.Name} {fixture.Id}";

                fixture.Type = placement.Type;
                fixture.Universe = placement.Universe;
                fixture.Address = placement.Address;
                if (placement.Label is not null)
                    fixture.Label = placement.Label;
            }
            else
            {
                show.Patch.Add(new Fixture(placement.Id, placement.Type, placement.Universe, placement.Address, placement.Label));
            }
        }

        var first = placements[0];
        var last = placements[^1];
        return CommandResult.Ok(
            $"Patched Fixture {from} Thru {to} as {first.Type.Name}: {first.Universe}.{first.Address} to {last.Universe}.{last.Address + last.Type.Footprint - 1}");
    }

    private static string Labelled(string label, int id, bool single) => single ? label : $"{label} {id}";

    /// <summary>Refuses placements that share DMX channels with each other or with fixtures that stay where they are.</summary>
    private void CheckForOverlaps(List<Placement> placements)
    {
        var moving = placements.Select(p => p.Id).ToHashSet();

        foreach (var placement in placements)
        {
            var occupant = show.Patch.FindOccupant(placement.Universe, placement.Address, placement.Type.Footprint, moving);
            if (occupant is not null)
            {
                throw new CommandException(
                    $"Fixture {placement.Id} at {placement.Universe}.{placement.Address} would overlap Fixture {occupant.Id} ({occupant.Universe}.{occupant.Address}-{occupant.EndAddress})");
            }
        }

        foreach (var universe in placements.GroupBy(p => p.Universe))
        {
            var sorted = universe.OrderBy(p => p.Address).ToList();
            for (var i = 1; i < sorted.Count; i++)
            {
                var previous = sorted[i - 1];
                if (previous.Address + previous.Type.Footprint > sorted[i].Address)
                {
                    throw new CommandException(
                        $"Fixture {previous.Id} and Fixture {sorted[i].Id} would overlap in universe {universe.Key}");
                }
            }
        }
    }

    /// <summary>Accepts <c>universe.address</c> (1.15) or a single absolute address (513 = universe 2, address 1).</summary>
    private static (int Universe, int Address) ParseAddress(string text)
    {
        var parts = text.Split('.');
        var universe = 1;
        var address = 0;

        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0], out universe) || !int.TryParse(parts[1], out address))
                throw new CommandException($"'{text}' is not a valid address");
        }
        else if (parts.Length == 1 && int.TryParse(text, out var absolute) && absolute >= 1)
        {
            universe = (absolute - 1) / 512 + 1;
            address = (absolute - 1) % 512 + 1;
        }

        if (universe < 1 || universe > ArtNetPacket.MaxUniverse || address < 1 || address > 512)
            throw new CommandException($"'{text}' is not a valid address. Use universe.address, e.g. 1.15");

        return (universe, address);
    }

    // ---- universes -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Universe 2 Label "FOH" Unicast 10.0.0.5 On</c>: creates the universe when it is new and changes what is
    /// given. Without options it just creates it (or reports it).
    /// </summary>
    private CommandResult UniverseCommand(TokenStream t)
    {
        var number = t.ExpectInteger("Universe number");
        if (number > ArtNetPacket.MaxUniverse)
            throw new CommandException($"Universe must be 1..{ArtNetPacket.MaxUniverse}");

        var isNew = !show.Universes.ContainsKey(number);
        var universe = show.EnsureUniverse(number);
        var changes = 0;

        while (!t.AtEnd)
        {
            if (t.AcceptWord("label"))
            {
                universe.Label = t.AcceptName("unicast", "broadcast", "on", "off") ?? throw new CommandException("Label text expected");
            }
            else if (t.AcceptWord("unicast"))
            {
                var text = t.ExpectNumberText("IPv4 address");
                if (!UniverseConfig.TryParseAddress(text, out _))
                    throw new CommandException($"'{text}' is not an IPv4 address");

                universe.Destination = UniverseDestination.Unicast;
                universe.UnicastAddress = text;
            }
            else if (t.AcceptWord("broadcast"))
            {
                universe.Destination = UniverseDestination.Broadcast;
            }
            else if (t.AcceptWord("on"))
            {
                universe.Enabled = true;
            }
            else if (t.AcceptWord("off"))
            {
                universe.Enabled = false;
            }
            else
            {
                throw new CommandException($"Unexpected '{t.Peek!.Value.Text}'. Use Label, Unicast <ip>, Broadcast, On or Off");
            }

            changes++;
        }

        var destination = universe.Destination == UniverseDestination.Unicast ? $"unicast {universe.UnicastAddress}" : "broadcast";
        var state = $"\"{universe.Label}\", {destination}, {(universe.Enabled ? "on" : "off")}";
        return CommandResult.Ok(isNew ? $"Created Universe {number}: {state}" : changes == 0 ? $"Universe {number}: {state}" : $"Universe {number} now {state}");
    }

    private void DeleteUniverse(int number)
    {
        if (!show.Universes.ContainsKey(number))
            throw new CommandException($"Universe {number} does not exist");

        var used = show.Patch.Fixtures.Count(f => f.Universe == number);
        if (used > 0)
            throw new CommandException($"Universe {number} still has {used} patched fixture{(used == 1 ? "" : "s")}. Move or delete them first");

        show.Universes.Remove(number);
    }
}
