using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>The window of a long job: what it is doing and how far it really is. Closes by itself when the job ends.</summary>
public sealed class ProgressWindow : DarkWindow
{
    private readonly BusyJob _job;
    private readonly TextBlock _status = Ui.Text("", "TextBrush", wrap: false);
    private readonly TextBlock _percent = Ui.Text("0 %", "DimBrush", wrap: false);
    private readonly ColumnDefinition _done = new() { Width = new GridLength(0, GridUnitType.Star) };
    private readonly ColumnDefinition _left = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly Button _cancel;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(60) };
    private bool _finished;
    private bool _screenPhase;

    public ProgressWindow(string title, BusyJob job, bool cancellable)
    {
        _job = job;
        Title = "NX-DMX";
        SizeToContent = SizeToContent.Height;
        Width = 440;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;

        var track = new Grid { Height = 12, Margin = new Thickness(0, 8, 0, 8), Background = Ui.Brush("Surface3Brush") };
        track.ColumnDefinitions.Add(_done);
        track.ColumnDefinitions.Add(_left);
        var fill = new Border { Background = Ui.Brush("AmberBrush") };
        track.Children.Add(fill);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "ProgressWindow");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_percent, "ProgressPercent");

        var line = new DockPanel();
        DockPanel.SetDock(_percent, Dock.Right);
        line.Children.Add(_percent);
        line.Children.Add(_status);

        _cancel = Ui.Button("Cancel", _job.Cancel, width: 100);
        _cancel.HorizontalAlignment = HorizontalAlignment.Right;
        _cancel.Margin = new Thickness(0, 10, 0, 0);
        _cancel.Visibility = cancellable ? Visibility.Visible : Visibility.Collapsed;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(Ui.Caption(title));
        panel.Children.Add(line);
        panel.Children.Add(track);
        panel.Children.Add(_cancel);
        Content = panel;

        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) =>
        {
            Update();
            _timer.Start();
        };
        Closing += (_, e) =>
        {
            // The window belongs to the job; only the job ends it.
            if (!_finished)
                e.Cancel = true;
        };
    }

    protected override bool CloseOnEscape => false;

    private void Update()
    {
        if (_screenPhase)
            return;

        var fraction = Math.Clamp(_job.Fraction, 0, 1);
        _done.Width = new GridLength(fraction, GridUnitType.Star);
        _left.Width = new GridLength(1 - fraction, GridUnitType.Star);
        _percent.Text = $"{(int)(fraction * 100)} %";
        _status.Text = _job.IsCancelRequested ? "Cancelling…" : _job.Status;
        _cancel.IsEnabled = _job.CanCancel && !_job.IsCancelRequested;
    }

    /// <summary>The measured work is done; what is left is putting the result on screen, which cannot be measured.</summary>
    public void ShowScreenPhase()
    {
        _screenPhase = true;
        _timer.Stop();
        _done.Width = new GridLength(1, GridUnitType.Star);
        _left.Width = new GridLength(0, GridUnitType.Star);
        _percent.Text = "";
        _status.Text = "Updating the screen…";
        _cancel.IsEnabled = false;
    }

    /// <summary>The job ended: the bar shows where it got to, then the window closes.</summary>
    public void Finish()
    {
        _finished = true;
        _timer.Stop();
        Close();
    }
}
