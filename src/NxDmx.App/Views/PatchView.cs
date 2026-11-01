using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NxDmx.App.Controls;
using NxDmx.App.Services;
using NxDmx.Core.Commands;

namespace NxDmx.App.Views;

/// <summary>
/// Patching: add fixtures from a profile to a universe and start address, and move, re-profile, rename or delete the
/// fixtures that are patched. Every action is a Patch/Label/Delete command and shows up in the command log.
/// </summary>
public sealed class PatchView : ConsoleView
{
    private sealed record TypeChoice(string Name, int Channels, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record PatchRow(int Id, string Label, string Type, int Universe, int Address, int End, int Channels, string Status, bool Conflict);

    private readonly DataGrid _grid = new() { IsReadOnly = true };
    private readonly AddressMap _map = new() { Margin = new Thickness(8, 4, 8, 8) };

    // add
    private readonly ComboBox _profile = new() { MinWidth = 200 };
    private readonly TextBox _count = Ui.Input("1");
    private readonly TextBox _first = Ui.Input();
    private readonly TextBox _universe = Ui.Input("1");
    private readonly TextBox _address = Ui.Input();
    private readonly CheckBox _auto = new() { Content = "Next free address", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _label = Ui.Input();
    private readonly TextBlock _preview = Ui.Text("", "DimBrush");
    private readonly Button _patchButton;

    // edit
    private readonly TextBlock _selection = Ui.Text("Select fixtures in the list to move, re-profile, rename or delete them.", "MutedBrush");
    private readonly TextBox _moveUniverse = Ui.Input();
    private readonly TextBox _moveAddress = Ui.Input();
    private readonly ComboBox _retype = new();
    private readonly TextBox _relabel = Ui.Input();
    private readonly StackPanel _editBody = new();

    private string _signature = "";
    private string _profileSignature = "";
    private bool _loading;

    public PatchView(ConsoleHost host) : base(host)
    {
        _patchButton = Ui.Button("Patch fixtures", AddFixtures, "GreenButton");
        _patchButton.Padding = new Thickness(0, 8, 0, 8);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_patchButton, "PatchButton");

        BuildGrid();

        var left = new DockPanel();
        var mapBox = new DockPanel { MaxHeight = 190 };
        mapBox.Children.Add(Ui.Caption("Address map – click to choose a start address", new Thickness(10, 6, 0, 0)));
        DockPanel.SetDock(mapBox.Children[0], Dock.Top);
        mapBox.Children.Add(new ScrollViewer { Content = _map, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        var mapBorder = new Border { Style = Ui.Style("PanelBorder"), BorderThickness = new Thickness(0, 1, 0, 0), Child = mapBox };
        DockPanel.SetDock(mapBorder, Dock.Bottom);
        left.Children.Add(mapBorder);
        left.Children.Add(_grid);

        var right = new StackPanel { Margin = new Thickness(14) };
        right.Children.Add(Ui.Caption("Add fixtures"));
        right.Children.Add(BuildAddForm());
        right.Children.Add(new Border { Height = 1, Background = Ui.Brush("LineBrush"), Margin = new Thickness(0, 12, 0, 12) });
        right.Children.Add(Ui.Caption("Selected fixtures"));
        right.Children.Add(_selection);
        BuildEditPanel();
        right.Children.Add(_editBody);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380), MinWidth = 320 });
        grid.Children.Add(left);
        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Focusable = false };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        var panel = new Border
        {
            Style = Ui.Style("PanelBorder"),
            BorderThickness = new Thickness(0),
            Child = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
        };
        Grid.SetColumn(panel, 2);
        grid.Children.Add(panel);
        Content = grid;

        _map.AddressPicked += (universe, channel) =>
        {
            _universe.Text = universe.ToString();
            _address.Text = channel.ToString();
            _auto.IsChecked = false;
            UpdatePreview();
        };

        foreach (var box in new[] { _count, _first, _universe, _address, _label })
            box.TextChanged += (_, _) => UpdatePreview();
        _profile.SelectionChanged += (_, _) => UpdatePreview();
        _auto.Click += (_, _) => UpdatePreview();
    }

    // ---- building --------------------------------------------------------------------------------------------

    private void BuildGrid()
    {
        _grid.Columns.Add(new DataGridTextColumn { Header = "NO", Binding = new Binding("Id"), Width = 56 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "LABEL", Binding = new Binding("Label"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 120 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "PROFILE", Binding = new Binding("Type"), Width = 140 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "UNIVERSE", Binding = new Binding("Universe"), Width = 86, ElementStyle = Ui.Style("RightCell") });
        _grid.Columns.Add(new DataGridTextColumn { Header = "ADDRESS", Binding = new Binding("Address"), Width = 86, ElementStyle = Ui.Style("RightCell") });
        _grid.Columns.Add(new DataGridTextColumn { Header = "TO", Binding = new Binding("End"), Width = 64, ElementStyle = Ui.Style("RightCell") });
        _grid.Columns.Add(new DataGridTextColumn { Header = "CH", Binding = new Binding("Channels"), Width = 54, ElementStyle = Ui.Style("RightCell") });
        _grid.Columns.Add(new DataGridTextColumn { Header = "", Binding = new Binding("Status"), Width = 90 });

        var style = new Style(typeof(DataGridRow), (Style)Application.Current.FindResource(typeof(DataGridRow)));
        var conflict = new DataTrigger { Binding = new Binding("Conflict"), Value = true };
        conflict.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("RedBrush")));
        style.Triggers.Add(conflict);
        _grid.RowStyle = style;
        _grid.SelectionChanged += (_, _) => OnSelectionChanged();
        System.Windows.Automation.AutomationProperties.SetAutomationId(_grid, "PatchTable");
    }

    private UIElement BuildAddForm()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Field("Profile", _profile));

        var row1 = new Grid();
        row1.ColumnDefinitions.Add(new ColumnDefinition());
        row1.ColumnDefinitions.Add(new ColumnDefinition());
        var quantity = Ui.Field("Quantity", _count);
        var first = Ui.Field("First fixture number", _first, margin: new Thickness(0, 0, 0, 10));
        Grid.SetColumn(first, 1);
        row1.Children.Add(quantity);
        row1.Children.Add(first);
        panel.Children.Add(row1);

        var row2 = new Grid();
        row2.ColumnDefinitions.Add(new ColumnDefinition());
        row2.ColumnDefinitions.Add(new ColumnDefinition());
        var universe = Ui.Field("Universe", _universe);
        var address = Ui.Field("Start address (1–512)", _address, margin: new Thickness(0, 0, 0, 10));
        Grid.SetColumn(address, 1);
        row2.Children.Add(universe);
        row2.Children.Add(address);
        panel.Children.Add(row2);

        panel.Children.Add(_auto);
        panel.Children.Add(Ui.Field("Label (optional)", _label));
        _preview.Margin = new Thickness(0, 0, 0, 10);
        _preview.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        panel.Children.Add(_preview);
        panel.Children.Add(_patchButton);
        return panel;
    }

    private void BuildEditPanel()
    {
        var move = new Grid();
        move.ColumnDefinitions.Add(new ColumnDefinition());
        move.ColumnDefinitions.Add(new ColumnDefinition());
        var universe = Ui.Field("Universe", _moveUniverse);
        var address = Ui.Field("Address", _moveAddress, margin: new Thickness(0, 0, 0, 10));
        Grid.SetColumn(address, 1);
        move.Children.Add(universe);
        move.Children.Add(address);
        _editBody.Children.Add(move);
        _editBody.Children.Add(Ui.Button("Move (patch one after the other)", MoveSelected, null, "Patch Fixture … At universe.address"));
        _editBody.Children.Add(new Border { Height = 12 });
        _editBody.Children.Add(Ui.Field("Change profile", _retype));
        _editBody.Children.Add(Ui.Button("Apply profile", RetypeSelected, null, "Patch Fixture … Type …"));
        _editBody.Children.Add(new Border { Height = 12 });
        _editBody.Children.Add(Ui.Field("Label (one fixture)", _relabel));
        _editBody.Children.Add(Ui.Button("Set label", RelabelSelected, null, "Label Fixture … \"…\""));
        _editBody.Children.Add(new Border { Height = 12 });
        _editBody.Children.Add(Ui.Button("Select in programmer", SelectInProgrammer, null, "Fixture … in the command line"));
        var delete = Ui.Button("Delete fixtures", DeleteSelected, "RedButton", "Delete Fixture …");
        delete.Margin = new Thickness(0, 6, 0, 0);
        _editBody.Children.Add(delete);
        foreach (var button in _editBody.Children.OfType<Button>())
            button.Padding = new Thickness(0, 6, 0, 6);
        _editBody.Visibility = Visibility.Collapsed;
    }

    // ---- refreshing ------------------------------------------------------------------------------------------

    public override void Refresh()
    {
        var snapshot = Host.Snapshot;

        var profileSignature = string.Join('|', snapshot.Profiles.Select(p => $"{p.Type.Name}:{p.Type.Footprint}"));
        if (profileSignature != _profileSignature)
        {
            _profileSignature = profileSignature;
            var selectedName = (_profile.SelectedItem as TypeChoice)?.Name;
            var retypeName = (_retype.SelectedItem as TypeChoice)?.Name;
            var choices = snapshot.Profiles.Select(p => new TypeChoice(p.Type.Name, p.Type.Footprint, $"{p.Type.Name} · {p.Type.Footprint} ch")).ToList();
            _loading = true;
            _profile.ItemsSource = choices;
            _retype.ItemsSource = choices.ToList();
            _profile.SelectedItem = choices.FirstOrDefault(c => c.Name == selectedName) ?? choices.FirstOrDefault(c => c.Name == "RGBDim") ?? choices.FirstOrDefault();
            _retype.SelectedItem = choices.FirstOrDefault(c => c.Name == retypeName) ?? choices.FirstOrDefault();
            _loading = false;
        }

        var rows = snapshot.Fixtures
            .Select(f => new PatchRow(f.Id, f.Label, f.TypeName, f.Universe, f.Address, f.EndAddress, f.Footprint, f.Conflict ? "CONFLICT" : "", f.Conflict))
            .ToList();
        var signature = string.Join('|', rows);
        if (signature != _signature)
        {
            _signature = signature;
            var selectedIds = _grid.SelectedItems.OfType<PatchRow>().Select(r => r.Id).ToHashSet();
            _loading = true;
            _grid.ItemsSource = rows;
            foreach (var row in rows.Where(r => selectedIds.Contains(r.Id)))
                _grid.SelectedItems.Add(row);
            _loading = false;
            OnSelectionChanged();
        }

        _map.Show(snapshot.Universes, int.TryParse(_universe.Text, out var u) ? u : 0);
        if (_first.Text.Length == 0 || !_first.IsKeyboardFocused && IsAutoFirst)
            _first.Text = NextFreeId().ToString();

        UpdatePreview();
    }

    private bool IsAutoFirst => Host.Snapshot.Fixtures.Any(f => f.Id.ToString() == _first.Text);

    private int NextFreeId()
    {
        var used = Host.Snapshot.Fixtures.Select(f => f.Id).ToHashSet();
        var id = 1;
        while (used.Contains(id))
            id++;

        return id;
    }

    /// <summary>First address in the universe where <paramref name="length"/> channels are free, or null.</summary>
    private int? FindFree(int universe, int length)
    {
        var blocks = Host.Snapshot.Universes.FirstOrDefault(u => u.Number == universe)?.Blocks ?? [];
        var address = 1;
        foreach (var block in blocks.OrderBy(b => b.Start))
        {
            if (block.End < address)
                continue;
            if (block.Start >= address + length)
                break;

            address = block.End + 1;
        }

        return address + length - 1 <= 512 ? address : null;
    }

    // ---- add -------------------------------------------------------------------------------------------------

    private sealed record AddPlan(string Command, string Preview, string? Error);

    private AddPlan Plan()
    {
        if (_profile.SelectedItem is not TypeChoice type)
            return new AddPlan("", "", "Choose a profile.");

        var count = Ui.ToInt(_count.Text);
        if (count is null or < 1 or > 4096)
            return new AddPlan("", "", "Quantity must be 1 to 4096.");

        var first = Ui.ToInt(_first.Text);
        if (first is null or < 1)
            return new AddPlan("", "", "The first fixture number must be 1 or higher.");

        var taken = Enumerable.Range(first.Value, count.Value).FirstOrDefault(id => Host.Snapshot.Fixtures.Any(f => f.Id == id));
        if (taken != 0)
            return new AddPlan("", "", $"Fixture {taken} exists already. Pick another first number.");

        var universe = Ui.ToInt(_universe.Text);
        if (universe is null or < 1 or > 32768)
            return new AddPlan("", "", "Universe must be 1 to 32768.");

        int address;
        if (_auto.IsChecked == true)
        {
            var found = FindFree(universe.Value, type.Channels * count.Value) ?? FindFree(universe.Value, type.Channels);
            if (found is null)
                return new AddPlan("", "", $"Universe {universe} has no free space for {type.Channels} channels.");

            address = found.Value;
        }
        else
        {
            if (Ui.ToInt(_address.Text) is not { } typed || typed is < 1 or > 512)
                return new AddPlan("", "", "The start address must be 1 to 512.");

            address = typed;
        }

        var last = first.Value + count.Value - 1;
        var total = type.Channels * count.Value;
        var command = $"Patch Fixture {first}{(count > 1 ? $" Thru {last}" : "")} Type {CommandText.Quote(type.Name)} At {universe}.{address}";
        if (_label.Text.Trim().Length > 0)
            command += $" Label {CommandText.Quote(_label.Text.Trim())}";

        var spill = address + total - 1 > 512 ? "  (continues in the next universe)" : "";
        var preview = $"Fixture {first}{(count > 1 ? $" – {last}" : "")}  {universe}.{address} + {total} ch{spill}";
        return new AddPlan(command, preview, null);
    }

    private void UpdatePreview()
    {
        if (_loading)
            return;

        _address.IsEnabled = _auto.IsChecked != true;
        var plan = Plan();
        if (_auto.IsChecked == true && plan.Error is null && Ui.ToInt(_universe.Text) is { } u && _profile.SelectedItem is TypeChoice t)
        {
            var found = FindFree(u, t.Channels * (Ui.ToInt(_count.Text) ?? 1)) ?? FindFree(u, t.Channels);
            if (found is not null && _address.Text != found.ToString())
                _address.Text = found.ToString();
        }

        _preview.Text = plan.Error ?? plan.Preview;
        _preview.Foreground = Ui.Brush(plan.Error is null ? "DimBrush" : "RedBrush");
        _patchButton.IsEnabled = plan.Error is null;
    }

    private void AddFixtures()
    {
        var plan = Plan();
        if (plan.Error is not null)
            return;

        var result = Host.Run(plan.Command);
        if (result.Success)
        {
            _first.Text = "";
            _label.Text = "";
        }
    }

    // ---- selected fixtures -----------------------------------------------------------------------------------

    private List<int> SelectedIds => _grid.SelectedItems.OfType<PatchRow>().Select(r => r.Id).Order().ToList();

    private void OnSelectionChanged()
    {
        if (_loading)
            return;

        var ids = SelectedIds;
        _editBody.Visibility = ids.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _selection.Text = ids.Count == 0
            ? "Select fixtures in the list to move, re-profile, rename or delete them."
            : $"{ids.Count} selected: Fixture {SelectionFormatter.Format(ids)}";
        _selection.Foreground = Ui.Brush(ids.Count == 0 ? "MutedBrush" : "AmberBrush");

        if (ids.Count > 0 && Host.Snapshot.Fixtures.FirstOrDefault(f => f.Id == ids[0]) is { } first)
        {
            _moveUniverse.Text = first.Universe.ToString();
            _moveAddress.Text = first.Address.ToString();
            _relabel.Text = ids.Count == 1 ? first.Label : "";
            _relabel.IsEnabled = ids.Count == 1;
            var choices = _retype.ItemsSource as IEnumerable<TypeChoice>;
            _retype.SelectedItem = choices?.FirstOrDefault(c => c.Name == first.TypeName);
        }
    }

    /// <summary>The selected numbers as runs of consecutive numbers: 1,2,3,7 becomes (1,3) and (7,7).</summary>
    private static List<(int From, int To)> Runs(List<int> ids)
    {
        var runs = new List<(int, int)>();
        foreach (var id in ids)
        {
            if (runs.Count > 0 && runs[^1].Item2 + 1 == id)
                runs[^1] = (runs[^1].Item1, id);
            else
                runs.Add((id, id));
        }

        return runs;
    }

    private static string Range((int From, int To) run) => run.From == run.To ? $"{run.From}" : $"{run.From} Thru {run.To}";

    private void MoveSelected()
    {
        var ids = SelectedIds;
        if (ids.Count == 0 || Ui.ToInt(_moveUniverse.Text) is not { } universe || Ui.ToInt(_moveAddress.Text) is not { } address)
        {
            Host.AddLog("Move: enter a universe and an address.", error: true);
            return;
        }

        var runs = Runs(ids);
        if (runs.Count == 1)
        {
            Host.Run($"Patch Fixture {Range(runs[0])} At {universe}.{address}");
            return;
        }

        // Not one block: patch the runs one after the other, each starting where the previous one ended.
        var lines = new List<string>();
        var next = address;
        var currentUniverse = universe;
        foreach (var id in ids)
        {
            var footprint = Host.Snapshot.Fixtures.First(f => f.Id == id).Footprint;
            if (next + footprint - 1 > 512)
            {
                currentUniverse++;
                next = 1;
            }

            lines.Add($"Patch Fixture {id} At {currentUniverse}.{next}");
            next += footprint;
        }

        Host.Run(string.Join("; ", lines));
    }

    private void RetypeSelected()
    {
        if (_retype.SelectedItem is not TypeChoice type || SelectedIds.Count == 0)
            return;

        Host.Run(string.Join("; ", Runs(SelectedIds).Select(run => $"Patch Fixture {Range(run)} Type {CommandText.Quote(type.Name)}")));
    }

    private void RelabelSelected()
    {
        var ids = SelectedIds;
        if (ids.Count != 1 || _relabel.Text.Trim().Length == 0)
            return;

        Host.Run($"Label Fixture {ids[0]} {CommandText.Quote(_relabel.Text.Trim())}");
    }

    private void SelectInProgrammer() => Host.Run("Fixture " + SelectionFormatter.Format(SelectedIds));

    private void DeleteSelected()
    {
        var ids = SelectedIds;
        if (ids.Count == 0)
            return;

        var answer = MessageBox.Show(Window.GetWindow(this),
            $"Delete {ids.Count} fixture{(ids.Count == 1 ? "" : "s")} from the patch?\n\nFixture {SelectionFormatter.Format(ids)}\n\nOops can take this back.",
            "Delete fixtures", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
            Host.Run(string.Join("; ", Runs(ids).Select(run => $"Delete Fixture {Range(run)}")));
    }
}
