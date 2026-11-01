using System.Windows.Controls;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>
/// Base of the main views. A view builds itself once, then gets <see cref="Refresh"/> whenever the console changed
/// while it is on screen, and <see cref="Tick"/> a few times a second for live displays such as the DMX monitor.
/// </summary>
public abstract class ConsoleView : UserControl
{
    protected ConsoleView(ConsoleHost host)
    {
        Host = host;
        Focusable = false;
    }

    protected ConsoleHost Host { get; }

    public virtual void Refresh()
    {
    }

    public virtual void Tick()
    {
    }

    /// <summary>Called when the view is shown, after it was hidden or not yet built.</summary>
    public virtual void Activated() => Refresh();
}
