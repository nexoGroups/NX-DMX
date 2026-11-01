using NxDmx.Core.Commands;
using NxDmx.Core.Dmx;
using NxDmx.Core.Engine;
using NxDmx.Core.Programming;

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
