using System.Globalization;
using NxDmx.Core.Commands;
using NxDmx.Core.Fixtures;

namespace NxDmx.Core.Remote;

/// <summary>One command that came in from a remote client, for the activity list.</summary>
public sealed record RemoteActivity(DateTime Time, string Source, string Line, bool Success, string Message)
{
    /// <summary>One line for the activity list: time, where it came from, the command and the error if it failed.</summary>
    public string Display => $"{Time:HH:mm:ss}  {Source,-10} {Line}{(Success ? "" : "   ✗ " + Message)}";
}

/// <summary>
/// What all remote transports (HTTP, OSC, text over TCP/UDP) have in common: they turn a request into command lines,
/// run them on the console and report each one. Everything a remote client does is an ordinary console command, so
/// anything that can be typed can be put on a Stream Deck button.
/// </summary>
public sealed class RemoteApi(LightingConsole console)
{
    /// <summary>Raised from the transport's thread for every command, successful or not.</summary>
    public event Action<RemoteActivity>? Activity;

    public LightingConsole Console => console;

    /// <summary>Runs one or several commands (separated by <c>;</c>) and returns the combined result.</summary>
    public (bool Success, string Message) Run(string source, string text)
    {
        var lines = CommandText.Split(text);
        if (lines.Count == 0)
            return (false, "No command given");

        // A series is tried on a copy first, so a remote button never leaves half of its commands applied.
        if (console.Preflight(lines) is { Success: false } refused)
        {
            Activity?.Invoke(new RemoteActivity(DateTime.Now, source, text, false, refused.Message));
            return (false, refused.Message);
        }

        var messages = new List<string>();
        foreach (var line in lines)
        {
            var result = console.Execute(line);
            Activity?.Invoke(new RemoteActivity(DateTime.Now, source, line, result.Success, result.Message));
            if (result.Message.Length > 0)
                messages.Add(result.Message);
            if (!result.Success)
                return (false, string.Join(" | ", messages));
        }

        return (true, string.Join(" | ", messages));
    }

    // ---- shortcuts used by the HTTP paths and OSC ------------------------------------------------------------

    /// <summary>
    /// Turns <c>/go/2</c>, <c>/group/3</c>, <c>/blackout/on</c> ... (the part after <c>/api/</c>) into a command line.
    /// Returns null when the verb is unknown.
    /// </summary>
    public static string? FromShortcut(string verb, IReadOnlyList<string> args)
    {
        string Arg(int i) => i < args.Count ? args[i].Trim() : "";
        bool HasNumber(int i) => i < args.Count && double.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        switch (verb.ToLowerInvariant())
        {
            case "go" or "back" or "off" or "toggle":
            {
                var name = char.ToUpperInvariant(verb[0]) + verb[1..].ToLowerInvariant();
                return HasNumber(0) ? $"{name} Cuelist {Whole(Arg(0))}" : name;
            }
            case "goto" when HasNumber(0):
                return $"Goto Cue {Arg(0)}" + (HasNumber(1) ? $" Cuelist {Whole(Arg(1))}" : "");
            case "group" when HasNumber(0):
                return $"Group {Whole(Arg(0))}" + (HasNumber(1) ? $" Thru {Whole(Arg(1))}" : "");
            case "fixture" when HasNumber(0):
                return $"Fixture {Whole(Arg(0))}" + (HasNumber(1) ? $" Thru {Whole(Arg(1))}" : "");
            case "at" when HasNumber(0):
                return $"At {Arg(0)}";
            case "color" or "colour" when Arg(0).Length > 0:
                return $"Color {CommandText.Quote(string.Join(' ', args))}";
            case "blackout":
                return Arg(0) switch
                {
                    "1" or "on" or "true" => "Blackout On",
                    "0" or "off" or "false" => "Blackout Off",
                    _ => "Blackout",
                };
            case "master" when HasNumber(0):
                return $"Master At {Arg(0)}";
            case "fader" when HasNumber(0):
                return HasNumber(1) ? $"Fader {Whole(Arg(0))} At {Arg(1)}" : $"Fader {Whole(Arg(0))} Button";
            case "clear":
                return "Clear";
            case "clearall":
                return "ClearAll";
            case "oops":
                return "Oops";
            case "cmd" or "command" when args.Count > 0:
                return string.Join(' ', args);
        }

        // /red/100 -> Red At 100
        if (FixtureAttributeExtensions.TryParse(verb, out var attribute) && HasNumber(0))
            return $"{attribute} At {Arg(0)}";

        return null;
    }

    private static string Whole(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? ((long)Math.Round(n)).ToString(CultureInfo.InvariantCulture)
            : text;

    // ---- OSC --------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>/nxdmx/cmd "Go Cuelist 1"</c>, <c>/nxdmx/go 2</c>, <c>/nxdmx/group 3</c>, <c>/nxdmx/red 100</c> ...
    /// Returns the command line the message means, or null when it does not map to one.
    /// </summary>
    public static string? FromOsc(OscMessage message)
    {
        var parts = message.Address.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("nxdmx", StringComparison.OrdinalIgnoreCase))
            return null;

        // Arguments in the address (/nxdmx/group/3) win; the message's own values are used when the address has none.
        var args = new List<string>(parts.Skip(2));
        if (args.Count == 0)
        {
            for (var i = 0; i < message.Arguments.Count; i++)
            {
                if (message.Text(i) is { } text)
                    args.Add(text);
            }
        }

        // A button sends 1 on press and 0 on release; the release does nothing (and "go 1" is just Cuelist 1).
        var verb = parts[1].ToLowerInvariant();
        if (verb != "blackout" && message.Arguments.Count == 1 && message.Number(0) == 0
            && verb is "go" or "back" or "off" or "toggle" or "clear" or "clearall" or "oops" or "group" or "fixture")
        {
            return null;
        }

        // /nxdmx/fader/3 0.6 moves fader 3: here the number in the address and the value of the message belong together.
        if (verb == "fader" && parts.Length > 2 && message.Text(0) is { } faderValue)
            args.Add(faderValue);

        return FromShortcut(verb, args);
    }
}
