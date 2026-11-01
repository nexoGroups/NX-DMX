using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NxDmx.App.Controls;
using NxDmx.App.Services;
using NxDmx.Core.Commands;
using NxDmx.Core.Dmx;

namespace NxDmx.App.Views;

/// <summary>
/// DMX universes: create, name, switch on and off, and choose where each one is sent (broadcast or one node).
/// The settings are per universe, so every universe can go to a different place. Edits run as Universe commands.
/// </summary>
public sealed class UniversesView : ConsoleView
{
    private sealed record UniverseRow(int Number, string Label, string Output, string Destination, int Fixtures, string Channels);

    private readonly DataGrid _grid = new() { SelectionMode = DataGridSelectionMode.Single };
    private readonly DmxMonitor _monitor = new();
    private readonly TextBlock _monitorTitle = Ui.Caption("DMX output", new Thickness(14, 8, 0, 4));
    private readonly TextBox _newNumber = Ui.Input(width: 70);
    private readonly TextBox _label = Ui.Input();
    private readonly CheckBox _enabled = new() { Content = "Output on", Margin = new Thickness(0, 0, 0, 10) };
    private readonly RadioButton _broadcast = new() { Content = "Broadcast (default destination of the Output view)", GroupName = "Dest", Margin = new Thickness(0, 0, 0, 6) };
    private readonly RadioButton _unicast = new() { Content = "Unicast to one node", GroupName = "Dest", Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBox _node = Ui.Input();
    private readonly TextBlock _title = Ui.Text("Select a universe", "AmberBrush");
    private readonly StackPanel _editBody = new();
    private int _selected;
    private string _signature = "";
    private bool _loading;

    public UniversesView(ConsoleHost host) : base(host)
    {
        _grid.Columns.Add(new DataGridTextColumn { Header = "UNIVERSE", Binding = new Binding("Number"), Width = 90 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "NAME", Binding = new Binding("Label"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "OUTPUT", Binding = new Binding("Output"), Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "DESTINATION", Binding = new Binding("Destination"), Width = 170 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "FIXTURES", Binding = new Binding("Fixtures"), Width = 80, ElementStyle = Ui.Style("RightCell") });
        _grid.Columns.Add(new DataGridTextColumn { Header = "CHANNELS", Binding = new Binding("Channels"), Width = 100, ElementStyle = Ui.Style("RightCell") });
        _grid.SelectionChanged += (_, _) => OnSelected();
        System.Windows.Automation.AutomationProperties.SetAutomationId(_newNumber, "NewUniverseNumber");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_grid, "UniverseTable");

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 8) };
        addRow.Children.Add(Ui.Text("New universe number:", "DimBrush", wrap: false, margin: new Thickness(0, 5, 8, 0)));
        addRow.Children.Add(_newNumber);
        var add = Ui.Button("Create", CreateUniverse, "GreenButton", "Universe n");
        add.Margin = new Thickness(6, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(add, "CreateUniverse");
        addRow.Children.Add(add);
        addRow.Children.Add(Ui.Text("  Universes are also created when you patch a fixture into them.", "MutedBrush", wrap: false, margin: new Thickness(10, 5, 0, 0)));

        var top = new DockPanel();
        var addBorder = new Border { Style = Ui.Style("PanelBorder"), BorderThickness = new Thickness(0, 0, 0, 1), Child = addRow };
        DockPanel.SetDock(addBorder, Dock.Top);
        top.Children.Add(addBorder);
        top.Children.Add(_grid);

        BuildEditor();

        var lower = new DockPanel();
        DockPanel.SetDock(_monitorTitle, Dock.Top);
        lower.Children.Add(_monitorTitle);
        lower.Children.Add(new ScrollViewer { Content = _monitor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Margin = new Thickness(8, 0, 8, 8) });
        var lowerBorder = new Border { Style = Ui.Style("PanelBorder"), BorderThickness = new Thickness(0, 1, 0, 0), Child = lower };

        // Left: the universes on top, the live DMX values below. Right: the editor for the selected universe, full height.
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(240), MinHeight = 140 });
        left.Children.Add(top);
        var rowSplitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Focusable = false, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(rowSplitter, 1);
        left.Children.Add(rowSplitter);
        Grid.SetRow(lowerBorder, 2);
        left.Children.Add(lowerBorder);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380), MinWidth = 320 });
        root.Children.Add(left);
        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Focusable = false };
        Grid.SetColumn(splitter, 1);
        root.Children.Add(splitter);
        var editor = new Border
        {
            Style = Ui.Style("PanelBorder"),
            BorderThickness = new Thickness(0),
            Child = new ScrollViewer { Content = _editBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
        };
        Grid.SetColumn(editor, 2);
        root.Children.Add(editor);
        Content = root;
    }

    private void BuildEditor()
    {
        _editBody.Margin = new Thickness(14);
        _editBody.Children.Add(Ui.Caption("Universe"));
        _title.FontSize = 15;
        _title.Margin = new Thickness(0, 0, 0, 12);
        _editBody.Children.Add(_title);

        var body = new StackPanel { Visibility = Visibility.Collapsed };
        body.Children.Add(Ui.Field("Name", _label));
        body.Children.Add(_enabled);
        body.Children.Add(Ui.Caption("Where this universe is sent"));
        body.Children.Add(_broadcast);
        body.Children.Add(_unicast);
        body.Children.Add(Ui.Field("Node IP address (unicast)", _node, margin: new Thickness(0, 4, 0, 14)));
        body.Children.Add(Ui.Text("Art-Net Port-Address on the wire = universe number − 1. Set the same on your node.", "MutedBrush", margin: new Thickness(0, 0, 0, 14)));
        var apply = Ui.Button("Apply", Apply, "GreenButton", "Universe n Label … Unicast … On/Off");
        apply.Padding = new Thickness(0, 7, 0, 7);
        System.Windows.Automation.AutomationProperties.SetAutomationId(apply, "ApplyUniverse");
        body.Children.Add(apply);
        var delete = Ui.Button("Delete universe", Delete, "RedButton", "Delete Universe n (only when no fixture is patched in it)");
        delete.Margin = new Thickness(0, 6, 0, 0);
        delete.Padding = new Thickness(0, 7, 0, 7);
        body.Children.Add(delete);
        _editBody.Children.Add(body);
        _editBody.Tag = body;

        _unicast.Checked += (_, _) => _node.IsEnabled = true;
        _broadcast.Checked += (_, _) => _node.IsEnabled = false;
    }

    public override void Refresh()
    {
        var snapshot = Host.Snapshot;
        var rows = snapshot.Universes.Select(u => new UniverseRow(
            u.Number, u.Label, u.Enabled ? "ON" : "OFF",
            u.Destination == UniverseDestination.Unicast ? $"Unicast {u.UnicastAddress}" : "Broadcast",
            u.FixtureCount, $"{u.ChannelsUsed} / 512")).ToList();

        var signature = string.Join('|', rows);
        if (signature != _signature)
        {
            _signature = signature;
            _loading = true;
            _grid.ItemsSource = rows;
            if (rows.All(r => r.Number != _selected))
                _selected = rows.FirstOrDefault()?.Number ?? 0;

            _grid.SelectedItem = rows.FirstOrDefault(r => r.Number == _selected);
            _loading = false;
            LoadEditor();
        }
    }

    public override void Tick()
    {
        var info = Host.Snapshot.Universes.FirstOrDefault(u => u.Number == _selected);
        if (info is null)
        {
            _monitorTitle.Text = "DMX OUTPUT";
            _monitor.Show(null, []);
            return;
        }

        byte[]? data = null;
        Host.Engine.LastFrame?.Universes.TryGetValue(_selected, out data);
        _monitorTitle.Text = $"DMX OUTPUT · UNIVERSE {_selected} · {info.Label.ToUpperInvariant()}{(info.Enabled ? "" : "  (OUTPUT OFF – not sent)")}";
        _monitor.Show(data, info.Blocks);
    }

    private void OnSelected()
    {
        if (_loading || _grid.SelectedItem is not UniverseRow row)
            return;

        _selected = row.Number;
        LoadEditor();
    }

    private void LoadEditor()
    {
        var body = (StackPanel)_editBody.Tag;
        var info = Host.Snapshot.Universes.FirstOrDefault(u => u.Number == _selected);
        body.Visibility = info is null ? Visibility.Collapsed : Visibility.Visible;
        if (info is null)
        {
            _title.Text = "Create a universe on the left";
            return;
        }

        _title.Text = $"Universe {info.Number}";
        _label.Text = info.Label;
        _enabled.IsChecked = info.Enabled;
        _node.Text = info.UnicastAddress;
        if (info.Destination == UniverseDestination.Unicast)
            _unicast.IsChecked = true;
        else
            _broadcast.IsChecked = true;

        _node.IsEnabled = info.Destination == UniverseDestination.Unicast;
        Tick();
    }

    private void CreateUniverse()
    {
        if (Ui.ToInt(_newNumber.Text) is not { } number)
        {
            Host.AddLog("Create universe: enter a universe number (1 to 32768).", error: true);
            return;
        }

        if (Host.Run($"Universe {number}").Success)
        {
            _selected = number;
            _signature = "";
            _newNumber.Text = "";
            Refresh();
        }
    }

    private void Apply()
    {
        if (_selected == 0)
            return;

        var line = $"Universe {_selected} Label {CommandText.Quote(_label.Text.Trim().Length == 0 ? $"Universe {_selected}" : _label.Text.Trim())}";
        if (_unicast.IsChecked == true)
        {
            if (_node.Text.Trim().Length == 0)
            {
                Host.AddLog("Unicast needs the IP address of the node, e.g. 192.168.1.50.", error: true);
                return;
            }

            line += $" Unicast {_node.Text.Trim()}";
        }
        else
        {
            line += " Broadcast";
        }

        line += _enabled.IsChecked == true ? " On" : " Off";
        Host.Run(line);
    }

    private void Delete()
    {
        if (_selected == 0)
            return;

        var answer = MessageBox.Show(Window.GetWindow(this), $"Delete universe {_selected}?\n\nOops can take this back.",
            "Delete universe", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
            Host.Run($"Delete Universe {_selected}");
    }
}
