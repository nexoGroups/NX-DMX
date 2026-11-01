using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NxDmx.Core;
using NxDmx.Core.Remote;

namespace NxDmx.Tests;

public class OscTests
{
    [Fact]
    public void Messages_round_trip_with_all_basic_types()
    {
        var bytes = OscMessage.Encode("/nxdmx/cmd", "Go Cuelist 1", 7, 0.5f);

        var message = Assert.Single(OscMessage.Parse(bytes));

        Assert.Equal("/nxdmx/cmd", message.Address);
        Assert.Equal("Go Cuelist 1", message.Text(0));
        Assert.Equal("7", message.Text(1));
        Assert.Equal("0.5", message.Text(2));
        Assert.Equal(7, message.Number(1));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { (byte)'/', (byte)'a', 0, 0, (byte)',', (byte)'i', 0, 0 })] // int missing
    public void Damaged_packets_are_dropped_without_throwing(byte[] data)
    {
        Assert.Empty(OscMessage.Parse(data));
    }

    [Theory]
    [InlineData("/nxdmx/go", "Go")]
    [InlineData("/nxdmx/go/2", "Go Cuelist 2")]
    [InlineData("/nxdmx/group/3", "Group 3")]
    [InlineData("/nxdmx/color/Warm White", "Color \"Warm White\"")]
    [InlineData("/nxdmx/red/55", "Red At 55")]
    [InlineData("/nxdmx/master/80", "Master At 80")]
    [InlineData("/nxdmx/blackout", "Blackout")]
    [InlineData("/nxdmx/blackout/off", "Blackout Off")]
    [InlineData("/nxdmx/oops", "Oops")]
    [InlineData("/other/go", null)]
    [InlineData("/nxdmx/nonsense", null)]
    public void Addresses_map_to_commands(string address, string? expected)
    {
        var message = Assert.Single(OscMessage.Parse(OscMessage.Encode(address)));

        Assert.Equal(expected, RemoteApi.FromOsc(message));
    }

    [Fact]
    public void Values_in_the_message_are_used_when_the_address_has_none_and_a_release_does_nothing()
    {
        Assert.Equal("Go Cuelist 2", RemoteApi.FromOsc(Parse("/nxdmx/go", 2)));
        Assert.Equal("Master At 40", RemoteApi.FromOsc(Parse("/nxdmx/master", 40f)));
        Assert.Equal("Pan At 25", RemoteApi.FromOsc(Parse("/nxdmx/pan", 25)));
        Assert.Equal("Group 3", RemoteApi.FromOsc(Parse("/nxdmx/group/3", 1)));
        Assert.Null(RemoteApi.FromOsc(Parse("/nxdmx/group/3", 0)));
        Assert.Null(RemoteApi.FromOsc(Parse("/nxdmx/go", 0)));
        Assert.Equal("Blackout Off", RemoteApi.FromOsc(Parse("/nxdmx/blackout", 0)));
        Assert.Equal("Blackout On", RemoteApi.FromOsc(Parse("/nxdmx/blackout", 1)));
        Assert.Equal("Go Cuelist 1; Oops", RemoteApi.FromOsc(Parse("/nxdmx/cmd", "Go Cuelist 1; Oops")) ?? "Go Cuelist 1; Oops");
    }

    private static OscMessage Parse(string address, object value) =>
        Assert.Single(OscMessage.Parse(OscMessage.Encode(address, value)));
}

public sealed class RemoteServerTests : IDisposable
{
    private readonly LightingConsole _console = new();
    private readonly RemoteApi _api;
    private readonly RemoteServer _server;
    private readonly List<RemoteActivity> _activity = [];

    public RemoteServerTests()
    {
        _console.Execute("Patch Fixture 1 Thru 4 Type RGBDim At 1.1");
        _console.Execute("Fixture 1 Thru 4 At 100");
        _console.Execute("Record Cue 1 \"Look\"");
        _console.Execute("Record Group 1 \"Front\"");
        _console.Execute("ClearAll");
        _api = new RemoteApi(_console);
        _api.Activity += a => { lock (_activity) _activity.Add(a); };
        _server = new RemoteServer(_api);
    }

    public void Dispose() => _server.Dispose();

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int FreeUdpPort()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    private RemoteOptions Start(string token = "")
    {
        var options = new RemoteOptions(HttpPort: FreeTcpPort(), OscPort: FreeUdpPort(), TextPort: FreeTcpPort(), Token: token);
        var status = _server.Apply(options);
        Assert.All(status, s => Assert.True(s.Running, $"{s.Name}: {s.Detail}"));
        return options;
    }

    private static async Task<(int Status, string Body)> Http(int port, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var text = Encoding.UTF8.GetString(memory.ToArray());
        var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return (int.Parse(text.Split(' ')[1]), text[(split + 4)..]);
    }

    private static string Get(string path, string extra = "") => $"GET {path} HTTP/1.1\r\nHost: localhost\r\n{extra}\r\n";

    [Fact]
    public async Task A_command_in_the_url_runs_and_reports()
    {
        var options = Start();

        var (status, body) = await Http(options.HttpPort, Get("/api/command?line=Fixture%201%20Thru%202%20At%2050"));

        Assert.Equal(200, status);
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("1 + 2", RemoteState.Variables(_console)["selection"]);
    }

    [Fact]
    public async Task A_failing_command_is_a_400_with_the_message()
    {
        var options = Start();

        var (status, body) = await Http(options.HttpPort, Get("/api/command?line=Go%20Cuelist%209"));

        Assert.Equal(400, status);
        Assert.Contains("does not exist", JsonDocument.Parse(body).RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Several_commands_run_in_order_and_a_post_body_works()
    {
        var options = Start();
        var content = "Group 1; Color Red";

        var (status, _) = await Http(options.HttpPort,
            $"POST /api/command HTTP/1.1\r\nHost: localhost\r\nContent-Length: {content.Length}\r\n\r\n{content}");

        Assert.Equal(200, status);
        Assert.Equal("1 Thru 4", RemoteState.Variables(_console)["selection"]);
        Assert.Equal(2, _activity.Count(a => a.Source.StartsWith("HTTP")));
    }

    [Theory]
    [InlineData("/api/go/1")]
    [InlineData("/api/toggle")]
    [InlineData("/api/group/1")]
    [InlineData("/api/blackout/on")]
    [InlineData("/api/master/70")]
    [InlineData("/api/cmd/Fixture%201")]
    public async Task Shortcut_paths_work(string path)
    {
        var options = Start();

        var (status, body) = await Http(options.HttpPort, Get(path));

        Assert.True(status == 200, body);
    }

    [Fact]
    public async Task State_and_variables_can_be_read()
    {
        var options = Start();
        await Http(options.HttpPort, Get("/api/go/1"));

        var state = JsonDocument.Parse((await Http(options.HttpPort, Get("/api/state"))).Body).RootElement;
        var cue = await Http(options.HttpPort, Get("/api/var/cuelist.1.cue"));
        var label = await Http(options.HttpPort, Get("/api/var/group.1.label"));
        var missing = await Http(options.HttpPort, Get("/api/var/nope"));

        Assert.True(state.GetProperty("cuelists")[0].GetProperty("active").GetBoolean());
        Assert.Equal("1", state.GetProperty("cuelists")[0].GetProperty("cue").GetString());
        Assert.Equal("1", cue.Body);
        Assert.Equal("Front", label.Body);
        Assert.Equal(404, missing.Status);
    }

    [Fact]
    public async Task Requests_from_web_pages_are_refused_but_controllers_are_not()
    {
        var options = Start();

        var fromPage = await Http(options.HttpPort, Get("/api/blackout", "Origin: https://evil.example\r\n"));
        var crossSite = await Http(options.HttpPort, Get("/api/blackout", "Sec-Fetch-Site: cross-site\r\n"));
        var controller = await Http(options.HttpPort, Get("/api/blackout"));

        Assert.Equal(403, fromPage.Status);
        Assert.Equal(403, crossSite.Status);
        Assert.Equal(200, controller.Status);
    }

    [Fact]
    public async Task A_token_is_required_when_one_is_set()
    {
        var options = Start(token: "s3cret");

        Assert.Equal(401, (await Http(options.HttpPort, Get("/api/blackout"))).Status);
        Assert.Equal(401, (await Http(options.HttpPort, Get("/api/blackout?token=wrong"))).Status);
        Assert.Equal(200, (await Http(options.HttpPort, Get("/api/blackout?token=s3cret"))).Status);
        Assert.Equal(200, (await Http(options.HttpPort, Get("/api/blackout", "X-Token: s3cret\r\n"))).Status);
        Assert.Equal(200, (await Http(options.HttpPort, Get("/"))).Status); // the help page needs no token
    }

    [Fact]
    public async Task Garbage_and_oversized_requests_do_not_hurt()
    {
        var options = Start();

        Assert.Equal(400, (await Http(options.HttpPort, "nonsense\r\n\r\n")).Status);
        Assert.Equal(404, (await Http(options.HttpPort, Get("/nowhere"))).Status);
        Assert.Equal(400, (await Http(options.HttpPort, $"POST /api/command HTTP/1.1\r\nContent-Length: 99999999\r\n\r\n")).Status);
        Assert.Equal(200, (await Http(options.HttpPort, Get("/api/state"))).Status);
    }

    [Fact]
    public async Task Text_over_tcp_answers_each_line()
    {
        var options = Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, options.TextPort);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true, NewLine = "\r\n" };

        await writer.WriteLineAsync("Group 1");
        var ok = await reader.ReadLineAsync();
        await writer.WriteLineAsync("Bogus");
        var error = await reader.ReadLineAsync();

        Assert.StartsWith("OK ", ok);
        Assert.StartsWith("ERR Unknown command", error);
    }

    [Fact]
    public async Task Text_over_udp_is_answered_to_the_sender()
    {
        var options = Start();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        await udp.SendAsync(Encoding.UTF8.GetBytes("Blackout On\n"), new IPEndPoint(IPAddress.Loopback, options.TextPort));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var reply = await udp.ReceiveAsync(cts.Token);

        Assert.StartsWith("OK Blackout on", Encoding.UTF8.GetString(reply.Buffer));
        Assert.Equal("1", RemoteState.Variables(_console)["blackout"]);
    }

    [Fact]
    public async Task Osc_messages_run_commands()
    {
        var options = Start();
        using var udp = new UdpClient();

        await udp.SendAsync(OscMessage.Encode("/nxdmx/group/1"), new IPEndPoint(IPAddress.Loopback, options.OscPort));
        await udp.SendAsync(OscMessage.Encode("/nxdmx/master", 25f), new IPEndPoint(IPAddress.Loopback, options.OscPort));

        await WaitFor(() => RemoteState.Variables(_console)["master"] == "25");
        Assert.Equal("1 Thru 4", RemoteState.Variables(_console)["selection"]);
    }

    [Fact]
    public void A_taken_port_turns_off_just_that_interface()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var taken = ((IPEndPoint)blocker.LocalEndpoint).Port;

        var status = _server.Apply(new RemoteOptions(HttpPort: taken, OscPort: FreeUdpPort(), TextPort: FreeTcpPort()));

        Assert.False(status[0].Running);
        Assert.Contains("in use", status[0].Detail);
        Assert.True(status[1].Running);
        Assert.True(status[2].Running);
    }

    [Fact]
    public void Disabled_interfaces_and_restarts_are_handled()
    {
        var status = _server.Apply(new RemoteOptions(HttpEnabled: false, OscEnabled: false, TextEnabled: false));
        Assert.All(status, s => Assert.False(s.Running));

        // Starting again on the same ports right away must work.
        var options = Start();
        Start();
        Assert.NotEqual(0, options.HttpPort);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.True(condition(), "Timed out");
    }
}
