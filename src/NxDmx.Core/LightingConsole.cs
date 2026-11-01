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
        var results = new List<(string, CommandResult)>();
        foreach (var line in CommandText.Split(lines))
        {
            var result = Execute(line);
            results.Add((line, result));
            if (!result.Success)
                break;
        }

        return results;
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

    public CommandResult DeleteProfile(string name)
    {
        CommandResult result;
        lock (_gate)
            result = _interpreter.DeleteProfile(name);

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
    public string ExportShow()
    {
        lock (_gate)
            return ShowFile.Serialize(Show);
    }

    /// <summary>
    /// Replaces the whole show with the one in the text. The programmer is emptied, all cuelists are off and Oops
    /// forgets its history. Throws <see cref="ShowFileException"/> and changes nothing when the text is not valid.
    /// </summary>
    public void ImportShow(string json)
    {
        var loaded = ShowFile.Deserialize(json);

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
