using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NxDmx.Core.ArtNet;

/// <summary>An Art-Net node that answered a poll.</summary>
public sealed record ArtNetNode(IPAddress Address, string ShortName, string LongName)
{
    /// <summary>How the node is listed for the operator: name and address.</summary>
    public string Text => ShortName.Length > 0 ? $"{ShortName} – {Address}" : Address.ToString();

    public override string ToString() => Text;
}

/// <summary>Finds Art-Net nodes on the network with ArtPoll, so a universe can be sent to one of them.</summary>
public static class ArtNetDiscovery
{
    private const ushort OpPoll = 0x2000;
    private const ushort OpPollReply = 0x2100;

    private static ReadOnlySpan<byte> Id => "Art-Net\0"u8;

    /// <summary>The 14 byte ArtPoll packet.</summary>
    public static byte[] BuildPoll()
    {
        var packet = new byte[14];
        Id.CopyTo(packet);
        packet[8] = (byte)(OpPoll & 0xFF);
        packet[9] = (byte)(OpPoll >> 8);
        packet[10] = 0;
        packet[11] = 14;
        packet[12] = 0; // TalkToMe: reply to this poll only
        packet[13] = 0; // lowest priority of diagnostics
        return packet;
    }

    /// <summary>Reads an ArtPollReply. Null for any other or damaged packet.</summary>
    public static ArtNetNode? ParsePollReply(ReadOnlySpan<byte> data)
    {
        if (data.Length < 108 || !data[..8].SequenceEqual(Id))
            return null;
        if ((data[9] << 8 | data[8]) != OpPollReply)
            return null;

        var address = new IPAddress(data.Slice(10, 4));
        return new ArtNetNode(address, Text(data.Slice(26, 18)), Text(data.Slice(44, 64)));
    }

    private static string Text(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? field : field[..end]).Trim();
    }

    /// <summary>
    /// Sends an ArtPoll to <paramref name="target"/> and collects the replies for <paramref name="wait"/>. Nodes answer
    /// to port 6454, so that port has to be free to listen on; the call throws a <see cref="SocketException"/> when
    /// another program holds it without sharing.
    /// </summary>
    public static async Task<IReadOnlyList<ArtNetNode>> DiscoverAsync(
        IPAddress target, IPAddress? local, TimeSpan wait, int port = ArtNetPacket.Port, CancellationToken token = default)
    {
        // Nodes answer to port 6454 on any address of this computer, so the listener binds to all of them; the poll
        // itself leaves through the chosen adapter.
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Bind(new IPEndPoint(IPAddress.Any, port));

        using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true })
        {
            sender.Bind(new IPEndPoint(local ?? IPAddress.Any, 0));
            await sender.SendToAsync(BuildPoll(), SocketFlags.None, new IPEndPoint(target, port), token);
        }

        var found = new Dictionary<IPAddress, ArtNetNode>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(wait);
        var buffer = new byte[1024];
        var from = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (true)
            {
                var result = await listener.ReceiveFromAsync(buffer, SocketFlags.None, from, timeout.Token);
                if (ParsePollReply(buffer.AsSpan(0, result.ReceivedBytes)) is { } node)
                    found[node.Address] = node;
            }
        }
        catch (OperationCanceledException)
        {
            // The waiting time is over; what was heard is the answer.
        }

        return [.. found.Values.OrderBy(n => n.Address.GetAddressBytes(), Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b)))];
    }
}
