using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>The settings of the app itself (not of the show): start with Windows, interface size, what is shown, what is asked.</summary>
public sealed class SettingsView : ConsoleView
{
    private readonly CheckBox _startup = new() { Content = "Start NX-DMX when Windows starts" };
    private readonly CheckBox _updates = new() { Content = "Look for a new version when NX-DMX starts" };
    private readonly CheckBox _startWindow = new() { Content = "Show the start window when NX-DMX starts" };
    private readonly CheckBox _remember = new() { Content = "Open the view that was open last time" };
    private readonly CheckBox _confirm = new() { Content = "Ask before deleting fixtures, universes and profiles" };
    private readonly CheckBox _keypad = new() { Content = "Show the keypad" };
    private readonly CheckBox _faders = new() { Content = "Show the fader bar" };
    private readonly StackPanel _sizes = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _note = Ui.Text("", "DimBrush", margin: new Thickness(0, 6, 0, 0));
    private bool _loading;

    public SettingsView(ConsoleHost host) : base(host)
    {
        var panel = new StackPanel { Margin = new Thickness(18), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };

        panel.Children.Add(Ui.Caption("Start"));
        panel.Children.Add(Row(_startup, "Off by default. NX-DMX then opens by itself when you sign in to Windows. It never starts any output you did not set up."));
        panel.Children.Add(Row(_startWindow, "Continue where you left off, a new pre-built or empty show, open a show or a recent one. Off goes straight to the show of the last session."));
        panel.Children.Add(Row(_updates, "A new version only shows a green button; NX-DMX never restarts by itself."));
        panel.Children.Add(Row(_remember, "Off always starts in the Fixtures view."));
        OnToggled(_startup, ToggleStartup);
        OnToggled(_startWindow, () => Change(s => s.ShowStartWindow = _startWindow.IsChecked == true));
        OnToggled(_updates, () => Change(s => s.CheckUpdatesOnStart = _updates.IsChecked == true));
        OnToggled(_remember, () => Change(s => s.RememberView = _remember.IsChecked == true));
        System.Windows.Automation.AutomationProperties.SetAutomationId(_startup, "SettingStartup");

        panel.Children.Add(Rule());
        panel.Children.Add(Ui.Caption("Interface"));
        panel.Children.Add(Ui.Text("Larger sizes make every button bigger, which is easier on a touch screen. The window may need more room.", "DimBrush", margin: new Thickness(0, 0, 0, 8)));
        foreach (var percent in new[] { 100, 125, 150, 175, 200 })
        {
            var chip = new RadioButton { Content = $"{percent} %", Style = Ui.Style("ChipRadio"), GroupName = "UiScale", Tag = percent, MinWidth = 76, Margin = new Thickness(0, 0, 4, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(chip, "Scale" + percent);
            chip.Checked += (_, _) => Change(s => s.UiScalePercent = percent);
            _sizes.Children.Add(chip);
        }

        panel.Children.Add(_sizes);
        panel.Children.Add(Row(_keypad, "The number keys at the bottom right."));
        panel.Children.Add(Row(_faders, "The fader bar at the bottom left. It can also be opened in a window of its own."));
        OnToggled(_keypad, () => Change(s => s.KeypadVisible = _keypad.IsChecked == true));
        OnToggled(_faders, () => { if (!_loading) Host.SetFaderBar(_faders.IsChecked == true, popped: Host.Settings.FadersPopped); });

        panel.Children.Add(Rule());
        panel.Children.Add(Ui.Caption("Safety"));
        panel.Children.Add(Row(_confirm, "Oops can take a deletion back, but a question first avoids a slip on a touch screen."));
        OnToggled(_confirm, () => Change(s => s.ConfirmDeletes = _confirm.IsChecked == true));

        panel.Children.Add(Rule());
        panel.Children.Add(Ui.Caption("About"));
        panel.Children.Add(Ui.Text($"NX-DMX {Host.Updates.CurrentVersion}", "TextBrush"));
        var folder = Ui.Button("Open the data folder", OpenFolder, tooltip: "Settings, the automatic backup and error.log are stored there");
        folder.Margin = new Thickness(0, 10, 0, 0);
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(folder);
        panel.Children.Add(_note);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        Host.FaderLayoutChanged += Refresh;
    }

    /// <summary>Reacts to a switch however it was changed: mouse, finger, keyboard or automation.</summary>
    private static void OnToggled(CheckBox box, Action changed)
    {
        box.Checked += (_, _) => changed();
        box.Unchecked += (_, _) => changed();
    }

    private static UIElement Row(CheckBox box, string hint)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        box.MinHeight = 28;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        row.Children.Add(box);
        row.Children.Add(Ui.Text(hint, "MutedBrush", margin: new Thickness(24, 2, 0, 0)));
        return row;
    }

    private static UIElement Rule() => new Border { Height = 1, Background = Ui.Brush("LineBrush"), Margin = new Thickness(0, 6, 0, 16) };

    private void Change(Action<AppSettings> change)
    {
        if (_loading)
            return;

        change(Host.Settings);
        Host.SaveSettings();
    }

    private void ToggleStartup()
    {
        if (_loading)
            return;

        var error = WindowsStartup.Set(_startup.IsChecked == true);
        if (error is not null)
        {
            _note.Text = "Windows would not change the setting: " + error;
            _note.Foreground = Ui.Brush("RedBrush");
        }
        else
        {
            _note.Text = _startup.IsChecked == true ? "NX-DMX will start with Windows from the next sign-in." : "NX-DMX no longer starts with Windows.";
            _note.Foreground = Ui.Brush("DimBrush");
        }

        Host.Settings.StartWithWindows = WindowsStartup.IsEnabled();
        Host.SaveSettings();
        _loading = true;
        _startup.IsChecked = Host.Settings.StartWithWindows;
        _loading = false;
    }

    public override void Refresh()
    {
        _loading = true;
        var s = Host.Settings;
        _startup.IsChecked = WindowsStartup.IsEnabled();
        _startWindow.IsChecked = s.ShowStartWindow;
        _updates.IsChecked = s.CheckUpdatesOnStart;
        _remember.IsChecked = s.RememberView;
        _confirm.IsChecked = s.ConfirmDeletes;
        _keypad.IsChecked = s.KeypadVisible;
        _faders.IsChecked = s.FaderBarVisible;
        foreach (var chip in _sizes.Children.OfType<RadioButton>())
            chip.IsChecked = (int)chip.Tag == s.UiScalePercent;

        _loading = false;
    }

    private void OpenFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(ShowStorage.SettingsPath)!;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Host.AddLog($"Could not open the folder: {ex.Message}", error: true);
        }
    }
}
