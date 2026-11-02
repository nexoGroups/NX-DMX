using NxDmx.Core.Commands;
using NxDmx.Core.Dmx;
using NxDmx.Core.Engine;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Programming;
using NxDmx.Core.Storage;

namespace NxDmx.Core;

/// <summary>
/// The console: show data, programmer and command line in one place. The UI thread executes commands while the
/// output thread renders frames and the remote servers run commands of their own, so every access to the state goes
/// through the lock held here.
/// </summary>
public sealed class LightingConsole
{
    private readonly object _gate = new();
    private readonly CommandInterpreter _interpreter;

    public LightingConsole()
    {
        Show = new Show();
        Programmer = new Programmer();
        Output = new OutputState();
        _interpreter = new CommandInterpreter(Show, Programmer, Output);
    }

    // Reach these only through Read / Execute / Render so the lock is held.
    private Show Show { get; }

    private Programmer Programmer { get; }

    private OutputState Output { get; }

    /// <summary>
    /// Raised on the calling thread after a command ran. That can be any thread (the remote servers call in from
    /// theirs), so handlers must hand over to their own thread before touching a window.
    /// </summary>
    public event Action? Changed;

    public CommandResult Execute(string line)
    {
        CommandResult result;
        lock (_gate)
            result = _interpreter.Execute(line);

        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// Runs several commands one after the other, separated by <c>;</c> (outside quotes), and stops at the first one
    /// that fails. One button can do <c>Group 1; Color Red; Record Cue 3</c>.
    /// </summary>
    public IReadOnlyList<(string Line, CommandResult Result)> ExecuteMany(string lines)
    {
        var parts = CommandText.Split(lines);
        var results = new List<(string, CommandResult)>();
        if (Preflight(parts) is { Success: false } refused)
        {
            results.Add((lines, refused));
            return results;
        }

        foreach (var line in parts)
        {
            var result = Execute(line);
            results.Add((line, result));
            if (!result.Success)
                break;
        }

        return results;
    }

    /// <summary>
    /// Tries a series of commands on a copy of the console before any of them runs for real, so that a series which
    /// would fail half-way changes nothing at all. Commands that act on what is playing (Go, a fader move) cannot be
    /// tried on a copy and are left out. The answer names the command that does not work.
    /// </summary>
    public CommandResult Preflight(IReadOnlyList<string> lines)
    {
        if (lines.Count(line => !CommandInterpreter.IsLive(line)) < 2)
            return CommandResult.Ok();

        string json;
        List<int> selection;
        List<(Programming.ValueKey Key, double Value)> values;
        lock (_gate)
        {
            json = ShowFile.Serialize(Show);
            selection = [.. Programmer.Selection];
            values = Programmer.Values.Select(v => (v.Key, v.Value)).ToList();
        }

        var copy = new LightingConsole();
        copy.ImportShow(json);
        copy.Programmer.Select(selection);
        foreach (var (key, value) in values)
            copy.Programmer.Set(key.FixtureId, key.Attribute, value);

        for (var i = 0; i < lines.Count; i++)
        {
            if (CommandInterpreter.IsLive(lines[i]))
                continue;

            var result = copy.Execute(lines[i]);
            if (!result.Success)
            {
                var text = result.Refused ? result.Message[CommandResult.RefusedPrefix.Length..] : result.Message;
                var message = $"Step {i + 1} of {lines.Count} (\"{lines[i]}\"): {text}. Nothing was changed";
                return result.Refused ? CommandResult.Refuse(message) : CommandResult.Error(message);
            }
        }

        return CommandResult.Ok();
    }

    /// <summary>Stores a fixture profile in the show (see <see cref="CommandInterpreter.SaveProfile"/>).</summary>
    public CommandResult SaveProfile(string? originalName, FixtureType type)
    {
        CommandResult result;
        lock (_gate)
            result = _interpreter.SaveProfile(originalName, type);

        Changed?.Invoke();
        return result;
    }

    public CommandResult DeleteProfile(string name) => Locked(() => _interpreter.DeleteProfile(name));

    /// <summary>Adds or replaces one of the show's own colours (<paramref name="hex"/> is <c>#RRGGBB</c>).</summary>
    public CommandResult SaveColor(string name, string hex) => Locked(() => _interpreter.SaveColor(name, hex));

    public CommandResult DeleteColor(string name) => Locked(() => _interpreter.DeleteColor(name));

    /// <summary>Adds many colours at once; one Oops takes the whole import back.</summary>
    public CommandResult ImportColors(IReadOnlyList<(string Name, string Hex)> colors) => Locked(() => _interpreter.ImportColors(colors));

    private CommandResult Locked(Func<CommandResult> action)
    {
        CommandResult result;
        lock (_gate)
            result = action();

        Changed?.Invoke();
        return result;
    }

    /// <summary>Forgets all Oops steps, so Oops cannot undo what was set up before the operator started.</summary>
    public void ClearHistory()
    {
        lock (_gate)
            _interpreter.ClearHistory();
    }

    /// <summary>The show (patch, groups, cuelists, universes, profiles) as the text of a show file.</summary>
    public string ExportShow(IProgress<double>? progress = null)
    {
        lock (_gate)
            return ShowFile.Serialize(Show, progress);
    }

    /// <summary>
    /// Replaces the whole show with the one in the text. The programmer is emptied, all cuelists are off and Oops
    /// forgets its history. Throws <see cref="ShowFileException"/> and changes nothing when the text is not valid.
    /// </summary>
    public void ImportShow(string json, IProgress<double>? progress = null)
    {
        // The slow part, reading the file into a new show, runs before the lock: the output keeps going meanwhile.
        var loaded = ShowFile.Deserialize(json, progress);

        lock (_gate)
        {
            Show.ReplaceWith(loaded);
            Programmer.ClearSelection();
            Programmer.ClearValues();
            _interpreter.ClearHistory();
        }

        Changed?.Invoke();
    }

    /// <summary>Reads the state under the lock. Return a copy; do not keep references to live objects.</summary>
    public T Read<T>(Func<Show, Programmer, T> reader)
    {
        lock (_gate)
            return reader(Show, Programmer);
    }

    /// <summary>Like <see cref="Read{T}(Func{Show,Programmer,T})"/>, with the output controls as well.</summary>
    public T Read<T>(Func<Show, Programmer, OutputState, T> reader)
    {
        lock (_gate)
            return reader(Show, Programmer, Output);
    }

    public DmxFrame Render()
    {
        lock (_gate)
            return FrameRenderer.Render(Show, Programmer, Output);
    }
}
