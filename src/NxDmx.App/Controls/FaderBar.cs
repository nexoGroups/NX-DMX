using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NxDmx.App.Services;
using NxDmx.App.Views;
using NxDmx.Core.Playback;

namespace NxDmx.App.Controls;

/// <summary>
/// The fader bar, modelled on the faders of a grandMA3: eight faders per page, each with a button above, a fader and
/// a label with its own colour below. Moving the pointer over a fader marks what is on it in the views above. The bar
/// is used docked in the main window and again in its own window; both show the same state.
/// </summary>
public sealed class FaderBar : UserControl
{
    private readonly ConsoleHost _host;
    private readonly bool _popped;
    private readonly Grid _strips = new();
    private readonly TextBlock _page = new() { Foreground = Ui.Brush("DimBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, MinWidth = 62, TextAlignment = TextAlignment.Center };
    private readonly List<FaderControl> _faders = [];
    private readonly List<Button> _buttons = [];
    private readonly List<Border> _frames = [];
    private string _signature = "";

    public FaderBar(ConsoleHost host, bool popped)
    {
        _host = host;
        _popped = popped;
        Focusable = false;

        var root = new DockPanel { Background = Ui.Brush("SurfaceBrush") };

        var header = new DockPanel { Margin = new Thickness(8, 4, 6, 2), Height = 28 };
        DockPanel.SetDock(header, Dock.Top);

        var close = HeaderButton("✕", "Hide the fader bar", () => _host.SetFaderBar(visible: false, popped: false));
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);

        var pop = HeaderButton(popped ? "⮌" : "⧉", popped ? "Dock the fader bar back into the main window" : "Open the fader bar in a window of its own",
            () => _host.SetFaderBar(visible: true, popped: !_popped));
        System.Windows.Automation.AutomationProperties.SetAutomationId(pop, "FaderPopOut");
        DockPanel.SetDock(pop, Dock.Right);
        header.Children.Add(pop);

        var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
        pager.Children.Add(HeaderButton("◀", "Previous page", () => _host.FaderPage--));
        pager.Children.Add(_page);
        pager.Children.Add(HeaderButton("▶", "Next page", () => _host.FaderPage++));
        DockPanel.SetDock(pager, Dock.Right);
        header.Children.Add(pager);

        header.Children.Add(Ui.Caption("Faders", new Thickness(0)));
        root.Children.Add(header);

        for (var i = 0; i < ConsoleHost.FadersPerPage; i++)
            _strips.ColumnDefinitions.Add(new ColumnDefinition());

        root.Children.Add(_strips);
        Content = new Border { BorderBrush = Ui.Brush("LineBrush"), BorderThickness = new Thickness(0, 0, 1, 0), Child = root };

        _host.StateChanged += Refresh;
        _host.FaderLayoutChanged += Refresh;
        Unloaded += (_, _) =>
        {
            _host.StateChanged -= Refresh;
            _host.FaderLayoutChanged -= Refresh;
        };
        Refresh();
    }

    private static Button HeaderButton(string text, string tooltip, Action click)
    {
        var button = Ui.Button(text, click, "FlatButton", tooltip);
        button.MinHeight = 26;
        button.MinWidth = 32;
        button.Padding = new Thickness(6, 0, 6, 0);
        button.Focusable = false;
        return button;
    }

    private IReadOnlyList<FaderInfo> PageFaders =>
        _host.Snapshot.Faders.Skip((_host.FaderPage - 1) * ConsoleHost.FadersPerPage).Take(ConsoleHost.FadersPerPage).ToList();

    private void Refresh()
    {
        _page.Text = $"PAGE {_host.FaderPage} / {ConsoleHost.FaderPages}";
        var faders = PageFaders;

        // A fader that is being dragged keeps its own value until it is released.
        if (_faders.Any(f => f.IsMouseCaptured))
            return;

        var signature = _host.FaderPage + "|" + string.Join('|', faders.Select(f => $"{f.Number}:{f.Kind}:{f.Target}:{f.Text}:{f.Color}:{f.ButtonText}:{f.Exists}"));
        if (signature != _signature)
        {
            _signature = signature;
            Build(faders);
        }

        for (var i = 0; i < faders.Count && i < _faders.Count; i++)
            _faders[i].Level = faders[i].Level;
    }

    private static Brush ColorOf(FaderInfo info)
    {
        if (info.Color.Length > 0 && ColorConverter.ConvertFromString(info.Color) is Color color)
            return new SolidColorBrush(color);

        return Ui.Brush(info.Kind switch
        {
            FaderKind.Cuelist => "GreenBrush",
            FaderKind.Master => "RedBrush",
            _ => "AmberBrush",
        });
    }

    private void Build(IReadOnlyList<FaderInfo> faders)
    {
        _strips.Children.Clear();
        _faders.Clear();
        _buttons.Clear();
        _frames.Clear();

        for (var i = 0; i < faders.Count; i++)
        {
            var info = faders[i];
            var color = ColorOf(info);
            var number = info.Number;

            var cell = new Grid { Margin = new Thickness(2, 2, 2, 4) };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

            var button = new Button
            {
                Content = info.IsAssigned ? info.ButtonText : "",
                Style = Ui.Style("KeyButton"),
                Margin = new Thickness(0, 0, 0, 2),
                IsEnabled = info.IsAssigned && info.Exists,
                FontSize = 10,
                Foreground = info.IsAssigned ? color : Ui.Brush("MutedBrush"),
                BorderBrush = info.IsAssigned ? color : Ui.Brush("LineBrush"),
                ToolTip = info.IsAssigned ? "Press: next cue / blackout / full or out" : "Free fader",
            };
            button.Click += (_, _) => _host.Run($"Fader {number} Button");
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, $"FaderButton{number}");
            _buttons.Add(button);
            cell.Children.Add(button);

            var fader = new FaderControl { Fill = color, IsActive = info.IsActive(), Level = info.Level, Margin = new Thickness(0, 0, 0, 2) };
            var lastSent = -1;
            fader.Moving += percent =>
            {
                if (percent == lastSent)
                    return;

                lastSent = percent;
                _host.RunQuiet($"Fader {number} At {percent}");
            };
            fader.Committed += percent =>
            {
                _host.RunQuiet($"Fader {number} At {percent}");
                _host.RefreshNow();
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(fader, $"Fader{number}");
            Grid.SetRow(fader, 1);
            _faders.Add(fader);
            cell.Children.Add(fader);

            var label = new StackPanel();
            label.Children.Add(new Border { Height = 3, Background = info.IsAssigned ? color : Ui.Brush("LineBrush") });
            label.Children.Add(new TextBlock
            {
                Text = info.IsAssigned ? info.Text : $"Fader {number}",
                Foreground = Ui.Brush(info.IsAssigned ? (info.Exists ? "TextBrush" : "RedBrush") : "MutedBrush"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 28,
                Margin = new Thickness(3, 2, 3, 0),
            });
            if (!info.IsAssigned)
            {
                label.Children.Add(new TextBlock { Text = "tap to assign", Foreground = Ui.Brush("MutedBrush"), FontSize = 9, Margin = new Thickness(3, 0, 3, 0) });
            }

            var labelBox = new Border { Background = Brushes.Transparent, Child = label, Cursor = Cursors.Hand, ToolTip = "Click to assign, rename or colour this fader" };
            labelBox.MouseLeftButtonUp += (_, e) =>
            {
                OpenAssignment(number);
                e.Handled = true;
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(labelBox, $"FaderLabel{number}");
            Grid.SetRow(labelBox, 2);
            cell.Children.Add(labelBox);

            var frame = new Border
            {
                Child = cell,
                BorderThickness = new Thickness(1),
                BorderBrush = Ui.Brush("LineBrush"),
                Background = Ui.Brush("Surface2Brush"),
                Margin = new Thickness(1, 0, 1, 0),
            };
            frame.MouseEnter += (_, _) =>
            {
                frame.BorderBrush = Ui.Brush("BlueBrush");
                _host.SetHighlight(_host.Snapshot.Faders.FirstOrDefault(f => f.Number == number));
            };
            frame.MouseLeave += (_, _) =>
            {
                frame.BorderBrush = Ui.Brush("LineBrush");
                _host.SetHighlight(null);
            };
            frame.MouseRightButtonUp += (_, e) =>
            {
                OpenAssignment(number);
                e.Handled = true;
            };
            _frames.Add(frame);
            Grid.SetColumn(frame, i);
            _strips.Children.Add(frame);
        }
    }

    private void OpenAssignment(int number)
    {
        _host.SetHighlight(null);
        var window = new FaderAssignWindow(_host, number) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}

internal static class FaderInfoExtensions
{
    /// <summary>A fader can be moved when it controls something that exists.</summary>
    public static bool IsActive(this FaderInfo info) => info.IsAssigned && info.Exists;
}
