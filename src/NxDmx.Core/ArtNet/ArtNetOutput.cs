using System.Net;
using System.Net.Sockets;
using NxDmx.Core.Dmx;

namespace NxDmx.Core.ArtNet;

/// <summary>Sends universes as ArtDmx over UDP: to one common destination (usually broadcast), or per universe to a node.</summary>
public sealed class ArtNetOutput : IRoutedDmxOutput, IDisposable
{
    private readonly Socket _socket;
    private readonly IPEndPoint _target;
    private readonly byte[] _buffer = new byte[ArtNetPacket.MaxPacketLength];
    private readonly Dictionary<int, byte> _sequences = new();
    private readonly Dictionary<IPAddress, IPEndPoint> _endpoints = new();
    private readonly object _gate = new();

    /// <param name="target">Node address, or a broadcast address such as 255.255.255.255 or 2.255.255.255.</param>
    /// <param name="localAddress">
    /// Network adapter to send from. Needed on PCs with several adapters, because a broadcast
    /// otherwise leaves through whichever adapter Windows picks.
    /// </param>
    public ArtNetOutput(IPAddress target, int port = ArtNetPacket.Port, IPAddress? localAddress = null)
    {
        _target = new IPEndPoint(target, port);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
        try
        {
            _socket.Bind(new IPEndPoint(localAddress ?? IPAddress.Any, 0));
        }
        catch
        {
            _socket.Dispose();
            throw;
        }
    }

    public void Send(int universe, ReadOnlySpan<byte> data) => Send(universe, data, null);

    public void Send(int universe, ReadOnlySpan<byte> data, IPAddress? target)
    {
        lock (_gate)
        {
            var length = ArtNetPacket.WriteArtDmx(_buffer, NextSequence(universe), universe, data);
            _socket.SendTo(_buffer.AsSpan(0, length), SocketFlags.None, target is null ? _target : EndpointFor(target));
        }
    }

    public void Dispose() => _socket.Dispose();

    private IPEndPoint EndpointFor(IPAddress address)
    {
        if (!_endpoints.TryGetValue(address, out var endpoint))
        {
            endpoint = new IPEndPoint(address, _target.Port);
            _endpoints[address] = endpoint;
        }

        return endpoint;
    }

    /// <summary>Sequence runs 1..255 and skips 0, which means "sequencing disabled".</summary>
    private byte NextSequence(int universe)
    {
        _sequences.TryGetValue(universe, out var current);
        var next = current == 255 ? (byte)1 : (byte)(current + 1);
        _sequences[universe] = next;
        return next;
    }
}
