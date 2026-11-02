using System.Net;
using System.Net.Sockets;
using NxDmx.Core;
using NxDmx.Core.ArtNet;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;
using NxDmx.Core.Remote;
using NxDmx.Core.Storage;

namespace NxDmx.Tests;

public class FaderTests
{
    private static Rig LookRig()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Thru 4 At 100", "Record Cue 1", "ClearAll", "Fixture 1 Thru 4", "Record Group 1", "ClearAll");
        return rig;
    }

    [Fact]
    public void A_cuelist_fader_starts_scales_and_releases_the_cuelist()
    {
        var rig = LookRig();
        rig.Do("Fader 1 Cuelist 1");

        rig.Do("Fader 1 At 50");
        Assert.True(rig.Show.Cuelists[1].IsActive);
        Assert.Equal(128, rig.Universe(1)[0]);

        rig.Do("Fader 1 At 100");
        Assert.Equal(255, rig.Universe(1)[0]);

        rig.Do("Fader 1 At 0");
        Assert.False(rig.Show.Cuelists[1].IsActive);
        Assert.Equal(0, rig.Universe(1)[0]);
    }

    [Fact]
    public void A_group_fader_scales_only_the_intensity_of_its_fixtures()
    {
        var rig = LookRig();
        rig.Do("Fixture 1 Thru 8 At 100", "Fader 2 Group 1");

        rig.Do("Fader 2 At 25");

        Assert.Equal(64, rig.Universe(1)[0]);   // fixture 1 is in the group
        Assert.Equal(255, rig.Universe(1)[16]); // fixture 5 is not
        Assert.Equal(255, rig.Universe(1)[1]);  // colour is untouched
    }

    [Fact]
    public void A_new_group_fader_starts_at_full_and_the_master_fader_moves_the_grand_master()
    {
        var rig = LookRig();
        rig.Do("Fixture 1 At 100", "Fader 2 Group 1", "Fader 3 Master");
        Assert.Equal(255, rig.Universe(1)[0]);

        rig.Do("Fader 3 At 40");
        Assert.Equal(0.4, rig.Output.GrandMaster, 3);
    }

    [Fact]
    public void Fader_buttons_do_what_their_kind_says()
    {
        var rig = LookRig();
        rig.Do("Fader 1 Cuelist 1", "Fader 2 Group 1", "Fader 3 Master");

        rig.Do("Fader 1 Button");
        Assert.True(rig.Show.Cuelists[1].IsActive);

        rig.Do("Fader 3 Button");
        Assert.True(rig.Output.Blackout);

        rig.Do("Fixture 1 At 100", "Fader 2 Button");
        Assert.Equal(0, rig.Universe(1)[0]);
    }

    [Fact]
    public void Moving_a_fader_is_live_and_assigning_one_can_be_undone()
    {
        var rig = LookRig();
        rig.Do("Fader 1 Cuelist 1", "Fader 1 At 60");

        rig.Do("Oops");
        Assert.Empty(rig.Show.Faders); // the assignment was the last programming step, the move was not

        Assert.Contains("nothing assigned", rig.Fail("Fader 1 At 10").Message);
    }

    [Fact]
    public void Faders_are_styled_and_cleared_by_command()
    {
        var rig = LookRig();

        rig.Do("Fader 4 Cuelist 1", "Fader 4 Label \"Main look\"", "Fader 4 Color #FF8800");
        var fader = rig.Show.Faders[4];
        Assert.Equal("Main look", fader.Label);
        Assert.Equal("#FF8800", fader.Color);

        rig.Do("Fader 4 Color Blue");
        Assert.Equal("#0000FF", fader.Color);

        rig.Do("Fader 4 Clear");
        Assert.Empty(rig.Show.Faders);
    }

    [Theory]
    [InlineData("Fader 0 At 5")]
    [InlineData("Fader 41 Cuelist 1")]
    [InlineData("Fader 1 Cuelist 9")]
    [InlineData("Fader 1 Group 9")]
    [InlineData("Fader 1 Color Chartreuse")]
    [InlineData("Fader 1 Frobnicate")]
    public void Bad_fader_commands_are_refused(string line)
    {
        var rig = LookRig();

        rig.Fail(line);

        Assert.Empty(rig.Show.Faders);
    }

    [Fact]
    public void Fader_assignments_are_saved_with_the_show()
    {
        var rig = LookRig();
        rig.Do("Fader 1 Cuelist 1", "Fader 2 Group 1", "Fader 2 Color #112233", "Fader 3 Master");
        var json = ShowFile.Serialize(rig.Show);

        var loaded = ShowFile.Deserialize(json);

        Assert.Equal(json, ShowFile.Serialize(loaded));
        Assert.Equal(FaderKind.Group, loaded.Faders[2].Kind);
        Assert.Equal("#112233", loaded.Faders[2].Color);
        Assert.Equal(FaderKind.Master, loaded.Faders[3].Kind);
    }

    [Fact]
    public void Remote_paths_and_osc_move_faders()
    {
        Assert.Equal("Fader 3 At 60", RemoteApi.FromShortcut("fader", ["3", "60"]));
        Assert.Equal("Fader 3 Button", RemoteApi.FromShortcut("fader", ["3"]));
        var message = Assert.Single(OscMessage.Parse(OscMessage.Encode("/nxdmx/fader/3", 60f)));
        Assert.Equal("Fader 3 At 60", RemoteApi.FromOsc(message));
        var plain = Assert.Single(OscMessage.Parse(OscMessage.Encode("/nxdmx/fader", 3, 60)));
        Assert.Equal("Fader 3 At 60", RemoteApi.FromOsc(plain));
    }
}

public class CustomColorTests
{
    [Fact]
    public void A_custom_colour_is_used_by_the_color_command_and_overrides_a_built_in_one()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 1 Type RGB At 1.1");
        console.Execute("Fixture 1 At 100");

        Assert.True(console.SaveColor("Sky", "#3399FF").Success);
        Assert.True(console.Execute("Color Sky").Success);
        Assert.True(console.SaveColor("Red", "#800000").Success);
        Assert.True(console.Execute("Color Red").Success);

        var frame = console.Render();
        Assert.Equal(128, frame.Universes[1][0]);
    }

    [Fact]
    public void A_hex_colour_works_without_saving_it()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type RGB At 1.1", "Fixture 1", "Color #FF8000");

        Assert.Equal(1, rig.Value(1, FixtureAttribute.Red));
        Assert.Equal(128 / 255.0, rig.Value(1, FixtureAttribute.Green), 3);
        Assert.Equal(0, rig.Value(1, FixtureAttribute.Blue));
    }

    [Theory]
    [InlineData("", "#FF0000")]
    [InlineData("Bad", "#GG0000")]
    [InlineData("Bad", "#FF00")]
    [InlineData("Qu\"ote", "#FF0000")]
    public void Invalid_colours_are_refused(string name, string hex)
    {
        var console = new LightingConsole();

        Assert.False(console.SaveColor(name, hex).Success);
        Assert.Equal(0, console.Read((show, _) => show.CustomColors.Count));
    }

    [Fact]
    public void Colours_are_deleted_and_taken_back_with_oops()
    {
        var console = new LightingConsole();
        console.SaveColor("Sky", "#3399FF");

        Assert.True(console.DeleteColor("Sky").Success);
        Assert.False(console.DeleteColor("Sky").Success);
        console.Execute("Oops");

        Assert.Equal(1, console.Read((show, _) => show.CustomColors.Count));
    }

    [Fact]
    public void Colours_survive_saving_the_show()
    {
        var console = new LightingConsole();
        console.SaveColor("Sky", "#3399FF");
        var json = console.ExportShow();

        var other = new LightingConsole();
        other.ImportShow(json);

        Assert.Equal(json, other.ExportShow());
        Assert.Equal("#3399FF", other.Read((show, _) => show.FindColor("sky")!.Hex));
    }

    [Fact]
    public void Colour_files_are_read_as_json_or_plain_text()
    {
        var json = ColorFile.Serialize([new ColorPreset("Sky", 0.2, 0.6, 1, Custom: true)]);
        Assert.Equal([("Sky", "#3399FF")], ColorFile.Parse(json));

        var text = "// my colours\nDeep Sea;#003366\n\"Sunset\", #FF5500\n";
        Assert.Equal([("Deep Sea", "#003366"), ("Sunset", "#FF5500")], ColorFile.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("Name;#12")]
    [InlineData("{\"Format\":\"other\"}")]
    [InlineData("{ not json")]
    public void Bad_colour_files_are_rejected_with_a_message(string text)
    {
        Assert.Throws<ShowFileException>(() => ColorFile.Parse(text));
    }

    [Fact]
    public void An_import_is_one_undo_step_and_checks_everything_first()
    {
        var console = new LightingConsole();

        Assert.True(console.ImportColors([("A", "#FF0000"), ("B", "#00FF00")]).Success);
        Assert.False(console.ImportColors([("C", "#0000FF"), ("D", "nope")]).Success);
        Assert.Equal(2, console.Read((show, _) => show.CustomColors.Count));

        console.Execute("Oops");
        Assert.Equal(0, console.Read((show, _) => show.CustomColors.Count));
    }
}

public class ProfileNameTests
{
    [Fact]
    public void Built_in_profiles_are_marked_in_lists_but_keep_their_command_name()
    {
        var rgb = FixtureTypeLibrary.Find("RGB")!;

        Assert.Equal("RGB (PreBuild)", rgb.DisplayName);
        Assert.Equal("RGB", rgb.Name);
        Assert.Equal("Own", new FixtureType("Own", [new(FixtureAttribute.Dimmer, 1)]).DisplayName);
    }
}

public class ArtNetDiscoveryTests
{
    private static byte[] Reply(string shortName, string longName, params byte[] ip)
    {
        var packet = new byte[239];
        "Art-Net\0"u8.CopyTo(packet);
        packet[8] = 0x00;
        packet[9] = 0x21;
        ip.CopyTo(packet, 10);
        System.Text.Encoding.ASCII.GetBytes(shortName).CopyTo(packet, 26);
        System.Text.Encoding.ASCII.GetBytes(longName).CopyTo(packet, 44);
        return packet;
    }

    [Fact]
    public void The_poll_packet_is_a_valid_artpoll()
    {
        var poll = ArtNetDiscovery.BuildPoll();

        Assert.Equal(14, poll.Length);
        Assert.Equal("Art-Net\0", System.Text.Encoding.ASCII.GetString(poll, 0, 8));
        Assert.Equal(new byte[] { 0x00, 0x20, 0, 14 }, poll[8..12]);
    }

    [Fact]
    public void A_reply_gives_address_and_names()
    {
        var node = ArtNetDiscovery.ParsePollReply(Reply("Stage box", "Stage box long name", 10, 1, 2, 3));

        Assert.Equal(IPAddress.Parse("10.1.2.3"), node!.Address);
        Assert.Equal("Stage box", node.ShortName);
        Assert.Equal("Stage box long name", node.LongName);
        Assert.Equal("Stage box – 10.1.2.3", node.Text);
    }

    [Fact]
    public void Other_packets_are_ignored()
    {
        Assert.Null(ArtNetDiscovery.ParsePollReply([1, 2, 3]));
        Assert.Null(ArtNetDiscovery.ParsePollReply(ArtNetDiscovery.BuildPoll()));
        var wrongId = Reply("x", "y", 1, 2, 3, 4);
        wrongId[0] = (byte)'X';
        Assert.Null(ArtNetDiscovery.ParsePollReply(wrongId));
    }

    [Fact]
    public async Task Nodes_that_answer_the_poll_are_found()
    {
        // A node on loopback: it listens on its own port and answers the poll to the discovering socket's port.
        using var node = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var nodePort = ((IPEndPoint)node.Client.LocalEndPoint!).Port;
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var discoveryPort = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Close();

        var responder = Task.Run(async () =>
        {
            var poll = await node.ReceiveAsync();
            Assert.Equal(ArtNetDiscovery.BuildPoll(), poll.Buffer);
            await node.SendAsync(Reply("Loop node", "Loopback test node", 127, 0, 0, 1), poll.RemoteEndPoint);
            await node.SendAsync(Reply("Loop node", "duplicate answer", 127, 0, 0, 1), poll.RemoteEndPoint);
        });

        // The poll goes to the node's port; the discovering socket listens on its own port.
        var found = await DiscoverVia(nodePort, discoveryPort);
        await responder;

        var one = Assert.Single(found);
        Assert.Equal("Loop node", one.ShortName);
    }

    private static async Task<IReadOnlyList<ArtNetNode>> DiscoverVia(int nodePort, int ownPort)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, ownPort));
        await socket.SendToAsync(ArtNetDiscovery.BuildPoll(), SocketFlags.None, new IPEndPoint(IPAddress.Loopback, nodePort));

        var buffer = new byte[1024];
        var found = new Dictionary<IPAddress, ArtNetNode>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        try
        {
            while (true)
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token);
                if (ArtNetDiscovery.ParsePollReply(buffer.AsSpan(0, result.ReceivedBytes)) is { } node)
                    found[node.Address] = node;
            }
        }
        catch (OperationCanceledException)
        {
        }

        return [.. found.Values];
    }

    [Fact]
    public async Task Discovery_reports_a_busy_port_as_a_socket_error()
    {
        using var holder = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        holder.ExclusiveAddressUse = true;
        holder.Bind(new IPEndPoint(IPAddress.Any, 0));
        var port = ((IPEndPoint)holder.LocalEndPoint!).Port;

        await Assert.ThrowsAsync<SocketException>(() =>
            ArtNetDiscovery.DiscoverAsync(IPAddress.Loopback, IPAddress.Loopback, TimeSpan.FromMilliseconds(100), port));
    }
}
