using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NxDmx.Core.Remote;

/// <summary>Which remote interfaces are on, where they listen, and who may reach them.</summary>
public sealed record RemoteOptions(
    bool HttpEnabled = true,
    int HttpPort = 8765,
    bool OscEnabled = true,
    int OscPort = 8000,
    bool TextEnabled = true,
    int TextPort = 7000,
    bool AllowNetwork = false,
    string Token = "")
{
    public static RemoteOptions Default { get; } = new();
}

/// <summary>The state of one interface after (re)starting: listening, off, or the reason it could not start.</summary>
public sealed record RemoteStatus(string Name, bool Running, string Detail);

/// <summary>
/// The remote control of the console for Bitfocus Companion, Stream Deck and other controllers: an HTTP API, OSC over
/// UDP and a plain-text command port (TCP and UDP). By default they listen on this computer only; "allow network"
/// opens them to other computers. A port that is taken or refused disables just that interface, never the console.
/// </summary>
public sealed class RemoteServer(RemoteApi api) : IDisposable
{
    private const int MaxRequestBytes = 64 * 1024;
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly List<Action> _stoppers = [];
    private CancellationTokenSource? _cts;
    private RemoteOptions _options = RemoteOptions.Default;

    public RemoteApi Api => api;

    public IReadOnlyList<RemoteStatus> Status { get; private set; } = [];

    /// <summary>Stops everything that runs and starts what the options ask for.</summary>
    public IReadOnlyList<RemoteStatus> Apply(RemoteOptions options)
    {
        lock (_gate)
        {
            StopCore();
            _options = options;
            _cts = new CancellationTokenSource();
            var status = new List<RemoteStatus>();
            var address = options.AllowNetwork ? IPAddress.Any : IPAddress.Loopback;

            status.Add(Start("HTTP", options.HttpEnabled, options.HttpPort, $"http://localhost:{options.HttpPort}/",
                () => StartHttp(address, options.HttpPort, _cts.Token)));
            status.Add(Start("OSC", options.OscEnabled, options.OscPort, $"UDP port {options.OscPort}",
                () => StartOsc(address, options.OscPort, _cts.Token)));
            status.Add(Start("Text", options.TextEnabled, options.TextPort, $"TCP and UDP port {options.TextPort}",
                () => StartText(address, options.TextPort, _cts.Token)));

            Status = status;
            return status;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
            Status = [];
        }
    }

    public void Dispose() => Stop();

    private RemoteStatus Start(string name, bool enabled, int port, string where, Action start)
    {
        if (!enabled)
            return new RemoteStatus(name, false, "Off");
        if (port is < 1 or > 65535)
            return new RemoteStatus(name, false, $"Port {port} is not valid");

        try
        {
            start();
            return new RemoteStatus(name, true, $"Listening on {where}");
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException or ObjectDisposedException)
        {
            var reason = ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse or SocketError.AccessDenied }
                ? $"Port {port} is already in use"
                : ex.Message;
            return new RemoteStatus(name, false, reason);
        }
    }

    private void StopCore()
    {
        _cts?.Cancel();
        foreach (var stop in _stoppers)
        {
            try
            {
                stop();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
            }
        }

        _stoppers.Clear();
        _cts?.Dispose();
        _cts = null;
    }

    // ---- HTTP -------------------------------------------------------------------------------------------------

    private void StartHttp(IPAddress address, int port, CancellationToken token)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        _stoppers.Add(listener.Stop);
        _ = AcceptLoop(listener, client => ServeHttp(client, token), token);
    }

    private static async Task AcceptLoop(TcpListener listener, Func<TcpClient, Task> serve, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                using (client)
                {
                    try
                    {
                        await serve(client);
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                    {
                        // A client that vanishes mid-request is its own problem.
                    }
                }
            }, token);
        }
    }

    private async Task ServeHttp(TcpClient client, CancellationToken serverToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        timeout.CancelAfter(ClientTimeout);
        var stream = client.GetStream();

        var request = await ReadRequest(stream, timeout.Token);
        if (request is null)
        {
            await Respond(stream, 400, "text/plain", "Bad request");
            return;
        }

        var (status, type, body) = Handle(request);
        await Respond(stream, status, type, body);
    }

    private sealed record HttpRequest(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers, string Body);

    private static async Task<HttpRequest?> ReadRequest(Stream stream, CancellationToken token)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            if (length == buffer.Length)
                return null;

            var read = await stream.ReadAsync(buffer.AsMemory(length), token);
            if (read == 0)
                return null;

            length += read;
            headerEnd = IndexOf(buffer, length, "\r\n\r\n"u8);
        }

        var head = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var requestLine = head[0].Split(' ');
        if (requestLine.Length < 2)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in head.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var contentLength = headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var parsed) ? parsed : 0;
        var bodyStart = headerEnd + 4;
        if (contentLength < 0 || bodyStart + contentLength > buffer.Length)
            return null;

        while (length < bodyStart + contentLength)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length, bodyStart + contentLength - length), token);
            if (read == 0)
                return null;

            length += read;
        }

        var target = requestLine[1];
        var question = target.IndexOf('?');
        var path = question < 0 ? target : target[..question];
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (question >= 0)
        {
            foreach (var pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var key = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
                var value = equals < 0 ? "" : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
                query[key] = value;
            }
        }

        return new HttpRequest(requestLine[0].ToUpperInvariant(), path, query, headers,
            Encoding.UTF8.GetString(buffer, bodyStart, contentLength));
    }

    private static int IndexOf(byte[] buffer, int length, ReadOnlySpan<byte> pattern) =>
        buffer.AsSpan(0, length).IndexOf(pattern);

    private static async Task Respond(Stream stream, int status, string contentType, string body)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", _ => "Error" };
        var payload = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}; charset=utf-8\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    private (int Status, string Type, string Body) Handle(HttpRequest request)
    {
        if (request.Method is not ("GET" or "POST" or "PUT"))
            return (400, "application/json", Error("Use GET or POST"));

        var suppliedToken = request.Query.GetValueOrDefault("token")
            ?? request.Headers.GetValueOrDefault("X-Token")
            ?? (request.Headers.GetValueOrDefault("Authorization") is { } auth && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..].Trim() : null);
        var tokenOk = _options.Token.Length > 0 && string.Equals(suppliedToken, _options.Token, StringComparison.Ordinal);

        // Web pages the operator happens to visit must not be able to steer the console: browsers tell cross-site
        // requests apart with these headers, while controllers such as Companion do not send them.
        var site = request.Headers.GetValueOrDefault("Sec-Fetch-Site");
        if (!tokenOk && (request.Headers.ContainsKey("Origin") || (site is not null && site is not ("none" or "same-origin"))))
            return (403, "application/json", Error("Cross-site requests are not allowed"));

        var path = request.Path.TrimEnd('/');
        if (path.Length == 0)
            return (200, "text/html", IndexPage());

        if (_options.Token.Length > 0 && !tokenOk)
            return (401, "application/json", Error("A token is required: add ?token=... or the header X-Token"));

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        if (segments.Count == 0 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) || segments.Count < 2)
            return (404, "application/json", Error("Not found. Open / for the list of endpoints"));

        var verb = segments[1].ToLowerInvariant();
        var args = segments.Skip(2).ToList();

        switch (verb)
        {
            case "state":
                return (200, "application/json", RemoteState.Json(api.Console));
            case "vars":
                return (200, "application/json", JsonSerializer.Serialize(RemoteState.Variables(api.Console)));
            case "var" when args.Count > 0:
            {
                var vars = RemoteState.Variables(api.Console);
                return vars.TryGetValue(string.Join('/', args), out var value)
                    ? (200, "text/plain", value)
                    : (404, "text/plain", "");
            }
            case "help":
                return (200, "text/plain", NxDmx.Core.Commands.CommandInterpreter.HelpText);
        }

        string? line;
        if (verb is "command" or "cmd" or "line")
        {
            line = args.Count > 0 ? string.Join('/', args)
                : request.Query.GetValueOrDefault("line") ?? request.Query.GetValueOrDefault("cmd") ?? BodyLine(request.Body);
        }
        else
        {
            line = RemoteApi.FromShortcut(verb, args);
            if (line is null && request.Query.GetValueOrDefault("value") is { } value)
                line = RemoteApi.FromShortcut(verb, [value]);
        }

        if (string.IsNullOrWhiteSpace(line))
            return (404, "application/json", Error($"Unknown or incomplete request '{request.Path}'"));

        var (success, message) = api.Run($"HTTP {verb}", line);
        return (success ? 200 : 400, "application/json", JsonSerializer.Serialize(new { ok = success, message }));
    }

    private static string? BodyLine(string body)
    {
        var text = body.Trim();
        if (text.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                foreach (var name in new[] { "line", "cmd", "command" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                        return value.GetString();
                }

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return text.Length == 0 ? null : text;
    }

    private static string Error(string message) => JsonSerializer.Serialize(new { ok = false, message });

    private string IndexPage()
    {
        var port = _options.HttpPort;
        return $$"""
            <!doctype html><meta charset="utf-8"><title>NX-DMX remote</title>
            <style>body{background:#000;color:#ccc;font:14px Consolas,monospace;margin:24px;max-width:860px}h1{font-size:18px;color:#fff}
            code{color:#ffb000}td{padding:3px 14px 3px 0;vertical-align:top}</style>
            <h1>NX-DMX remote control</h1>
            <p>Every request runs an ordinary console command. Separate several with <code>;</code>.</p>
            <table>
            <tr><td><code>GET /api/command?line=Go Cuelist 1</code></td><td>run any command (also POST with the line as body)</td></tr>
            <tr><td><code>GET /api/cmd/Fixture 1 Thru 4</code></td><td>same, as path</td></tr>
            <tr><td><code>/api/go/2 /api/back/2 /api/off/2 /api/toggle/2</code></td><td>cuelist playback</td></tr>
            <tr><td><code>/api/group/3 /api/fixture/1/8</code></td><td>select</td></tr>
            <tr><td><code>/api/at/50 /api/red/100 /api/color/Red</code></td><td>values and colours</td></tr>
            <tr><td><code>/api/blackout /api/blackout/on /api/master/80</code></td><td>output</td></tr>
            <tr><td><code>/api/clear /api/clearall /api/oops</code></td><td>programmer</td></tr>
            <tr><td><code>/api/state</code></td><td>state as JSON</td></tr>
            <tr><td><code>/api/vars</code>, <code>/api/var/master</code></td><td>flat variables (Companion)</td></tr>
            </table>
            <p>Example: <code>http://localhost:{{port}}/api/go/1</code></p>
            """;
    }

    // ---- OSC --------------------------------------------------------------------------------------------------

    private void StartOsc(IPAddress address, int port, CancellationToken token)
    {
        var socket = new UdpClient(new IPEndPoint(address, port));
        _stoppers.Add(socket.Dispose);
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try
                {
                    packet = await socket.ReceiveAsync(token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    // Windows reports an ICMP "port unreachable" from an earlier send as an error; carry on.
                    continue;
                }

                foreach (var message in OscMessage.Parse(packet.Buffer))
                {
                    if (RemoteApi.FromOsc(message) is { } line)
                        api.Run("OSC " + message.Address, line);
                }
            }
        }, token);
    }

    // ---- plain text --------------------------------------------------------------------------------------------

    private void StartText(IPAddress address, int port, CancellationToken token)
    {
        var tcp = new TcpListener(address, port);
        tcp.Start();
        _stoppers.Add(tcp.Stop);
        _ = AcceptLoop(tcp, client => ServeText(client, token), token);

        UdpClient udp;
        try
        {
            udp = new UdpClient(new IPEndPoint(address, port));
        }
        catch
        {
            tcp.Stop();
            throw;
        }

        _stoppers.Add(udp.Dispose);
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try
                {
                    packet = await udp.ReceiveAsync(token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(packet.Buffer);
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var (success, message) = api.Run("UDP", line);
                    var reply = Encoding.UTF8.GetBytes($"{(success ? "OK" : "ERR")} {message}\r\n");
                    try
                    {
                        await udp.SendAsync(reply, packet.RemoteEndPoint, token);
                    }
                    catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                    {
                    }
                }
            }
        }, token);
    }

    private async Task ServeText(TcpClient client, CancellationToken token)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        while (!token.IsCancellationRequested)
        {
            // A connection that stays quiet for a long time is closed, so dead controllers do not pile up.
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(TimeSpan.FromMinutes(10));

            string? line;
            try
            {
                line = await reader.ReadLineAsync(idle.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (line is null)
                return;
            if (line.Length > 4096)
            {
                await writer.WriteLineAsync("ERR Line too long");
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var (success, message) = api.Run("TCP", line);
            await writer.WriteLineAsync($"{(success ? "OK" : "ERR")} {message}");
        }
    }
}
