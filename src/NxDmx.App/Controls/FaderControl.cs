using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NxDmx.App.Controls;

/// <summary>
/// One vertical fader like on a console: a track, a coloured fill up to the level and a cap with the value. Drag the
/// cap or touch anywhere on the track. While dragging it reports the level for a live effect; the mouse (or finger)
/// release reports the final value.
/// </summary>
public sealed class FaderControl : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(FaderControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(FaderControl),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(FaderControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double CapHeight = 30;
    private double? _drag;

    public FaderControl()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
        MinHeight = 60;
        MinWidth = 30;
        ClipToBounds = true;
    }

    /// <summary>0..1. Shown unless the fader is being dragged.</summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>False for a free fader: it is drawn dim and cannot be moved.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Raised while dragging, with the level in percent, at most as often as the pointer moves.</summary>
    public event Action<int>? Moving;

    /// <summary>Raised on release with the final level in percent.</summary>
    public event Action<int>? Committed;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var level = Math.Clamp(_drag ?? Level, 0, 1);
        var line = (Brush)Application.Current.FindResource("LineBrush");
        var dim = (Brush)Application.Current.FindResource("MutedBrush");
        var travel = Math.Max(1, h - CapHeight);
        var capTop = (1 - level) * travel;

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)), new Pen(line, 1), new Rect(0.5, 0.5, w - 1, h - 1));

        // The groove in the middle and the coloured fill from the bottom up to the cap.
        var grooveX = w / 2;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)), null, new Rect(grooveX - 2, 6, 4, h - 12));
        if (IsActive)
        {
            dc.PushOpacity(0.85);
            dc.DrawRectangle(Fill, null, new Rect(grooveX - 2, capTop + CapHeight / 2, 4, h - 6 - (capTop + CapHeight / 2)));
            dc.Pop();
        }

        // Scale ticks every 10 %.
        for (var i = 0; i <= 10; i++)
        {
            var y = CapHeight / 2 + (1 - i / 10.0) * travel;
            dc.DrawLine(new Pen(i % 5 == 0 ? dim : line, 1), new Point(3, y), new Point(i % 5 == 0 ? 9 : 6, y));
            dc.DrawLine(new Pen(i % 5 == 0 ? dim : line, 1), new Point(w - 3, y), new Point(w - (i % 5 == 0 ? 9 : 6), y));
        }

        // The cap, with its value.
        var capColor = IsActive ? Fill : dim;
        var cap = new Rect(4, capTop + 1, w - 8, CapHeight - 2);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), new Pen(capColor, 1.5), cap);
        dc.DrawLine(new Pen(capColor, 2), new Point(cap.Left + 3, cap.Top + cap.Height / 2), new Point(cap.Right - 3, cap.Top + cap.Height / 2));

        if (IsActive)
        {
            var text = new FormattedText($"{Math.Round(level * 100):0}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 10.5, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(cap.Left + (cap.Width - text.Width) / 2, cap.Top + 2));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsActive)
            return;

        CaptureMouse();
        Update(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured)
            Update(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
            return;

        var level = Pick(e);
        ReleaseMouseCapture();
        _drag = null;
        InvalidateVisual();
        Committed?.Invoke((int)Math.Round(level * 100));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _drag = null;
        InvalidateVisual();
    }

    private void Update(MouseEventArgs e)
    {
        _drag = Pick(e);
        InvalidateVisual();
        Moving?.Invoke((int)Math.Round(_drag.Value * 100));
    }

    private double Pick(MouseEventArgs e)
    {
        var travel = Math.Max(1, ActualHeight - CapHeight);
        var y = e.GetPosition(this).Y - CapHeight / 2;
        return Math.Round(Math.Clamp(1 - y / travel, 0, 1) * 100) / 100;
    }
}
