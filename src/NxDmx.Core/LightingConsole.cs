using NxDmx.Core.Commands;
using NxDmx.Core.Dmx;
using NxDmx.Core.Engine;
using NxDmx.Core.Programming;
using NxDmx.Core.Storage;

namespace NxDmx.Core;

/// <summary>
/// The console: show data, programmer and command line in one place. The UI thread executes commands while the
/// output thread renders frames, so every access to the state goes through the lock held here.
/// </summary>
public sealed class LightingConsole
{
    private readonly object _gate = new();
    private readonly CommandInterpreter _interpreter;

    public LightingConsole()
    {
        Show = new Show();
        Programmer = new Programmer();
        _interpreter = new CommandInterpreter(Show, Programmer);
    }

    // Reach these only through Read / Execute / Render so the lock is held.
    private Show Show { get; }

    private Programmer Programmer { get; }

    /// <summary>Raised on the calling thread after a command ran.</summary>
    public event Action? Changed;

    public CommandResult Execute(string line)
    {
        CommandResult result;
        lock (_gate)
            result = _interpreter.Execute(line);

        Changed?.Invoke();
        return result;
    }

    /// <summary>Forgets all Oops steps, so Oops cannot undo what was set up before the operator started.</summary>
    public void ClearHistory()
    {
        lock (_gate)
            _interpreter.ClearHistory();
    }

    /// <summary>The show (patch, groups, cuelists) as the text of a show file.</summary>
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

    public DmxFrame Render()
    {
        lock (_gate)
            return FrameRenderer.Render(Show, Programmer);
    }
}
