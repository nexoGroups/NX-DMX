using System.IO;
using System.Windows;
using System.Windows.Controls;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

public enum StartupChoice
{
    /// <summary>The window was closed: the show on screen stays as it is.</summary>
    None,
    Prebuilt,
    Empty,
    Open,
    Recent,
}

/// <summary>
/// The window at the start: carry on with the show of the last session, begin a new pre-built or empty show, open a show
/// file, or one of the recent ones. It only asks; <see cref="StartupFlow"/> does what was chosen.
/// </summary>
public sealed class StartupWindow : DarkWindow
{
    private const double TileTextWidth = 424;

    private readonly ConsoleHost _host;
    private readonly CheckBox _again = new() { Content = "Show this window when NX-DMX starts", IsChecked = true, VerticalContentAlignment = VerticalAlignment.Center };

    public StartupWindow(ConsoleHost host)
    {
        _host = host;
        Title = "NX-DMX";
        SizeToContent = SizeToContent.Height;
        Width = 520;
        ResizeMode = ResizeMode.NoResize;
        _again.IsChecked = host.Settings.ShowStartWindow;

        var panel = new StackPanel { Margin = new Thickness(22) };
        var heading = Ui.Text($"NX-DMX {host.Updates.CurrentVersion}", "TextBrush", wrap: false);
        heading.Style = Ui.Style("Heading");
        heading.FontSize = 20;
        panel.Children.Add(heading);
        panel.Children.Add(Ui.Text("What do you want to do?", "DimBrush", margin: new Thickness(0, 2, 0, 14)));

        if (host.RestoredSession)
        {
            var snapshot = host.Snapshot;
            var detail = $"{host.ShowTitle} · {snapshot.Fixtures.Count} fixtures · {snapshot.Cuelists.Count} cuelist{(snapshot.Cuelists.Count == 1 ? "" : "s")}"
                + (host.IsDirty ? " · unsaved changes" : "");
            var carryOn = Tile("Continue where you left off", detail, Close, "GreenButton");
            carryOn.IsDefault = true;
            System.Windows.Automation.AutomationProperties.SetAutomationId(carryOn, "StartContinue");
            panel.Children.Add(carryOn);
        }

        panel.Children.Add(Tile("New pre-built show", "A small rig to try things at once: 8 RGB washes, 4 movers, 4 dimmers, groups and a cuelist.", () => Pick(StartupChoice.Prebuilt), automationId: "StartPrebuilt"));
        panel.Children.Add(Tile("New empty show", "Start with nothing. Add universes and patch your own fixtures.", () => Pick(StartupChoice.Empty), automationId: "StartEmpty"));
        panel.Children.Add(Tile("Open show…", "Load a show file (.nxshow) from this computer.", () => Pick(StartupChoice.Open), automationId: "StartOpen"));

        var recent = host.RecentShows.Take(5).ToList();
        if (recent.Count > 0)
        {
            panel.Children.Add(Ui.Caption("Recent shows", new Thickness(0, 10, 0, 4)));
            foreach (var path in recent)
            {
                var item = Tile(Path.GetFileNameWithoutExtension(path), path, () => PickRecent(path), compact: true);
                item.ToolTip = path;
                panel.Children.Add(item);
            }
        }

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var close = Ui.Button("Close", Close, width: 100);
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(_again);
        panel.Children.Add(footer);
        Content = panel;

        System.Windows.Automation.AutomationProperties.SetAutomationId(close, "StartClose");
        Closed += (_, _) =>
        {
            if (host.Settings.ShowStartWindow != (_again.IsChecked == true))
            {
                host.Settings.ShowStartWindow = _again.IsChecked == true;
                host.SaveSettings();
            }
        };
    }

    public StartupChoice Choice { get; private set; }

    public string? RecentPath { get; private set; }

    private static Button Tile(string title, string detail, Action click, string? style = null, bool compact = false, string? automationId = null)
    {
        // The button template centres its content, so the text block gets the full width of the tile itself: the window cannot be resized.
        var text = new StackPanel { Width = TileTextWidth, HorizontalAlignment = HorizontalAlignment.Left };
        text.Children.Add(new TextBlock { Text = title, FontSize = compact ? 12.5 : 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Ui.Brush("DimBrush"), TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });

        var button = Ui.Button("", click, style);
        button.Content = text;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Padding = new Thickness(14, compact ? 6 : 10, 14, compact ? 6 : 10);
        button.Margin = new Thickness(0, 0, 0, 6);
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        if (automationId is not null)
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);

        return button;
    }

    private void Pick(StartupChoice choice)
    {
        Choice = choice;
        Close();
    }

    private void PickRecent(string path)
    {
        RecentPath = path;
        Pick(StartupChoice.Recent);
    }
}
