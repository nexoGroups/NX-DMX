using System.Windows;
using System.Windows.Controls;
using NxDmx.App.Services;
using NxDmx.Core.Dmx;

namespace NxDmx.App.Views;

/// <summary>
/// Shows the DMX values of one universe as the text of the Bitfocus Companion action "Generic Art-Net: Set Complex Value",
/// ready to copy. The values are the ones at the moment the window was opened.
/// </summary>
public sealed class CompanionExportWindow : DarkWindow
{
    private readonly byte[] _data;
    private readonly IReadOnlyList<(int Start, int End)> _patched;
    private readonly RadioButton _patchedScope = Scope("Patched channels", "Every channel of a patched fixture, zeros included. Fires the whole look.");
    private readonly RadioButton _nonZeroScope = Scope("Only above zero", "Only the channels that are on. Leaves everything else as it is.");
    private readonly RadioButton _allScope = Scope("All 512", "Every channel of the universe.");
    private readonly TextBox _text = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        Height = 130,
    };
    private readonly TextBlock _count = Ui.Text("", "MutedBrush", margin: new Thickness(0, 4, 0, 0));
    private readonly TextBlock _copied = Ui.Text("", "GreenBrush", wrap: false, margin: new Thickness(0, 0, 12, 0));

    public CompanionExportWindow(int universe, byte[] data, IReadOnlyList<AddressBlock> blocks)
    {
        _data = (byte[])data.Clone();
        _patched = blocks.Select(b => (b.Start, b.End)).ToList();
        Title = "Export for Companion";
        SizeToContent = SizeToContent.Height;
        Width = 520;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(Ui.Caption($"Universe {universe} · Bitfocus Companion"));
        panel.Children.Add(Ui.Text(
            "Paste this into the action \"Generic Art-Net: Set Complex Value\". It sets the channels to the values you see in the monitor right now.",
            "DimBrush", margin: new Thickness(0, 0, 0, 10)));

        var scopes = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var scope in new[] { _patchedScope, _nonZeroScope, _allScope })
        {
            scope.Margin = new Thickness(0, 0, 4, 4);
            scope.Checked += (_, _) => Fill();
            scopes.Children.Add(scope);
        }

        panel.Children.Add(Ui.Caption("Which channels"));
        panel.Children.Add(scopes);
        panel.Children.Add(Ui.Caption("Values (channel,value;)"));
        panel.Children.Add(_text);
        panel.Children.Add(_count);
        panel.Children.Add(Ui.Text(
            $"Set the universe in the Companion action to the one your node listens to. NX-DMX sends universe {universe} as Art-Net Port-Address {universe - 1}.",
            "MutedBrush", margin: new Thickness(0, 8, 0, 0)));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(_copied);
        var close = Ui.Button("Close", Close, width: 100);
        var copy = Ui.Button("Copy", Copy, "GreenButton", "Copy the text to the clipboard", width: 100);
        System.Windows.Automation.AutomationProperties.SetAutomationId(copy, "CompanionCopy");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_text, "CompanionText");
        close.Margin = new Thickness(0, 0, 6, 0);
        buttons.Children.Add(close);
        buttons.Children.Add(copy);
        panel.Children.Add(buttons);
        Content = panel;

        // Patched channels are the useful default; a universe without fixtures falls back to what is on.
        (_patched.Count > 0 ? _patchedScope : _nonZeroScope).IsChecked = true;
    }

    private static RadioButton Scope(string text, string tooltip) =>
        new() { Content = text, ToolTip = tooltip, Style = Ui.Style("ChipRadio"), GroupName = "CompanionScope", MinWidth = 110 };

    private CompanionScope Selected => _allScope.IsChecked == true ? CompanionScope.All : _nonZeroScope.IsChecked == true ? CompanionScope.NonZero : CompanionScope.Patched;

    private void Fill()
    {
        _text.Text = CompanionExport.Format(_data, Selected, _patched);
        var pairs = _text.Text.Count(c => c == ';');
        _count.Text = pairs == 0 ? "No channels to export." : $"{pairs} channel{(pairs == 1 ? "" : "s")}.";
        _copied.Text = "";
    }

    private void Copy()
    {
        if (_text.Text.Length == 0)
            return;

        try
        {
            Clipboard.SetText(_text.Text);
            _copied.Text = "Copied.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another program holds the clipboard for a moment. Selecting the text lets the operator copy it by hand.
            _text.Focus();
            _text.SelectAll();
            _copied.Text = "Clipboard busy – press Ctrl+C.";
        }
    }
}
