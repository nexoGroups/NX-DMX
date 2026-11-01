using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>Groups of fixtures as big tiles. Click to select; Record, Label and Delete work on a group by clicking it afterwards.</summary>
public sealed class GroupsView : ConsoleView
{
    private readonly WrapPanel _tiles = new() { Margin = new Thickness(10) };
    private string _signature = "";

    public GroupsView(ConsoleHost host) : base(host)
    {
        var root = new DockPanel();

        var bar = new WrapPanel { Margin = new Thickness(10, 8, 10, 8) };
        bar.Children.Add(Ui.Text("Store the selection:", "DimBrush", wrap: false, margin: new Thickness(0, 5, 8, 0)));
        bar.Children.Add(Ui.Button("Record", () => Host.Insert("Record "), "KeyRecord", "Press Record, then click a group (or the + tile) to store the current selection there", 80));
        bar.Children.Add(Ui.Button("Label", () => Host.Insert("Label "), "KeyButton", "Press Label, click a group, then type the name in quotes", 80));
        bar.Children.Add(Ui.Button("Delete", () => Host.Insert("Delete "), "KeyButton", "Press Delete, then click a group", 80));
        foreach (var child in bar.Children.OfType<Button>())
            child.Margin = new Thickness(0, 0, 4, 0);

        var header = new Border { Style = Ui.Style("PanelBorder"), BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = root;
    }

    public override void Refresh()
    {
        var snapshot = Host.Snapshot;
        var signature = string.Join('|', snapshot.Groups.Select(g => $"{g.Number}:{g.Label}:{g.Count}:{g.Fixtures}:{g.IsSelected}"))
            + "#" + snapshot.Selection.Count;
        if (signature == _signature)
            return;

        _signature = signature;
        _tiles.Children.Clear();
        foreach (var group in snapshot.Groups)
            _tiles.Children.Add(Tile(group.Number.ToString(), group.Label, $"{group.Count} fixtures", group.Fixtures, group.IsSelected, $"Group {group.Number}"));

        var next = Enumerable.Range(1, 9999).First(n => snapshot.Groups.All(g => g.Number != n));
        var add = Tile("+", "New group", snapshot.Selection.Count == 0 ? "select fixtures first" : $"from {snapshot.SelectionText}", "", false, $"Record Group {next}", () => NewGroup(next));
        add.Opacity = snapshot.Selection.Count == 0 ? 0.45 : 1;
        _tiles.Children.Add(add);
    }

    /// <summary>
    /// Stores the selection as a new group. A selection that is already a group is refused, so that clicking the tile
    /// twice does not pile up identical groups. After Record or Delete the tile behaves like any other click.
    /// </summary>
    private void NewGroup(int number)
    {
        if (Host.IsVerbPending)
        {
            Host.Click($"Record Group {number}");
            return;
        }

        var snapshot = Host.Snapshot;
        var same = snapshot.Selection.Count > 0 ? snapshot.Groups.FirstOrDefault(g => g.IsSelected) : null;
        if (same is not null)
        {
            Host.Notify($"Group {same.Number} \"{same.Label}\" already holds exactly these fixtures. Select others first.", isError: true);
            return;
        }

        Host.Click($"Record Group {number}");
    }

    private Button Tile(string number, string label, string count, string fixtures, bool selected, string command, Action? click = null)
    {
        var grid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock { Text = number, FontFamily = new FontFamily("Consolas"), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        var name = Ui.Text(label, "TextBrush", wrap: false);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(name, 1);
        grid.Children.Add(name);
        var counter = Ui.Text(count, "DimBrush", wrap: false);
        Grid.SetRow(counter, 2);
        grid.Children.Add(counter);
        var list = Ui.Text(fixtures.Length == 0 ? "" : "Fixture " + fixtures, "MutedBrush", wrap: false);
        list.FontFamily = new FontFamily("Consolas");
        list.FontSize = 11;
        list.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(list, 3);
        grid.Children.Add(list);

        var tile = new Button
        {
            Width = 176,
            Height = 112,
            Margin = new Thickness(4),
            Style = Ui.Style("TileButton"),
            Background = Ui.Brush(selected ? "AmberDimBrush" : "Surface2Brush"),
            BorderBrush = Ui.Brush(selected ? "AmberBrush" : "LineBrush"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Content = grid,
            ToolTip = command,
        };
        tile.MouseEnter += (_, _) => { if (!selected) tile.Background = Ui.Brush("Surface3Brush"); };
        tile.MouseLeave += (_, _) => { if (!selected) tile.Background = Ui.Brush("Surface2Brush"); };
        tile.Click += (_, _) => (click ?? (() => Host.Click(command)))();
        System.Windows.Automation.AutomationProperties.SetAutomationId(tile, "Group" + number);
        return tile;
    }
}
