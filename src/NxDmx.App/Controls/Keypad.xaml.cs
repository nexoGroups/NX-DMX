using System.Windows;
using System.Windows.Controls;
using NxDmx.App.Services;

namespace NxDmx.App.Controls;

/// <summary>The console keys. Each key types its word into the command line; some act at once, like Oops.</summary>
public partial class Keypad : UserControl
{
    private ConsoleHost? _host;

    public Keypad()
    {
        InitializeComponent();
    }

    public void Attach(ConsoleHost host) => _host = host;

    private static string TagOf(object sender) => (string)((FrameworkElement)sender).Tag;

    private void Insert_Click(object sender, RoutedEventArgs e) => _host?.Insert(TagOf(sender));

    /// <summary>Keys that act at once. With text in the command line they add to it instead, so a line is never lost.</summary>
    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null)
            return;

        var command = TagOf(sender);
        if (_host.CommandLine.Trim().Length > 0 && command is not ("Oops" or "ClearAll" or "Help" or "Blackout"))
            _host.Insert(command);
        else
            _host.Run(command);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null)
            return;

        if (_host.CommandLine.Length > 0)
            _host.CommandLine = "";
        else
            _host.Run("Clear");
    }

    private void Backspace_Click(object sender, RoutedEventArgs e)
    {
        if (_host is { CommandLine.Length: > 0 })
            _host.CommandLine = _host.CommandLine[..^1];
    }

    private void Please_Click(object sender, RoutedEventArgs e) => _host?.Submit();
}
