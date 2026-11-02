using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NxDmx.App.Controls;
using NxDmx.App.Services;
using NxDmx.Core.Commands;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Programming;

namespace NxDmx.App.Views;

/// <summary>One fixture tile. Updated in place so the tiles do not flicker while values change.</summary>
public sealed class TileVm : INotifyPropertyChanged
{
    private bool _selected;
    private bool _programmed;
    private bool _highlighted;
    private string _level = "";
    private Brush _brush = Brushes.Gray;
    private string _label = "";
    private string _toolTip = "";

    public TileVm(int id) => Id = id;

    public int Id { get; }

    public string AutomationId => $"Fixture{Id}";

    public string Label { get => _label; private set => Set(ref _label, value, nameof(Label)); }

    public string Patch { get; private set; } = "";

    public string Level { get => _level; private set => Set(ref _level, value, nameof(Level)); }

    public Brush Brush { get => _brush; private set => Set(ref _brush, value, nameof(Brush)); }

    public bool Selected { get => _selected; private set => Set(ref _selected, value, nameof(Selected)); }

    public bool Programmed
    {
        get => _programmed;
        private set
        {
            if (Set(ref _programmed, value, nameof(Programmed)))
                OnChanged(nameof(ProgrammedVisibility));
        }
    }

    /// <summary>The fader under the pointer controls this fixture.</summary>
    public bool Highlighted { get => _highlighted; set => Set(ref _highlighted, value, nameof(Highlighted)); }

    public Visibility ProgrammedVisibility => _programmed ? Visibility.Visible : Visibility.Collapsed;

    public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value, nameof(ToolTip)); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(FixtureInfo info)
    {
        Label = info.Label;
        Selected = info.Selected;
        Programmed = info.Programmed;
        Level = info.Detail.Length > 0 ? info.Detail : info.Intensity is { } i ? $"{Math.Round(i * 100):0} %" : info.TypeName;
        var color = Color.FromRgb(info.Red, info.Green, info.Blue);
        if (_brush is not SolidColorBrush current || current.Color != color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Brush = brush;
        }

        var patch = info.Patch;
        if (Patch != patch)
        {
            Patch = patch;
            OnChanged(nameof(Patch));
        }

        ToolTip = $"Fixture {info.Id} · {info.TypeName} · {info.Patch}";
    }

    private bool Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The programming view: every patched fixture as a tile (click to select) and, for the selection, the attribute
/// pages with directly clickable values. Every click is written into the command line and runs as a normal command.
/// </summary>
public partial class FixturesView : ConsoleView
{
    private readonly ObservableCollection<TileVm> _tiles = [];
    private readonly Dictionary<int, TileVm> _byId = new();
    private readonly Dictionary<AttributeGroup, RadioButton> _tabs = new();
    private AttributeGroup _group = AttributeGroup.Intensity;
    private string _signature = "";
    private int _lastClicked;

    private readonly Dictionary<FixtureAttribute, LevelBar> _bars = new();
    private readonly Dictionary<(FixtureAttribute, string), ToggleButton> _functionButtons = new();
    private XyPad? _pad;

    public FixturesView(ConsoleHost host) : base(host)
    {
        InitializeComponent();
        Tiles.ItemsSource = _tiles;
    }

    public override void Refresh()
    {
        var snapshot = Host.Snapshot;
        UpdateTiles(snapshot);
        UpdateGroups(snapshot);
        UpdateAttributes(snapshot);
    }

    // ---- tiles -----------------------------------------------------------------------------------------------

    private void UpdateTiles(ConsoleSnapshot snapshot)
    {
        var ids = snapshot.Fixtures.Select(f => f.Id).ToList();
        if (!ids.SequenceEqual(_tiles.Select(t => t.Id)))
        {
            _tiles.Clear();
            _byId.Clear();
            foreach (var info in snapshot.Fixtures)
            {
                var tile = new TileVm(info.Id);
                _byId[info.Id] = tile;
                _tiles.Add(tile);
            }
        }

        foreach (var info in snapshot.Fixtures)
            _byId[info.Id].Update(info);

        var marked = HighlightedFixtures(snapshot);
        foreach (var (id, tile) in _byId)
            tile.Highlighted = marked.Contains(id);

        EmptyHint.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectBar.IsEnabled = _tiles.Count > 0;
    }

    /// <summary>The fixtures of the group or cuelist on the fader under the pointer.</summary>
    private HashSet<int> HighlightedFixtures(ConsoleSnapshot snapshot)
    {
        var fader = Host.Highlight;
        if (fader is null)
            return [];

        return fader.Kind switch
        {
            Core.Playback.FaderKind.Group => [.. snapshot.Groups.FirstOrDefault(g => g.Number == fader.Target)?.FixtureIds ?? []],
            Core.Playback.FaderKind.Cuelist => [.. snapshot.Cuelists.FirstOrDefault(l => l.Number == fader.Target)?.FixtureIds ?? []],
            _ => [],
        };
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        var id = (int)((FrameworkElement)sender).Tag;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var selected = Host.Snapshot.Selection.Contains(id);

        string command;
        if (shift && _lastClicked != 0 && _lastClicked != id)
        {
            var range = $"Fixture {Math.Min(_lastClicked, id)} Thru {Math.Max(_lastClicked, id)}";
            command = ctrl ? "+ " + range : range;
        }
        else if (ctrl)
        {
            command = (selected ? "- " : "+ ") + $"Fixture {id}";
        }
        else
        {
            command = $"Fixture {id}";
        }

        _lastClicked = id;
        Host.Click(command);
    }

    // ---- groups ----------------------------------------------------------------------------------------------

    private void UpdateGroups(ConsoleSnapshot snapshot)
    {
        var signature = string.Join('|', snapshot.Groups.Select(g => $"{g.Number}:{g.Label}:{g.Count}:{g.IsSelected}"));
        if (GroupStrip.Tag as string == signature)
            return;

        GroupStrip.Tag = signature;
        GroupStrip.Children.Clear();
        if (snapshot.Groups.Count == 0)
        {
            GroupStrip.Children.Add(new TextBlock
            {
                Text = "none yet – select fixtures and record one with Record Group 1",
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }

        foreach (var group in snapshot.Groups)
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("Chip"),
                Content = $"{group.Number}  {group.Label}",
                IsChecked = group.IsSelected,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = $"Group {group.Number}: Fixture {group.Fixtures}",
                Focusable = false,
            };
            var number = group.Number;
            button.Click += (_, _) => Host.Click($"Group {number}");
            GroupStrip.Children.Add(button);
        }
    }

    // ---- selection helpers -----------------------------------------------------------------------------------

    private List<int> AllIds => Host.Snapshot.Fixtures.Select(f => f.Id).ToList();

    private void SelectIds(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0)
            return;

        Host.Click("Fixture " + SelectionFormatter.Format(list));
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SelectIds(AllIds);

    private void SelectOdd_Click(object sender, RoutedEventArgs e) => SelectIds(AllIds.Where((_, i) => i % 2 == 0));

    private void SelectEven_Click(object sender, RoutedEventArgs e) => SelectIds(AllIds.Where((_, i) => i % 2 == 1));

    private void Invert_Click(object sender, RoutedEventArgs e) => SelectIds(AllIds.Where(id => !Host.Snapshot.Selection.Contains(id)));

    private void Next_Click(object sender, RoutedEventArgs e) => Step(1);

    private void Prev_Click(object sender, RoutedEventArgs e) => Step(-1);

    private void Step(int direction)
    {
        var ids = AllIds;
        if (ids.Count == 0)
            return;

        var selected = Host.Snapshot.Selection;
        var anchor = selected.Count == 0 ? (direction > 0 ? -1 : ids.Count) : ids.FindLastIndex(selected.Contains);
        if (direction < 0 && selected.Count > 0)
            anchor = ids.FindIndex(selected.Contains);

        var next = ((anchor + direction) % ids.Count + ids.Count) % ids.Count;
        Host.Click($"Fixture {ids[next]}");
    }

    private void Deselect_Click(object sender, RoutedEventArgs e) => Host.Run("Clear");

    private void ClearAll_Click(object sender, RoutedEventArgs e) => Host.Run("ClearAll");

    // ---- attribute panel -------------------------------------------------------------------------------------

    private static readonly (AttributeGroup Group, string Title)[] Groups =
    [
        (AttributeGroup.Intensity, "INTENSITY"),
        (AttributeGroup.Position, "POSITION"),
        (AttributeGroup.Color, "COLOR"),
        (AttributeGroup.Beam, "BEAM"),
        (AttributeGroup.Control, "CONTROL"),
    ];

    private void UpdateAttributes(ConsoleSnapshot snapshot)
    {
        var count = snapshot.Selection.Count;
        SelectionTitle.Text = count == 0 ? "–" : $"{snapshot.SelectionText}   ({count})";

        var available = snapshot.Attributes.Select(a => a.Attribute.Group()).Distinct().ToHashSet();
        if (!available.Contains(_group) && available.Count > 0)
            _group = Groups.First(g => available.Contains(g.Group)).Group;

        BuildTabs(available);

        var attributes = snapshot.Attributes.Where(a => a.Attribute.Group() == _group).ToList();
        var signature = _group + "|" + string.Join(',', attributes.Select(a => $"{a.Attribute}:{a.Functions.Count}"))
            + (_group == AttributeGroup.Color ? "|" + string.Join(',', snapshot.Colors.Select(c => c.Name + c.Hex)) : "");
        if (signature != _signature)
        {
            _signature = signature;
            BuildBody(snapshot, attributes);
        }

        UpdateValues(attributes);
    }

    private void BuildTabs(HashSet<AttributeGroup> available)
    {
        var signature = string.Join(',', available.Order()) + "|" + _group;
        if (GroupTabs.Tag as string == signature)
            return;

        GroupTabs.Tag = signature;
        GroupTabs.Children.Clear();
        _tabs.Clear();
        foreach (var (group, title) in Groups)
        {
            var tab = new RadioButton
            {
                Style = (Style)FindResource("ChipRadio"),
                Content = title,
                GroupName = "AttributeGroup",
                IsChecked = group == _group,
                IsEnabled = available.Contains(group),
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(9, 4, 9, 4),
                Focusable = false,
            };
            var captured = group;
            System.Windows.Automation.AutomationProperties.SetAutomationId(tab, "AttrTab" + group);
            tab.Checked += (_, _) =>
            {
                if (_group == captured)
                    return;

                _group = captured;
                _signature = "";
                UpdateAttributes(Host.Snapshot);
            };
            _tabs[group] = tab;
            GroupTabs.Children.Add(tab);
        }
    }

    private void BuildBody(ConsoleSnapshot snapshot, List<SelectedAttribute> attributes)
    {
        AttributeBody.Children.Clear();
        _bars.Clear();
        _functionButtons.Clear();
        _pad = null;

        if (snapshot.Selection.Count == 0)
        {
            AttributeBody.Children.Add(Hint("Click fixtures on the left to select them. Click a value here and it is written to the command line and runs."));
            return;
        }

        if (attributes.Count == 0)
        {
            AttributeBody.Children.Add(Hint("The selected fixtures have no attributes on this page."));
            return;
        }

        switch (_group)
        {
            case AttributeGroup.Intensity:
                BuildIntensity(attributes);
                break;
            case AttributeGroup.Position:
                BuildPosition(attributes);
                break;
            case AttributeGroup.Color:
                BuildColor(attributes);
                break;
            default:
                BuildGeneric(attributes);
                break;
        }
    }

    private UIElement Hint(string text) => new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)FindResource("MutedBrush"),
        Margin = new Thickness(0, 8, 0, 0),
    };

    private LevelBar Bar(FixtureAttribute attribute, string? command = null)
    {
        var bar = new LevelBar
        {
            Caption = attribute.ToString(),
            Fill = (Brush)FindResource(BarBrush(attribute)),
            Margin = new Thickness(0, 0, 0, 6),
        };
        bar.Committed += percent => Host.Click($"{command ?? attribute.ToString()} At {percent}");
        _bars[attribute] = bar;
        return bar;
    }

    private static string BarBrush(FixtureAttribute attribute) => attribute switch
    {
        FixtureAttribute.Red => "RedBrush",
        FixtureAttribute.Green => "GreenBrush",
        FixtureAttribute.Blue => "BlueBrush",
        _ => "AmberBrush",
    };

    private UIElement ButtonRow(IEnumerable<(string Text, string Command)> buttons, int columns)
    {
        var grid = new UniformGrid { Columns = columns, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (text, command) in buttons)
        {
            var captured = command;
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 3, 3), Padding = new Thickness(0, 6, 0, 6) };
            button.Click += (_, _) => Host.Click(captured);
            grid.Children.Add(button);
        }

        return grid;
    }

    private void BuildIntensity(List<SelectedAttribute> attributes)
    {
        AttributeBody.Children.Add(Bar(FixtureAttribute.Dimmer));

        var levels = Enumerable.Range(0, 11).Select(i => (i == 0 ? "Out" : i == 10 ? "Full" : $"{i * 10}", $"At {i * 10}"));
        AttributeBody.Children.Add(ButtonRow(levels, 6));
        AttributeBody.Children.Add(ButtonRow([("+10", RelativeCommand(+10)), ("−10", RelativeCommand(-10)), ("+1", RelativeCommand(+1)), ("−1", RelativeCommand(-1))], 4));
    }

    /// <summary>A relative step is typed as an absolute level, so it is an ordinary command that Oops and Companion understand.</summary>
    private string RelativeCommand(int delta)
    {
        var current = Host.Snapshot.Attributes.FirstOrDefault(a => a.Attribute == FixtureAttribute.Dimmer)?.Value ?? 0;
        var next = Math.Clamp((int)Math.Round(current * 100) + delta, 0, 100);
        return $"At {next}";
    }

    private void BuildPosition(List<SelectedAttribute> attributes)
    {
        if (attributes.Any(a => a.Attribute == FixtureAttribute.Pan) && attributes.Any(a => a.Attribute == FixtureAttribute.Tilt))
        {
            _pad = new XyPad { Height = 220, Margin = new Thickness(0, 0, 0, 10) };
            _pad.Committed += (x, y) => Host.Click($"Pan At {x} Tilt At {y}");
            AttributeBody.Children.Add(_pad);
            AttributeBody.Children.Add(ButtonRow(
            [
                ("Center", "Pan At 50 Tilt At 50"),
                ("Front", "Pan At 50 Tilt At 30"),
                ("Up", "Pan At 50 Tilt At 80"),
                ("Home", "Pan At 0 Tilt At 0"),
            ], 4));
        }

        BuildGeneric(attributes);
    }

    /// <summary>Black text on light colours and white text on dark ones, so every swatch name can be read.</summary>
    public static Brush ReadableOn(Color color)
    {
        var luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
        return luminance > 125 ? Brushes.Black : Brushes.White;
    }

    private void BuildColor(List<SelectedAttribute> attributes)
    {
        var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var preset in Host.Snapshot.Colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(preset.Hex);
            var button = new Button
            {
                // The text is a TextBlock of its own: a plain string would take the default text colour of the theme.
                Content = new TextBlock
                {
                    Text = preset.Name,
                    Foreground = ReadableOn(color),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                },
                Width = 88,
                Height = 44,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = (Brush)FindResource(preset.Custom ? "AmberBrush" : "LineStrongBrush"),
                ToolTip = preset.Custom ? $"Color {preset.Name} (your own)" : $"Color {preset.Name}",
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, "Color" + preset.Name.Replace(" ", ""));
            var command = preset.Name.Contains(' ') ? $"Color {CommandText.Quote(preset.Name)}" : $"Color {preset.Name}";
            button.Click += (_, _) => Host.Click(command);
            swatches.Children.Add(button);
        }

        var library = new Button
        {
            Content = "+ Colours…",
            Width = 88,
            Height = 44,
            Margin = new Thickness(0, 0, 4, 4),
            FontSize = 11,
            ToolTip = "Add, import or export your own colours",
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(library, "ColorLibrary");
        library.Click += (_, _) => new ColorLibraryWindow(Host) { Owner = Window.GetWindow(this) }.ShowDialog();
        swatches.Children.Add(library);

        AttributeBody.Children.Add(swatches);
        BuildGeneric(attributes);
    }

    /// <summary>A bar for every attribute of the page, with buttons for the named functions of its channel.</summary>
    private void BuildGeneric(List<SelectedAttribute> attributes)
    {
        foreach (var item in attributes.Where(a => _group != AttributeGroup.Intensity))
        {
            if (_bars.ContainsKey(item.Attribute))
                continue;

            AttributeBody.Children.Add(Bar(item.Attribute));

            if (item.Functions.Count == 0)
                continue;

            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var function in item.Functions)
            {
                var button = new ToggleButton
                {
                    Style = (Style)FindResource("Chip"),
                    Content = function.Name,
                    Margin = new Thickness(0, 0, 3, 3),
                    Padding = new Thickness(8, 3, 8, 3),
                    MinHeight = 24,
                    ToolTip = $"DMX {function.From}–{function.To}",
                    Focusable = false,
                };
                var command = $"{item.Attribute} Function {CommandText.Quote(function.Name)}";
                button.Click += (_, _) => Host.Click(command);
                _functionButtons[(item.Attribute, function.Name)] = button;
                wrap.Children.Add(button);
            }

            AttributeBody.Children.Add(wrap);
        }
    }

    private void UpdateValues(List<SelectedAttribute> attributes)
    {
        foreach (var item in attributes)
        {
            if (_bars.TryGetValue(item.Attribute, out var bar))
            {
                bar.Value = item.Value;
                bar.IsProgrammed = item.Programmed;
                bar.IsMixed = item.Mixed;
            }

            foreach (var function in item.Functions)
            {
                if (_functionButtons.TryGetValue((item.Attribute, function.Name), out var button))
                    button.IsChecked = item.ActiveFunction == function.Name;
            }
        }

        if (_pad is not null)
        {
            var pan = attributes.FirstOrDefault(a => a.Attribute == FixtureAttribute.Pan);
            var tilt = attributes.FirstOrDefault(a => a.Attribute == FixtureAttribute.Tilt);
            _pad.Pan = pan?.Mixed == false ? pan.Value : null;
            _pad.Tilt = tilt?.Mixed == false ? tilt.Value : null;
        }
    }
}
