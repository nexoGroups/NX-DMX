using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NxDmx.App.Controls;

/// <summary>
/// A flat bar for a value from 0 to 100 %. Clicking or dragging shows the new value at once and reports it when the
/// mouse is released, so one drag makes one command instead of dozens.
/// </summary>
public sealed class LevelBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double?), typeof(LevelBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(LevelBar),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LevelBar),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsProgrammedProperty = DependencyProperty.Register(
        nameof(IsProgrammed), typeof(bool), typeof(LevelBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsMixedProperty = DependencyProperty.Register(
        nameof(IsMixed), typeof(bool), typeof(LevelBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private double? _drag;

    public LevelBar()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
        Height = 34;
        MinWidth = 60;
        ClipToBounds = true;
    }

    public double? Value
    {
        get => (double?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Marks a value that was set in the programmer (red) as opposed to one that comes from a playback.</summary>
    public bool IsProgrammed
    {
        get => (bool)GetValue(IsProgrammedProperty);
        set => SetValue(IsProgrammedProperty, value);
    }

    /// <summary>The selected fixtures disagree about the value.</summary>
    public bool IsMixed
    {
        get => (bool)GetValue(IsMixedProperty);
        set => SetValue(IsMixedProperty, value);
    }

    /// <summary>Raised on mouse release with the chosen value in percent (0..100, whole numbers).</summary>
    public event Action<int>? Committed;

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x0E)), new Pen(Brush("LineBrush"), 1), new Rect(0.5, 0.5, width - 1, height - 1));

        var value = _drag ?? Value;
        if (value is { } v)
        {
            var filled = Math.Max(0, (width - 2) * Math.Clamp(v, 0, 1));
            var fill = IsProgrammed || _drag is not null ? Brush("RedBrush") : Fill;
            dc.PushOpacity(0.55);
            dc.DrawRectangle(fill, null, new Rect(1, 1, filled, height - 2));
            dc.Pop();
            dc.DrawRectangle(fill, null, new Rect(1 + filled - 2 < 1 ? 1 : 1 + filled - 2, 1, 2, height - 2));
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        var caption = new FormattedText(Caption.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 10, Brush("DimBrush"), dpi);
        dc.DrawText(caption, new Point(7, (height - caption.Height) / 2));

        var text = IsMixed ? "mixed" : value is { } shown ? $"{Math.Round(shown * 100):0} %" : "–";
        var number = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13,
            IsProgrammed || _drag is not null ? Brush("RedBrush") : Brushes.White, dpi);
        dc.DrawText(number, new Point(width - number.Width - 7, (height - number.Height) / 2));
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

        var percent = (int)Math.Round(Pick(e) * 100);
        ReleaseMouseCapture();
        _drag = null;
        InvalidateVisual();
        Committed?.Invoke(percent);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _drag = null;
        InvalidateVisual();
    }

    private double Pick(MouseEventArgs e) =>
        Math.Round(Math.Clamp(e.GetPosition(this).X / Math.Max(1, ActualWidth), 0, 1) * 100) / 100;

    private Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
