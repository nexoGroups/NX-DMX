using System.Net;

namespace NxDmx.Core.Dmx;

public interface IDmxOutput
{
    /// <summary>Sends one universe. <paramref name="universe"/> is 1-based, as shown to the operator.</summary>
    void Send(int universe, ReadOnlySpan<byte> data);
}

/// <summary>An output that can send single universes to a node of their own instead of the common destination.</summary>
public interface IRoutedDmxOutput : IDmxOutput
{
    /// <param name="target">The node to send to, or null for the output's common (broadcast) destination.</param>
    void Send(int universe, ReadOnlySpan<byte> data, IPAddress? target);
}
