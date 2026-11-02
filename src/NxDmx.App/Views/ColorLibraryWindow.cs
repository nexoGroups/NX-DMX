using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NxDmx.App.Controls;
using NxDmx.App.Services;
using NxDmx.Core.Programming;
using NxDmx.Core.Storage;

namespace NxDmx.App.Views;

/// <summary>
/// The colour library: add your own colours (typed as hex or mixed with three bars), delete them, and import or export
/// colour lists. Colours are stored in the show and are available to the Color command and the colour page.
/// </summary>
public sealed class ColorLibraryWindow : DarkWindow
{
    private const string FileFilter = "Colour list (*.nxcolors;*.txt;*.csv)|*.nxcolors;*.txt;*.csv|All files (*.*)|*.*";

    private readonly ConsoleHost _host;
    private readonly StackPanel _list = new();
    private readonly TextBox _name = Ui.Input();
    private readonly TextBox _hex = Ui.Input("#3399FF");
    private readonly Border _preview = new() { Height = 40, BorderBrush = Ui.Brush("LineBrush"), BorderThickness = new Thickness(1) };
    private readonly LevelBar _red = new() { Caption = "Red", Fill = Ui.Brush("RedBrush") };
    private readonly LevelBar _green = new() { Caption = "Green", Fill = Ui.Brush("GreenBrush") };
    private readonly LevelBar _blue = new() { Caption = "Blue", Fill = Ui.Brush("BlueBrush") };
    private readonly TextBlock _message = Ui.Text("", "DimBrush");

    public ColorLibraryWindow(ConsoleHost host)
    {
        _host = host;
        Title = "Colours";
        Width = 760;
        Height = 620;
        MinWidth = 640;
        MinHeight = 480;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        // Left: your colours.
        var left = new DockPanel();
        var listHeader = Ui.Caption("Your colours");
        DockPanel.SetDock(listHeader, Dock.Top);
        left.Children.Add(listHeader);
        var transfer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        transfer.Children.Add(Ui.Button("Import…", Import, tooltip: "Load colours from a .nxcolors file or a text list: Name;#RRGGBB per line"));
        var export = Ui.Button("Export…", Export, tooltip: "Save your colours as a .nxcolors file");
        export.Margin = new Thickness(6, 0, 0, 0);
        transfer.Children.Add(export);
        DockPanel.SetDock(transfer, Dock.Bottom);
        left.Children.Add(transfer);
        left.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        grid.Children.Add(left);

        // Right: a new colour.
        var right = new StackPanel();
        right.Children.Add(Ui.Caption("New colour"));
        right.Children.Add(Ui.Field("Name", _name));
        right.Children.Add(Ui.Field("Colour as hex (#RRGGBB)", _hex));
        _preview.Margin = new Thickness(0, 0, 0, 8);
        right.Children.Add(_preview);
        foreach (var bar in new[] { _red, _green, _blue })
        {
            bar.Margin = new Thickness(0, 0, 0, 4);
            bar.Committed += _ => MixFromBars();
            right.Children.Add(bar);
        }

        var add = Ui.Button("Add colour", Add, "GreenButton", "Save the colour in the show");
        add.Margin = new Thickness(0, 8, 0, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(add, "AddColor");
        right.Children.Add(add);
        _message.Margin = new Thickness(0, 10, 0, 0);
        right.Children.Add(_message);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var root = new DockPanel();
        var close = Ui.Button("Close", Close, width: 100);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(16, 0, 16, 14);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(grid);
        Content = root;

        _hex.TextChanged += (_, _) => ShowHex();
        _host.StateChanged += Rebuild;
        Closed += (_, _) => _host.StateChanged -= Rebuild;
        ShowHex();
        Rebuild();
    }

    private void Say(string text, bool error = false)
    {
        _message.Text = text;
        _message.Foreground = Ui.Brush(error ? "RedBrush" : "DimBrush");
    }

    private void ShowHex()
    {

        if (ColorPreset.TryParseHex(_hex.Text, out var r, out var g, out var b))
        {
            _preview.Background = new SolidColorBrush(Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255)));
            (_red.Value, _green.Value, _blue.Value) = (r, g, b);
            _hex.BorderBrush = Ui.Brush("LineBrush");
        }
        else
        {
            _preview.Background = Brushes.Transparent;
            _hex.BorderBrush = Ui.Brush("RedBrush");
        }
    }

    private void MixFromBars()
    {
        string Channel(LevelBar bar) => ((int)Math.Round((bar.Value ?? 0) * 255)).ToString("X2");

        _hex.Text = $"#{Channel(_red)}{Channel(_green)}{Channel(_blue)}";
    }

    private void Add()
    {
        var result = _host.Console.SaveColor(_name.Text, _hex.Text);
        _host.AddLog(result.Message, error: !result.Success);
        Say(result.Message, !result.Success);
        if (result.Success)
            _name.Text = "";
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var own = _host.Snapshot.Colors.Where(c => c.Custom).ToList();
        if (own.Count == 0)
            _list.Children.Add(Ui.Text("You have no colours of your own yet. Add one on the right, or import a list.", "MutedBrush"));

        foreach (var color in own)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4), Height = 38 };
            var delete = Ui.Button("✕", () => Delete(color.Name), "RedButton", "Delete this colour");
            delete.Width = 40;
            delete.MinHeight = 34;
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(delete);
            row.Children.Add(new Border
            {
                Width = 56,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color.Hex)),
                BorderBrush = Ui.Brush("LineStrongBrush"),
                BorderThickness = new Thickness(1),
            });
            row.Children.Add(new TextBlock { Text = color.Name, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            _list.Children.Add(row);
        }
    }

    private void Delete(string name)
    {
        var result = _host.Console.DeleteColor(name);
        _host.AddLog(result.Message, error: !result.Success);
        Say(result.Message, !result.Success);
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = FileFilter, Title = "Import colours" };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var colors = ColorFile.Parse(ShowStorage.Read(dialog.FileName));
            var result = _host.Console.ImportColors(colors);
            _host.AddLog(result.Message, error: !result.Success);
            Say(result.Success ? $"{result.Message} from {Path.GetFileName(dialog.FileName)}." : result.Message, !result.Success);
        }
        catch (Exception ex) when (ex is ShowFileException or IOException or UnauthorizedAccessException)
        {
            _host.AddLog($"Could not import {Path.GetFileName(dialog.FileName)}: {ex.Message}", error: true);
            Say(ex.Message, error: true);
        }
    }

    private void Export()
    {
        var own = _host.Console.Read((show, _) => show.CustomColors.Values.ToList());
        if (own.Count == 0)
        {
            Say("There are no colours of your own to export.", error: true);
            return;
        }

        var dialog = new SaveFileDialog { Filter = FileFilter, DefaultExt = ColorFile.Extension, AddExtension = true, FileName = "My colours", Title = "Export colours" };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            ShowStorage.Write(dialog.FileName, ColorFile.Serialize(own));
            Say($"Exported {own.Count} colour{(own.Count == 1 ? "" : "s")} to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say(ex.Message, error: true);
        }
    }
}
