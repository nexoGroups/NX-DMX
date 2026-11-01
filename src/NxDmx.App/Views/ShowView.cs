using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>The show file: new, open, save, and a word on the automatic backup.</summary>
public sealed class ShowView : ConsoleView
{
    private readonly TextBlock _name = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("TextBrush") };
    private readonly TextBlock _path = Ui.Text("", "DimBrush");
    private readonly TextBlock _counts = Ui.Text("", "DimBrush", margin: new Thickness(0, 10, 0, 0));
    private readonly TextBlock _dirty = Ui.Text("", "AmberBrush");

    public ShowView(ConsoleHost host) : base(host)
    {
        var panel = new StackPanel { Margin = new Thickness(18), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Ui.Caption("Current show"));
        panel.Children.Add(_name);
        panel.Children.Add(_dirty);
        panel.Children.Add(_path);
        panel.Children.Add(_counts);

        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Wide("New", () => ShowDialogs.New(Host, Window.GetWindow(this)!), "Ctrl+N · an empty show"));
        buttons.Children.Add(Wide("Open…", () => ShowDialogs.Open(Host, Window.GetWindow(this)!), "Ctrl+O"));
        buttons.Children.Add(Wide("Save", () => ShowDialogs.Save(Host, Window.GetWindow(this)!), "Ctrl+S", "GreenButton"));
        buttons.Children.Add(Wide("Save as…", () => ShowDialogs.SaveAs(Host, Window.GetWindow(this)!), "Ctrl+Shift+S"));
        panel.Children.Add(buttons);

        panel.Children.Add(new Border { Height = 1, Background = Ui.Brush("LineBrush"), Margin = new Thickness(0, 22, 0, 16) });
        panel.Children.Add(Ui.Caption("Automatic backup"));
        panel.Children.Add(Ui.Text("The show is also written to a backup file after every change. After a crash, a power cut or an update restart, " +
            "NX-DMX comes back with the show of your last session. The programmer and the cuelist positions start empty.", "DimBrush"));
        var folder = Ui.Button("Open backup folder", OpenBackupFolder, tooltip: "Where the automatic backup and settings are stored");
        folder.Margin = new Thickness(0, 10, 0, 0);
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(folder);

        panel.Children.Add(new Border { Height = 1, Background = Ui.Brush("LineBrush"), Margin = new Thickness(0, 22, 0, 16) });
        panel.Children.Add(Ui.Caption("What a show contains"));
        panel.Children.Add(Ui.Text("Universes and their destinations, your own fixture profiles, the patch, groups, cuelists and cues. " +
            "Not saved with the show: the programmer, blackout, the grand master and network settings of this computer.", "DimBrush"));

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }

    private static Button Wide(string text, Action click, string tooltip, string? style = null)
    {
        var button = Ui.Button(text, click, style, tooltip, 120);
        button.Margin = new Thickness(0, 0, 6, 0);
        return button;
    }

    public override void Refresh()
    {
        _name.Text = Host.ShowTitle;
        _dirty.Text = Host.IsDirty ? "Unsaved changes" : "";
        _path.Text = Host.ShowPath ?? "Not saved to a file yet.";

        var s = Host.Snapshot;
        _counts.Text = $"{s.Universes.Count} universe{(s.Universes.Count == 1 ? "" : "s")} · {s.Fixtures.Count} fixtures · {s.Groups.Count} groups · " +
            $"{s.Cuelists.Count} cuelist{(s.Cuelists.Count == 1 ? "" : "s")} · {s.Profiles.Count(p => !p.Type.IsBuiltIn)} own profiles";
    }

    public override void Tick() => Refresh();

    private void OpenBackupFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(ShowStorage.SettingsPath)!;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Host.AddLog($"Could not open the folder: {ex.Message}", error: true);
        }
    }
}
