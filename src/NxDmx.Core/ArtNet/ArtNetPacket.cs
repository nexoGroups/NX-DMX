using NxDmx.Core.Dmx;

namespace NxDmx.Core.ArtNet;

/// <summary>Encoding of Art-Net 4 packets.</summary>
public static class ArtNetPacket
{
    public const int Port = 6454;
    public const int HeaderLength = 18;
    public const int MaxPacketLength = HeaderLength + DmxFrame.UniverseSize;

    /// <summary>The 15-bit Port-Address allows 32768 universes (numbered 1..32768 here, 0..32767 on the wire).</summary>
    public const int MaxUniverse = 32768;

    private const ushort OpDmx = 0x5000;
    private const byte ProtocolVersion = 14;

    private static ReadOnlySpan<byte> Id => "Art-Net\0"u8;

    /// <summary>
    /// Writes an ArtDmx packet and returns its length. Data is padded with a zero to an even length,
    /// as the protocol requires.
    /// </summary>
    public static int WriteArtDmx(Span<byte> buffer, byte sequence, int universe, ReadOnlySpan<byte> data)
    {
        if (universe is < 1 or > MaxUniverse)
            throw new ArgumentOutOfRangeException(nameof(universe), universe, $"Universe must be 1..{MaxUniverse}.");
        if (data.Length > DmxFrame.UniverseSize)
            throw new ArgumentException($"A universe holds at most {DmxFrame.UniverseSize} channels.", nameof(data));

        var dataLength = Math.Max(2, data.Length + (data.Length & 1));
        var total = HeaderLength + dataLength;
        if (buffer.Length < total)
            throw new ArgumentException("Buffer too small.", nameof(buffer));

        var portAddress = universe - 1;

        Id.CopyTo(buffer);
        buffer[8] = (byte)(OpDmx & 0xFF);
        buffer[9] = (byte)(OpDmx >> 8);
        buffer[10] = 0;
        buffer[11] = ProtocolVersion;
        buffer[12] = sequence;
        buffer[13] = 0;
        buffer[14] = (byte)(portAddress & 0xFF);
        buffer[15] = (byte)((portAddress >> 8) & 0x7F);
        buffer[16] = (byte)(dataLength >> 8);
        buffer[17] = (byte)(dataLength & 0xFF);

        data.CopyTo(buffer[HeaderLength..]);
        buffer.Slice(HeaderLength + data.Length, dataLength - data.Length).Clear();
        return total;
    }
}
