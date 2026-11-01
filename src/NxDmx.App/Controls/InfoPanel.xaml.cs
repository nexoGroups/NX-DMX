using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using NxDmx.App.Services;
using NxDmx.Core.Commands;
using NxDmx.Core.Fixtures;

namespace NxDmx.App.Controls;

/// <summary>The panel above the command line: the programmer table, the command log and the help.</summary>
public partial class InfoPanel : UserControl
{
    private ConsoleHost? _host;
    private List<FixtureAttribute> _columns = [];

    public InfoPanel()
    {
        InitializeComponent();
        HelpText.Text = CommandInterpreter.HelpText;
    }

    public void Attach(ConsoleHost host)
    {
        _host = host;
        LogList.ItemsSource = host.Log;
        host.Log.CollectionChanged += OnLogChanged;
        host.StateChanged += Refresh;
        host.FeedbackChanged += ShowFeedback;
        Refresh();
    }

    /// <summary>The answer to the last command, so that a click on a value is never silent: green-grey when it worked, red when not.</summary>
    private void ShowFeedback()
    {
        if (_host is null)
            return;

        var (text, isError) = _host.Feedback;
        Hint.Text = text;
        Hint.ToolTip = text;
        Hint.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "RedBrush" : "DimBrush");
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Show the newest line without stealing the view when the operator reads on the log tab.
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.BeginInvoke(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && ProgrammerGrid is null)
            return;

        var programmer = ProgrammerTab.IsChecked == true;
        LogScroll.Visibility = LogTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HelpScroll.Visibility = HelpTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
        if (LogTab.IsChecked == true)
            LogScroll.ScrollToEnd();
    }

    /// <summary>Shows the log tab, e.g. when a command failed and the message should be read.</summary>
    public void ShowLog() => LogTab.IsChecked = true;

    private void Refresh()
    {
        if (_host is null || ProgrammerGrid is null)
            return;

        var snapshot = _host.Snapshot;
        ShowFeedback();

        if (snapshot.ProgrammerColumns.SequenceEqual(_columns) is false)
        {
            _columns = [.. snapshot.ProgrammerColumns];
            RebuildColumns();
        }

        ProgrammerGrid.ItemsSource = snapshot.ProgrammerRows;
        var onProgrammerTab = ProgrammerTab.IsChecked == true;
        ProgrammerGrid.Visibility = onProgrammerTab && snapshot.ProgrammerRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProgrammerEmpty.Visibility = onProgrammerTab && snapshot.ProgrammerRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildColumns()
    {
        while (ProgrammerGrid.Columns.Count > 2)
            ProgrammerGrid.Columns.RemoveAt(2);

        for (var i = 0; i < _columns.Count; i++)
        {
            var style = new Style(typeof(TextBlock), (Style)FindResource(typeof(TextBlock)));
            style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas")));
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            var trigger = new DataTrigger { Binding = new Binding($"Cells[{i}].Programmed"), Value = true };
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("RedBrush")));
            style.Triggers.Add(trigger);

            ProgrammerGrid.Columns.Add(new DataGridTextColumn
            {
                Header = _columns[i].ShortName().ToUpperInvariant(),
                Binding = new Binding($"Cells[{i}].Text"),
                ElementStyle = style,
                MinWidth = 54,
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
            });
        }
    }
}
