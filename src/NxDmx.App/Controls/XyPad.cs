using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NxDmx.App.Controls;

/// <summary>A square pad for pan (across) and tilt (up). Reports the position, in percent, when the mouse is released.</summary>
public sealed class XyPad : FrameworkElement
{
    public static readonly DependencyProperty PanProperty = DependencyProperty.Register(
        nameof(Pan), typeof(double?), typeof(XyPad), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TiltProperty = DependencyProperty.Register(
        nameof(Tilt), typeof(double?), typeof(XyPad), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private Point? _drag;

    public XyPad()
    {
        Cursor = Cursors.Cross;
        Focusable = false;
        MinWidth = 120;
        MinHeight = 120;
    }

    public double? Pan
    {
        get => (double?)GetValue(PanProperty);
        set => SetValue(PanProperty, value);
    }

    public double? Tilt
    {
        get => (double?)GetValue(TiltProperty);
        set => SetValue(TiltProperty, value);
    }

    /// <summary>Pan and tilt in percent (0..100).</summary>
    public event Action<int, int>? Committed;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var line = (Brush)Application.Current.FindResource("LineBrush");
        var dim = (Brush)Application.Current.FindResource("MutedBrush");
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x0E)), new Pen(line, 1), new Rect(0.5, 0.5, w - 1, h - 1));

        var grid = new Pen(line, 1);
        for (var i = 1; i < 4; i++)
        {
            dc.DrawLine(grid, new Point(w * i / 4, 1), new Point(w * i / 4, h - 1));
            dc.DrawLine(grid, new Point(1, h * i / 4), new Point(w - 1, h * i / 4));
        }

        var position = _drag ?? (Pan is { } pan && Tilt is { } tilt ? new Point(pan, 1 - tilt) : (Point?)null);
        if (position is { } p)
        {
            var x = Math.Clamp(p.X, 0, 1) * w;
            var y = Math.Clamp(p.Y, 0, 1) * h;
            var accent = (Brush)Application.Current.FindResource(_drag is null ? "AmberBrush" : "RedBrush");
            var cross = new Pen(accent, 1);
            dc.DrawLine(cross, new Point(x, 0), new Point(x, h));
            dc.DrawLine(cross, new Point(0, y), new Point(w, y));
            dc.DrawRectangle(accent, null, new Rect(x - 4, y - 4, 8, 8));

            var text = new FormattedText($"PAN {Math.Round(p.X * 100):0}  TILT {Math.Round((1 - p.Y) * 100):0}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Consolas"), 11, accent, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(6, h - text.Height - 4));
        }
        else
        {
            var text = new FormattedText("PAN / TILT", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, dim, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(6, 5));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        _drag = Pick(e);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured)
            return;

        _drag = Pick(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
            return;

        var p = Pick(e);
        ReleaseMouseCapture();
        _drag = null;
        InvalidateVisual();
        Committed?.Invoke((int)Math.Round(p.X * 100), (int)Math.Round((1 - p.Y) * 100));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _drag = null;
        InvalidateVisual();
    }

    private Point Pick(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(
            Math.Round(Math.Clamp(p.X / Math.Max(1, ActualWidth), 0, 1) * 100) / 100,
            Math.Round(Math.Clamp(p.Y / Math.Max(1, ActualHeight), 0, 1) * 100) / 100);
    }
}
