using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NxDmx.App.Services;

namespace NxDmx.App.Controls;

/// <summary>The 512 channel values of one universe as a grid of cells, each with a bar for its level.</summary>
public sealed class DmxMonitor : FrameworkElement
{
    private const double CellHeight = 24;
    private const double LabelWidth = 38;

    private byte[] _data = new byte[512];
    private bool _hasData;
    private IReadOnlyList<AddressBlock> _blocks = [];
    private int _hot;

    public DmxMonitor()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    private int Columns => ActualWidth >= LabelWidth + 32 * 28 ? 32 : 16;

    public void Show(byte[]? data, IReadOnlyList<AddressBlock> blocks)
    {
        _hasData = data is not null;
        _data = data ?? new byte[512];
        _blocks = blocks;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        var columns = width >= LabelWidth + 32 * 28 ? 32 : 16;
        return new Size(width, 512 / columns * CellHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var columns = Columns;
        var rows = 512 / columns;
        var cellWidth = (ActualWidth - LabelWidth) / columns;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Consolas");
        var line = new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), 1);
        var dim = (Brush)Application.Current.FindResource("MutedBrush");
        var amber = (Brush)Application.Current.FindResource("AmberBrush");
        var bar = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xB0, 0x00));
        var owner = new int[513];
        foreach (var block in _blocks)
        {
            for (var channel = block.Start; channel <= Math.Min(block.End, 512); channel++)
                owner[channel] = block.FixtureId;
        }

        for (var row = 0; row < rows; row++)
        {
            var y = row * CellHeight;
            var label = new FormattedText((row * columns + 1).ToString("000", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, dim, dpi);
            dc.DrawText(label, new Point(4, y + (CellHeight - label.Height) / 2));

            for (var column = 0; column < columns; column++)
            {
                var channel = row * columns + column + 1;
                var value = _data[channel - 1];
                var x = LabelWidth + column * cellWidth;
                var cell = new Rect(x, y, cellWidth, CellHeight);

                if (owner[channel] != 0)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C)), null, cell);

                if (value > 0)
                    dc.DrawRectangle(bar, null, new Rect(x + 1, y + CellHeight - 1 - (CellHeight - 2) * value / 255.0, cellWidth - 2, (CellHeight - 2) * value / 255.0));

                dc.DrawRectangle(null, channel == _hot ? new Pen(amber, 1) : line, cell);

                var text = new FormattedText(value == 0 ? "·" : value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 11.5, value == 0 ? dim : Brushes.White, dpi);
                dc.DrawText(text, new Point(x + (cellWidth - text.Width) / 2, y + (CellHeight - text.Height) / 2));
            }
        }

        if (!_hasData)
        {
            var note = new FormattedText("Nothing is sent in this universe yet. Patch a fixture or turn the universe on.", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, dim, dpi);
            dc.DrawText(note, new Point(LabelWidth + 8, 6));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var columns = Columns;
        var cellWidth = (ActualWidth - LabelWidth) / columns;
        var p = e.GetPosition(this);
        var channel = p.X < LabelWidth ? 0 : (int)(p.Y / CellHeight) * columns + (int)((p.X - LabelWidth) / cellWidth) + 1;
        if (channel is < 1 or > 512)
            channel = 0;

        if (channel != _hot)
        {
            _hot = channel;
            var fixture = channel == 0 ? 0 : _blocks.FirstOrDefault(b => channel >= b.Start && channel <= b.End)?.FixtureId ?? 0;
            ToolTip = channel == 0 ? null : $"Channel {channel} = {_data[channel - 1]}{(fixture != 0 ? $"  ·  Fixture {fixture}" : "")}";
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hot = 0;
        InvalidateVisual();
    }
}
