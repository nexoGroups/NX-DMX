using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NxDmx.App.Views;

/// <summary>Small helpers for building the views in code with the look of the theme.</summary>
internal static class Ui
{
    public static Style Style(string key) => (Style)Application.Current.FindResource(key);

    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    public static TextBlock Caption(string text, Thickness? margin = null) => new()
    {
        Text = text.ToUpperInvariant(),
        Style = Style("Caption"),
        Margin = margin ?? new Thickness(0, 0, 0, 3),
    };

    public static TextBlock Text(string text, string brush = "TextBrush", bool wrap = true, Thickness? margin = null) => new()
    {
        Text = text,
        Foreground = Brush(brush),
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Margin = margin ?? new Thickness(0),
    };

    public static Button Button(string text, Action onClick, string? style = null, string? tooltip = null, double? width = null)
    {
        var button = new Button { Content = text, ToolTip = tooltip };
        if (style is not null)
            button.Style = Style(style);
        if (width is { } w)
            button.Width = w;

        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A caption above a control, for forms.</summary>
    public static StackPanel Field(string caption, UIElement control, double? width = null, Thickness? margin = null)
    {
        var panel = new StackPanel { Margin = margin ?? new Thickness(0, 0, 12, 10) };
        panel.Children.Add(Caption(caption));
        if (width is { } w && control is FrameworkElement element)
            element.Width = w;

        panel.Children.Add(control);
        return panel;
    }

    public static TextBox Input(string text = "", double? width = null) => new() { Text = text, Width = width ?? double.NaN };

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Style = Style("PanelBorder"),
        Padding = padding ?? new Thickness(14),
        Child = child,
    };

    /// <summary>The text of a text box as a whole number, or null when it is not one.</summary>
    public static int? ToInt(string? text) => int.TryParse(text?.Trim(), out var value) ? value : null;
}
