using System.Windows;
using NxDmx.App.Controls;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>The fader bar in a window of its own, for a second screen or a touch screen next to the console.</summary>
public sealed class FaderWindow : DarkWindow
{
    private readonly ConsoleHost _host;

    public FaderWindow(ConsoleHost host)
    {
        _host = host;
        Title = "NX-DMX · Faders";
        Width = 760;
        Height = 330;
        MinWidth = 560;
        MinHeight = 240;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = new FaderBar(host, popped: true);

        // Next to the main window when there is room, otherwise on top of its lower right corner.
        Loaded += (_, _) =>
        {
            if (Owner is { } owner)
            {
                Left = Math.Max(0, owner.Left + owner.ActualWidth - Width - 24);
                Top = Math.Max(0, owner.Top + owner.ActualHeight - Height - 24);
            }
        };
    }

    /// <summary>Set while the program closes, so that closing this window with it does not count as docking the bar.</summary>
    public static bool AppClosing { get; set; }

    protected override bool CloseOnEscape => false;

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Closing the window docks the bar again, unless the program is closing or the bar was hidden.
        if (!AppClosing && _host.Settings.FadersPopped && _host.Settings.FaderBarVisible)
            _host.SetFaderBar(visible: true, popped: false);
    }
}
