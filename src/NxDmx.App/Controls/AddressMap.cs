using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NxDmx.App.Services;

namespace NxDmx.App.Controls;

/// <summary>
/// The DMX address space of every universe as a bar of 512 channels, with each fixture as a block. Free space is
/// dark, a fixture that overlaps another is red. Clicking a bar reports universe and channel, to pick a start address.
/// </summary>
public sealed class AddressMap : FrameworkElement
{
    private const double RowHeight = 30;
    private const double LabelWidth = 130;

    private IReadOnlyList<UniverseInfo> _universes = [];
    private int _hotUniverse;
    private int _hotChannel;

    public AddressMap()
    {
        Cursor = Cursors.Cross;
        Focusable = false;
        ClipToBounds = true;
    }

    /// <summary>Universe and first channel of a click on free or occupied space.</summary>
    public event Action<int, int>? AddressPicked;

    public int SelectedUniverse { get; set; }

    public void Show(IReadOnlyList<UniverseInfo> universes, int selectedUniverse)
    {
        _universes = universes;
        SelectedUniverse = selectedUniverse;
        Height = Math.Max(RowHeight, universes.Count * RowHeight);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, Math.Max(RowHeight, _universes.Count * RowHeight));

    protected override void OnRender(DrawingContext dc)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var barWidth = Math.Max(10, ActualWidth - LabelWidth - 8);
        var unit = barWidth / 512.0;
        var back = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x0E));
        var line = new Pen((Brush)Application.Current.FindResource("LineBrush"), 1);
        var red = (Brush)Application.Current.FindResource("RedBrush");
        var amber = (Brush)Application.Current.FindResource("AmberBrush");
        var dim = (Brush)Application.Current.FindResource("DimBrush");
        var blocks = new[]
        {
            new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x5A)),
            new SolidColorBrush(Color.FromRgb(0x4A, 0x5A, 0x6A)),
        };

        for (var row = 0; row < _universes.Count; row++)
        {
            var universe = _universes[row];
            var top = row * RowHeight + 4;
            var height = RowHeight - 8;

            var selected = universe.Number == SelectedUniverse;
            var label = new FormattedText($"{universe.Number}  {universe.Label}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, selected ? amber : dim, dpi) { MaxTextWidth = LabelWidth - 10, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(label, new Point(4, top + (height - label.Height) / 2));

            dc.DrawRectangle(back, line, new Rect(LabelWidth, top, barWidth, height));

            var index = 0;
            foreach (var block in universe.Blocks)
            {
                var x = LabelWidth + (block.Start - 1) * unit;
                var width = Math.Max(1, (Math.Min(block.End, 512) - block.Start + 1) * unit);
                dc.DrawRectangle(block.Conflict ? red : blocks[index++ % 2], null, new Rect(x, top + 1, Math.Max(1, width - 1), height - 2));
                if (width > 22)
                {
                    var text = new FormattedText(block.FixtureId.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Consolas"), 10.5, Brushes.White, dpi) { MaxTextWidth = width - 3, MaxLineCount = 1, Trimming = TextTrimming.None };
                    dc.DrawText(text, new Point(x + 3, top + (height - text.Height) / 2));
                }
            }

            if (_hotUniverse == universe.Number && _hotChannel > 0)
            {
                var x = LabelWidth + (_hotChannel - 1) * unit;
                dc.DrawRectangle(null, new Pen(amber, 1), new Rect(x, top, Math.Max(2, unit), height));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        (_hotUniverse, _hotChannel) = Locate(e.GetPosition(this));
        ToolTip = _hotChannel > 0 ? $"Universe {_hotUniverse}, channel {_hotChannel}" : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hotUniverse = _hotChannel = 0;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var (universe, channel) = Locate(e.GetPosition(this));
        if (channel > 0)
            AddressPicked?.Invoke(universe, channel);
    }

    private (int Universe, int Channel) Locate(Point p)
    {
        var row = (int)(p.Y / RowHeight);
        if (row < 0 || row >= _universes.Count || p.X < LabelWidth)
            return (0, 0);

        var barWidth = Math.Max(10, ActualWidth - LabelWidth - 8);
        var channel = (int)((p.X - LabelWidth) / barWidth * 512) + 1;
        return channel is >= 1 and <= 512 ? (_universes[row].Number, channel) : (0, 0);
    }
}
