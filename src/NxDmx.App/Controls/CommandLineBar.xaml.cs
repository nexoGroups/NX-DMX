using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NxDmx.App.Services;

namespace NxDmx.App.Controls;

/// <summary>The command line: what the operator types and what clicks in the views compose.</summary>
public partial class CommandLineBar : UserControl
{
    private ConsoleHost? _host;
    private bool _updating;

    public CommandLineBar()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the keypad button is pressed.</summary>
    public event Action<bool>? KeypadToggled;

    /// <summary>Raised when the FADERS button is pressed.</summary>
    public event Action<bool>? FadersToggled;

    public void SetKeypadChecked(bool visible) => KeypadToggle.IsChecked = visible;

    public void SetFadersChecked(bool visible) => FadersToggle.IsChecked = visible;

    private void FadersToggle_Click(object sender, RoutedEventArgs e)
    {
        FadersToggled?.Invoke(FadersToggle.IsChecked == true);
        FocusLine();
    }

    public void Attach(ConsoleHost host, bool keypadVisible)
    {
        _host = host;
        KeypadToggle.IsChecked = keypadVisible;
        host.CommandLineChanged += SyncFromHost;
        host.StateChanged += ShowState;
        ShowState();
    }

    public void FocusLine()
    {
        Line.Focus();
        Line.CaretIndex = Line.Text.Length;
    }

    /// <summary>Puts typed text into the command line even when something else had the focus.</summary>
    public void TypeText(string text)
    {
        Line.Focus();
        Line.Text += text;
        Line.CaretIndex = Line.Text.Length;
    }

    private void SyncFromHost()
    {
        if (_host is null || Line.Text == _host.CommandLine)
            return;

        _updating = true;
        Line.Text = _host.CommandLine;
        Line.CaretIndex = Line.Text.Length;
        _updating = false;
    }

    private void Line_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updating && _host is not null)
            _host.CommandLine = Line.Text;
    }

    private void ShowState()
    {
        if (_host is null)
            return;

        var snapshot = _host.Snapshot;
        SelectionText.Text = snapshot.SelectionText.Length == 0 ? "" : $"SEL {snapshot.SelectionText}";
        ProgrammerText.Text = snapshot.ProgrammerValueCount == 0 ? "" : $"PRG {snapshot.ProgrammerValueCount}";
    }

    private void Line_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_host is null)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                _host.Submit();
                e.Handled = true;
                break;
            case Key.Escape:
                _host.CommandLine = "";
                _host.ResetHistoryPosition();
                e.Handled = true;
                break;
            case Key.Up:
                if (_host.HistoryStep(-1) is { } older)
                    _host.CommandLine = older;
                e.Handled = true;
                break;
            case Key.Down:
                if (_host.HistoryStep(1) is { } newer)
                    _host.CommandLine = newer;
                e.Handled = true;
                break;
        }
    }

    private void Please_Click(object sender, RoutedEventArgs e)
    {
        _host?.Submit();
        FocusLine();
    }

    private void Oops_Click(object sender, RoutedEventArgs e)
    {
        _host?.Run("Oops");
        FocusLine();
    }

    /// <summary>Like on grandMA: the first press empties the command line, the next ones step back through Clear.</summary>
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null)
            return;

        if (_host.CommandLine.Length > 0)
            _host.CommandLine = "";
        else
            _host.Run("Clear");

        FocusLine();
    }

    private void KeypadToggle_Click(object sender, RoutedEventArgs e)
    {
        KeypadToggled?.Invoke(KeypadToggle.IsChecked == true);
        FocusLine();
    }
}
