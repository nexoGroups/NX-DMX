using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NxDmx.App.Services;
using NxDmx.Core.Commands;
using NxDmx.Core.Playback;

namespace NxDmx.App.Views;

/// <summary>Assign a fader: what it controls, its name and its colour. Applies one command line when confirmed.</summary>
public sealed class FaderAssignWindow : DarkWindow
{
    private readonly ConsoleHost _host;
    private readonly int _number;
    private readonly RadioButton _cuelist = Kind("Cuelist");
    private readonly RadioButton _group = Kind("Group");
    private readonly RadioButton _master = Kind("Master");
    private readonly RadioButton _empty = Kind("Empty");
    private readonly ComboBox _target = new() { MinWidth = 260 };
    private readonly TextBox _label = Ui.Input();
    private readonly WrapPanel _colors = new();
    private string _color = "";

    private sealed record TargetChoice(int Number, string Text)
    {
        public override string ToString() => Text;
    }

    public FaderAssignWindow(ConsoleHost host, int number)
    {
        _host = host;
        _number = number;
        Title = $"Fader {number}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        MinWidth = 420;

        var info = host.Snapshot.Faders.FirstOrDefault(f => f.Number == number);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(Ui.Caption("Fader " + number));
        panel.Children.Add(Ui.Text("What does this fader control?", "DimBrush", margin: new Thickness(0, 0, 0, 8)));

        var kinds = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var kind in new[] { _cuelist, _group, _master, _empty })
        {
            kind.Margin = new Thickness(0, 0, 4, 4);
            kind.Checked += (_, _) => FillTargets();
            kinds.Children.Add(kind);
        }

        panel.Children.Add(kinds);
        panel.Children.Add(Ui.Field("Which one?", _target));
        panel.Children.Add(Ui.Field("Name under the fader (empty = automatic)", _label));

        panel.Children.Add(Ui.Caption("Colour"));
        panel.Children.Add(_colors);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = Ui.Button("Cancel", Close, width: 100);
        var ok = Ui.Button("OK", Apply, "GreenButton", width: 100);
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "FaderAssignOk");
        cancel.Margin = new Thickness(0, 0, 6, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Content = panel;

        _label.Text = info is { IsAssigned: true } ? CustomLabel(info) : "";
        _color = info?.Color ?? "";
        (info?.Kind switch
        {
            FaderKind.Cuelist => _cuelist,
            FaderKind.Group => _group,
            FaderKind.Master => _master,
            _ => _empty,
        }).IsChecked = true;
        FillTargets(info);
        BuildColors();
    }

    private static RadioButton Kind(string text) => new() { Content = text, Style = Ui.Style("ChipRadio"), GroupName = "FaderKind", MinWidth = 90 };

    /// <summary>The label the operator typed, not the automatic one.</summary>
    private string CustomLabel(FaderInfo info)
    {
        var show = _host.Snapshot;
        var automatic = info.Kind switch
        {
            FaderKind.Cuelist => show.Cuelists.FirstOrDefault(l => l.Number == info.Target) is { } c ? $"{c.Number} {c.Label}" : $"Cuelist {info.Target}",
            FaderKind.Group => show.Groups.FirstOrDefault(g => g.Number == info.Target) is { } g ? $"{g.Number} {g.Label}" : $"Group {info.Target}",
            _ => "Master",
        };
        return info.Text == automatic ? "" : info.Text;
    }

    private void FillTargets(FaderInfo? current = null)
    {
        var snapshot = _host.Snapshot;
        List<TargetChoice> choices = _cuelist.IsChecked == true
            ? snapshot.Cuelists.Select(l => new TargetChoice(l.Number, $"Cuelist {l.Number} · {l.Label}")).ToList()
            : _group.IsChecked == true
                ? snapshot.Groups.Select(g => new TargetChoice(g.Number, $"Group {g.Number} · {g.Label}")).ToList()
                : [];

        _target.ItemsSource = choices;
        _target.IsEnabled = choices.Count > 0;
        _target.SelectedItem = choices.FirstOrDefault(c => current is not null && c.Number == current.Target) ?? choices.FirstOrDefault();
    }

    private void BuildColors()
    {
        _colors.Children.Clear();
        _colors.Children.Add(Swatch("", "Default colour", Ui.Brush("MutedBrush")));
        foreach (var color in _host.Snapshot.Colors)
            _colors.Children.Add(Swatch(color.Hex, color.Name, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color.Hex))));
    }

    private Button Swatch(string hex, string name, Brush brush)
    {
        var selected = string.Equals(hex, _color, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Width = 44,
            Height = 40,
            Margin = new Thickness(0, 0, 4, 4),
            Background = brush,
            BorderBrush = Ui.Brush(selected ? "AmberBrush" : "LineBrush"),
            BorderThickness = new Thickness(selected ? 3 : 1),
            ToolTip = name,
            Content = hex.Length == 0 ? new TextBlock { Text = "auto", FontSize = 9, Foreground = Brushes.Black } : null,
            Focusable = false,
        };
        button.Click += (_, _) =>
        {
            _color = hex;
            BuildColors();
        };
        return button;
    }

    private void Apply()
    {
        var lines = new List<string>();
        if (_empty.IsChecked == true)
        {
            lines.Add($"Fader {_number} Clear");
        }
        else
        {
            if (_master.IsChecked == true)
            {
                lines.Add($"Fader {_number} Master");
            }
            else
            {
                if (_target.SelectedItem is not TargetChoice target)
                {
                    MessageBox.Show(this, "There is nothing to choose yet. Record a cuelist or a group first, or pick Master.", "Fader", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                lines.Add($"Fader {_number} {(_cuelist.IsChecked == true ? "Cuelist" : "Group")} {target.Number}");
            }

            lines.Add($"Fader {_number} Label {CommandText.Quote(_label.Text.Trim())}");
            lines.Add($"Fader {_number} Color {(_color.Length == 0 ? "Default" : _color)}");
        }

        _host.Run(string.Join("; ", lines));
        Close();
    }
}
