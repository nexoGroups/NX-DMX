using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>Cuelists as playback cards with Go / Back / Off, and the cues of the selected cuelist as a table.</summary>
public sealed class CuelistsView : ConsoleView
{
    private readonly WrapPanel _cards = new() { Margin = new Thickness(8, 8, 8, 4) };
    private readonly DataGrid _cues = new();
    private readonly TextBlock _title = new() { Style = Ui.Style("Heading"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _empty = Ui.Text("This cuelist has no cues yet. Set values on fixtures, then press Record Cue.", "MutedBrush");
    private int _selected = 1;
    private bool _pendingNew;
    private string _signature = "";

    public CuelistsView(ConsoleHost host) : base(host)
    {
        var root = new DockPanel();

        var top = new Border { Style = Ui.Style("PanelBorder"), BorderThickness = new Thickness(0, 0, 0, 1), Child = _cards };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bar = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        buttons.Children.Add(Ui.Button("Record Cue", RecordCue, "KeyRecord", "Store the programmer as the next cue of this cuelist"));
        buttons.Children.Add(Ui.Button("Update", () => Host.Insert("Update "), null, "Press Update, then click a cue to merge the programmer into it"));
        buttons.Children.Add(Ui.Button("Label", () => Host.Insert("Label "), null, "Press Label, click a cue, then type the name in quotes"));
        buttons.Children.Add(Ui.Button("Delete", () => Host.Insert("Delete "), null, "Press Delete, then click a cue"));
        buttons.Children.Add(Ui.Button("New cuelist", NewCuelist, null, "Create an empty cuelist with the next free number"));
        foreach (var button in buttons.Children.OfType<Button>())
            button.Margin = new Thickness(4, 0, 0, 0);

        bar.Children.Add(buttons);
        bar.Children.Add(_title);
        var barBorder = new Border { Style = Ui.Style("PanelBorder"), BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
        DockPanel.SetDock(barBorder, Dock.Top);
        root.Children.Add(barBorder);

        _cues.Columns.Add(new DataGridTextColumn { Header = "", Binding = new System.Windows.Data.Binding("Marker"), Width = 34 });
        _cues.Columns.Add(new DataGridTextColumn { Header = "CUE", Binding = new System.Windows.Data.Binding("NumberText"), Width = 80 });
        _cues.Columns.Add(new DataGridTextColumn { Header = "LABEL", Binding = new System.Windows.Data.Binding("Label"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _cues.Columns.Add(new DataGridTextColumn
        {
            Header = "VALUES",
            Binding = new System.Windows.Data.Binding("ValueCount"),
            Width = 90,
            ElementStyle = Ui.Style("RightCell"),
        });
        _cues.RowStyle = RowStyle();
        _cues.RowHeight = 30;
        _cues.SelectionMode = DataGridSelectionMode.Single;
        _cues.Focusable = false;
        _cues.MouseLeftButtonUp += Cues_MouseLeftButtonUp;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_cues, "CueTable");

        var body = new Grid();
        body.Children.Add(_cues);
        _empty.Margin = new Thickness(16, 14, 16, 0);
        _empty.VerticalAlignment = VerticalAlignment.Top;
        body.Children.Add(_empty);
        root.Children.Add(body);
        Content = root;
    }

    private static Style RowStyle()
    {
        var style = new Style(typeof(DataGridRow), (Style)Application.Current.FindResource(typeof(DataGridRow)));
        var current = new DataTrigger { Binding = new System.Windows.Data.Binding("Cue.IsCurrent"), Value = true };
        current.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("GreenDimBrush")));
        current.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("GreenBrush")));
        style.Triggers.Add(current);
        return style;
    }

    public override void Refresh()
    {
        var snapshot = Host.Snapshot;
        var list = snapshot.Cuelists.FirstOrDefault(l => l.Number == _selected);
        if (list is null && !_pendingNew && snapshot.Cuelists.Count > 0)
        {
            _selected = snapshot.Cuelists[0].Number;
            list = snapshot.Cuelists[0];
        }

        if (list is not null)
            _pendingNew = false;

        var signature = string.Join('|', snapshot.Cuelists.Select(l => $"{l.Number}:{l.Label}:{l.Active}:{l.CurrentText}:{l.Cues.Count}")) + "#" + _selected;
        if (signature != _signature)
        {
            _signature = signature;
            BuildCards(snapshot);
        }

        _title.Text = list is null ? (_pendingNew ? $"Cuelist {_selected} · new" : "No cuelist yet") : list.Label == $"Cuelist {list.Number}" ? list.Label : $"Cuelist {list.Number} · {list.Label}";
        _cues.ItemsSource = list?.Cues.Select(c => new CueRowVm(c, list.Number)).ToList();
        _cues.Visibility = list is null || list.Cues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = list is null || list.Cues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (list is null)
            _empty.Text = "No cuelist yet. Set values on fixtures, then press Record Cue: Cuelist 1 is created for you.";
    }

    private void BuildCards(ConsoleSnapshot snapshot)
    {
        _cards.Children.Clear();
        foreach (var list in snapshot.Cuelists)
            _cards.Children.Add(Card(list));
    }

    private Border Card(CuelistInfo list)
    {
        var selected = list.Number == _selected;
        var grid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new DockPanel();
        head.Children.Add(new TextBlock
        {
            Text = list.Number.ToString(), FontFamily = new FontFamily("Consolas"), FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 10, 0),
        });
        head.Children.Add(new TextBlock { Text = list.Label, Foreground = Ui.Brush("DimBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(head);

        var state = new TextBlock
        {
            Text = list.CurrentText,
            FontFamily = new FontFamily("Consolas"),
            Foreground = Ui.Brush(list.Active ? "GreenBrush" : "MutedBrush"),
            Margin = new Thickness(0, 4, 0, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(state, 1);
        grid.Children.Add(state);

        var row = new UniformGrid { Columns = 4 };
        var number = list.Number;
        row.Children.Add(Ui.Button("GO", () => Host.Run($"Go Cuelist {number}"), "GreenButton"));
        row.Children.Add(Ui.Button("BACK", () => Host.Run($"Back Cuelist {number}")));
        row.Children.Add(Ui.Button("OFF", () => Host.Run($"Off Cuelist {number}")));
        row.Children.Add(Ui.Button("TOGGLE", () => Host.Run($"Toggle Cuelist {number}"), tooltip: "Start the cuelist, or release it when it runs"));
        foreach (var button in row.Children.OfType<Button>())
        {
            button.Margin = new Thickness(0, 0, 3, 0);
            button.Padding = new Thickness(0, 4, 0, 4);
            button.FontSize = 10.5;
            button.Focusable = false;
        }

        Grid.SetRow(row, 2);
        grid.Children.Add(row);

        var card = new Border
        {
            Width = 260,
            Margin = new Thickness(3),
            Child = grid,
            Cursor = Cursors.Hand,
            Background = Ui.Brush(selected ? "Surface3Brush" : "Surface2Brush"),
            BorderBrush = Ui.Brush(list.Active ? "GreenBrush" : selected ? "AmberBrush" : "LineBrush"),
            BorderThickness = new Thickness(selected || list.Active ? 2 : 1),
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) is not null)
                return;

            if (Host.IsVerbPending)
            {
                Host.Click($"Cuelist {number}");
            }
            else
            {
                _selected = number;
                _pendingNew = false;
                _signature = "";
                Refresh();
            }

            e.Handled = true;
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(card, "Cuelist" + number);
        return card;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current is not null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private void Cues_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindParent<DataGridRow>(source) is not { Item: CueRowVm cue })
            return;

        var target = $"Cue {cue.Cue.NumberText} Cuelist {cue.Cuelist}";
        if (Host.IsVerbPending)
            Host.Click(target);
        else
            Host.Run("Goto " + target);
    }

    private void RecordCue()
    {
        var list = Host.Snapshot.Cuelists.FirstOrDefault(l => l.Number == _selected);
        var next = list is null || list.Cues.Count == 0 ? 1 : Math.Floor(list.Cues.Max(c => c.Number)) + 1;
        Host.Click($"Record Cue {next}{(_selected != 1 ? $" Cuelist {_selected}" : "")}");
    }

    private void NewCuelist()
    {
        var snapshot = Host.Snapshot;
        var next = Enumerable.Range(1, 9999).First(n => snapshot.Cuelists.All(l => l.Number != n));
        _selected = next;
        _pendingNew = true;
        _signature = "";
        Host.AddLog($"Cuelist {next} is created with its first Record Cue. Set values, then press Record Cue.");
        Refresh();
    }

    private sealed class CueRowVm(CueInfo cue, int cuelist)
    {
        public CueInfo Cue { get; } = cue;

        public int Cuelist { get; } = cuelist;

        public string Marker => Cue.IsCurrent ? "▶" : "";

        public string NumberText => Cue.NumberText;

        public string Label => Cue.Label;

        public int ValueCount => Cue.ValueCount;
    }
}
