using System.Net;
using System.Net.Sockets;
using NxDmx.Core.ArtNet;

namespace NxDmx.Tests;

public class ArtNetTests
{
    [Fact]
    public void ArtDmx_header_and_data_for_universe_1()
    {
        var data = new byte[512];
        data[0] = 255;
        data[511] = 7;
        var buffer = new byte[ArtNetPacket.MaxPacketLength];

        var length = ArtNetPacket.WriteArtDmx(buffer, sequence: 5, universe: 1, data);

        Assert.Equal(530, length);
        Assert.Equal("Art-Net\0"u8.ToArray(), buffer[..8]);
        Assert.Equal(0x00, buffer[8]); // OpCode 0x5000, low byte first
        Assert.Equal(0x50, buffer[9]);
        Assert.Equal(0, buffer[10]); // protocol version 14
        Assert.Equal(14, buffer[11]);
        Assert.Equal(5, buffer[12]);
        Assert.Equal(0, buffer[13]);
        Assert.Equal(0, buffer[14]); // SubUni
        Assert.Equal(0, buffer[15]); // Net
        Assert.Equal(2, buffer[16]); // length 512, high byte first
        Assert.Equal(0, buffer[17]);
        Assert.Equal(255, buffer[18]);
        Assert.Equal(7, buffer[18 + 511]);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(17, 16, 0)]
    [InlineData(257, 0, 1)]
    [InlineData(32768, 255, 127)]
    public void Universe_maps_to_port_address(int universe, int subUni, int net)
    {
        var buffer = new byte[ArtNetPacket.MaxPacketLength];

        ArtNetPacket.WriteArtDmx(buffer, 1, universe, new byte[512]);

        Assert.Equal(subUni, buffer[14]);
        Assert.Equal(net, buffer[15]);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    [InlineData(511, 512)]
    public void Data_length_is_padded_to_an_even_number(int dataLength, int expectedLength)
    {
        var buffer = new byte[ArtNetPacket.MaxPacketLength];
        Array.Fill(buffer, (byte)0xEE);
        var data = Enumerable.Repeat((byte)9, dataLength).ToArray();

        var total = ArtNetPacket.WriteArtDmx(buffer, 1, 1, data);

        Assert.Equal(ArtNetPacket.HeaderLength + expectedLength, total);
        Assert.Equal(expectedLength, (buffer[16] << 8) | buffer[17]);
        Assert.Equal(9, buffer[ArtNetPacket.HeaderLength + dataLength - 1]);
        if (expectedLength > dataLength)
            Assert.Equal(0, buffer[ArtNetPacket.HeaderLength + dataLength]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32769)]
    public void Universe_out_of_range_is_rejected(int universe)
    {
        var buffer = new byte[ArtNetPacket.MaxPacketLength];

        Assert.Throws<ArgumentOutOfRangeException>(() => ArtNetPacket.WriteArtDmx(buffer, 1, universe, new byte[512]));
    }

    [Fact]
    public async Task Output_sends_packets_over_udp_with_a_sequence_that_skips_zero()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        using var output = new ArtNetOutput(IPAddress.Loopback, port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sequences = new List<byte>();

        for (var i = 0; i < 256; i++)
        {
            output.Send(3, new byte[] { 10, 20, 30, 40 });
            var packet = await receiver.ReceiveAsync(timeout.Token);
            Assert.Equal(22, packet.Buffer.Length);
            Assert.Equal(2, packet.Buffer[14]); // universe 3 is port-address 2
            Assert.Equal(new byte[] { 10, 20, 30, 40 }, packet.Buffer[18..]);
            sequences.Add(packet.Buffer[12]);
        }

        Assert.Equal(1, sequences[0]);
        Assert.Equal(255, sequences[254]);
        Assert.Equal(1, sequences[255]);
        Assert.DoesNotContain((byte)0, sequences);
    }
}
