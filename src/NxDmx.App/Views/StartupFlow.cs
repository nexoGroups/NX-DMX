using System.Windows;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>Shows the start window and does what the operator chose. Used at the start and from the Show view.</summary>
public static class StartupFlow
{
    public static void Show(ConsoleHost host, Window owner)
    {
        var window = new StartupWindow(host) { Owner = owner };
        window.ShowDialog();

        switch (window.Choice)
        {
            case StartupChoice.Prebuilt:
                ShowDialogs.NewPrebuilt(host, owner);
                break;
            case StartupChoice.Empty:
                ShowDialogs.New(host, owner);
                break;
            case StartupChoice.Open:
                ShowDialogs.Open(host, owner);
                break;
            case StartupChoice.Recent when window.RecentPath is { } path:
                if (ShowDialogs.ConfirmDiscard(host, owner))
                    ShowDialogs.OpenFile(host, owner, path);
                break;
        }
    }
}
