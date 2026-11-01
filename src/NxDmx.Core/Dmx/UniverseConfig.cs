using System.Net;
using System.Net.Sockets;

namespace NxDmx.Core.Dmx;

/// <summary>Where a universe is sent: to the broadcast address of the output settings, or straight to one node.</summary>
public enum UniverseDestination
{
    Broadcast,
    Unicast,
}

/// <summary>
/// One DMX universe of the show. Each universe has its own name, can be switched off and has its own destination, so
/// that, say, universe 1 goes to a node on the stage and universe 2 to a dimmer rack by broadcast.
/// </summary>
public sealed class UniverseConfig(int number)
{
    /// <summary>1-based, as shown to the operator. On the wire the Art-Net Port-Address is this number minus one.</summary>
    public int Number { get; } = number;

    public string Label { get; set; } = $"Universe {number}";

    public bool Enabled { get; set; } = true;

    public UniverseDestination Destination { get; set; } = UniverseDestination.Broadcast;

    /// <summary>IPv4 address of the node when <see cref="Destination"/> is unicast.</summary>
    public string UnicastAddress { get; set; } = "";

    public UniverseConfig Clone() => new(Number)
    {
        Label = Label,
        Enabled = Enabled,
        Destination = Destination,
        UnicastAddress = UnicastAddress,
    };

    public static bool TryParseAddress(string text, out IPAddress address) =>
        IPAddress.TryParse(text.Trim(), out address!) && address.AddressFamily == AddressFamily.InterNetwork;
}

/// <summary>What the output has to know about one universe when it sends a frame.</summary>
public sealed record UniverseRoute(bool Enabled, IPAddress? Unicast)
{
    public static UniverseRoute Default { get; } = new(true, null);
}
