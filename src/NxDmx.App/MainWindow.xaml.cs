using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NxDmx.App.Services;
using NxDmx.App.Views;
using Velopack;

namespace NxDmx.App;

public partial class MainWindow : Window
{
    private readonly ConsoleHost _host;
    private readonly Dictionary<string, ConsoleView> _views = new();
    private readonly DispatcherTimer _tick;
    private string _currentView = "";
    private UpdateInfo? _availableUpdate;
    private bool _updateBusy;

    private static readonly (string Key, string Title)[] Views =
    [
        ("Fixtures", "Fixtures"),
        ("Groups", "Groups"),
        ("Cuelists", "Cuelists"),
        ("Patch", "Patch"),
        ("Profiles", "Fixture Manager"),
        ("Universes", "Universes"),
        ("Output", "Output"),
        ("Remote", "Remote Control"),
        ("Show", "Show"),
    ];

    public MainWindow()
    {
        InitializeComponent();

        _host = new ConsoleHost(Dispatcher);
        Info.Attach(_host);
        CommandBar.Attach(_host, _host.Settings.KeypadVisible);
        CommandBar.KeypadToggled += SetKeypadVisible;
        Pad.Attach(_host);
        SetKeypadVisible(_host.Settings.KeypadVisible);

        MasterBar.Committed += percent => _host.Run($"Master At {percent}");
        _host.StateChanged += OnStateChanged;
        _host.ShellChanged += UpdateHeader;

        VersionButton.Content = "v" + _host.Updates.CurrentVersion;

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _tick.Tick += (_, _) => _views.GetValueOrDefault(_currentView)?.Tick();
        _tick.Start();

        _host.AddLog("NX-DMX ready. Click fixtures and values, or type commands: Fixture 1 Thru 4 At 50 Please. Help lists everything.");
        if (_host.StartupNote is not null)
            _host.AddLog(_host.StartupNote);
        if (_host.OutputSetupError is not null)
            _host.AddLog(_host.OutputSetupError, error: true);

        UpdateHeader();
        OnStateChanged();
    }

    // ---- window ----------------------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Black title bar on Windows 10/11, so the window has no light strip on top of the dark interface.</summary>
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        var black = 0x000000;
        var text = 0xDADADA;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 35, ref black, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var start = Views.Any(v => v.Key == _host.Settings.LastView) ? _host.Settings.LastView : "Fixtures";
        Select(start);
        CommandBar.FocusLine();
        await CheckForUpdatesAsync(manual: false);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ShowDialogs.ConfirmDiscard(_host, this))
        {
            e.Cancel = true;
            return;
        }

        _tick.Stop();
        _host.Settings.LastView = _currentView;
        _host.Dispose();
    }

    // ---- focus: typing always reaches the command line ---------------------------------------------------------

    /// <summary>After clicking a button the command line takes the focus back, unless the click was on an input.</summary>
    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && !IsInput(source))
            Dispatcher.BeginInvoke(() => CommandBar.FocusLine(), DispatcherPriority.Input);
    }

    /// <summary>Letters typed while no input has the focus go to the command line, as on a console.</summary>
    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox or ComboBoxItem)
            return;

        CommandBar.TypeText(e.Text);
        e.Handled = true;
    }

    private static bool IsInput(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or ComboBox or ComboBoxItem or ScrollBar or Thumb or GridSplitter or DataGridCell)
                return true;

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.S:
                if (shift) ShowDialogs.SaveAs(_host, this); else ShowDialogs.Save(_host, this);
                break;
            case Key.O:
                ShowDialogs.Open(_host, this);
                break;
            case Key.N:
                ShowDialogs.New(_host, this);
                break;
            case >= Key.D1 and <= Key.D9:
                Select(Views[e.Key - Key.D1].Key);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ---- views -----------------------------------------------------------------------------------------------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Uid: { Length: > 0 } key } && IsLoaded)
            Display(key);
    }

    private void Select(string key)
    {
        var button = new[] { NavFixtures, NavGroups, NavCuelists, NavPatch, NavProfiles, NavUniverses, NavOutput, NavRemote, NavShow }
            .FirstOrDefault(b => b.Uid == key);
        if (button is null)
            return;

        if (button.IsChecked == true)
            Display(key);
        else
            button.IsChecked = true;
    }

    private void Display(string key)
    {
        if (key == _currentView)
            return;

        if (!_views.TryGetValue(key, out var view))
        {
            view = Create(key);
            _views[key] = view;
        }

        _currentView = key;
        ViewTitle.Text = Views.First(v => v.Key == key).Title;
        ViewHost.Content = view;
        view.Activated();
    }

    private ConsoleView Create(string key) => key switch
    {
        "Fixtures" => new FixturesView(_host),
        "Groups" => new GroupsView(_host),
        "Cuelists" => new CuelistsView(_host),
        "Patch" => new PatchView(_host),
        "Profiles" => new ProfilesView(_host),
        "Universes" => new UniversesView(_host),
        "Output" => new OutputView(_host),
        "Remote" => new RemoteView(_host),
        "Show" => new ShowView(_host),
        _ => throw new ArgumentException("Unknown view " + key),
    };

    private void OnStateChanged()
    {
        _views.GetValueOrDefault(_currentView)?.Refresh();
        MasterBar.Value = _host.Snapshot.Master;
        BlackoutButton.IsChecked = _host.Snapshot.Blackout;
    }

    private void SetKeypadVisible(bool visible)
    {
        Pad.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _host.Settings.KeypadVisible = visible;
    }

    private void Blackout_Click(object sender, RoutedEventArgs e) => _host.Run("Blackout");

    /// <summary>Double-click on the splitter puts the bottom area back to its normal height.</summary>
    private void Splitter_DoubleClick(object sender, MouseButtonEventArgs e) => BottomRow.Height = new GridLength(214);

    // ---- header ----------------------------------------------------------------------------------------------

    private void UpdateHeader()
    {
        ShowName.Text = _host.ShowTitle;
        ShowDirty.Visibility = _host.IsDirty ? Visibility.Visible : Visibility.Collapsed;
        Title = $"NX-DMX {_host.Updates.CurrentVersion} · {_host.ShowTitle}{(_host.IsDirty ? " *" : "")}";

        StatusText.Text = _host.OutputStatus;
        StatusLed.Background = (Brush)FindResource(_host.Health switch
        {
            OutputHealth.Sending => "GreenBrush",
            OutputHealth.Error => "RedBrush",
            _ => "MutedBrush",
        });
        StatusText.Foreground = (Brush)FindResource(_host.Health == OutputHealth.Error ? "RedBrush" : "DimBrush");
    }

    // ---- updates ---------------------------------------------------------------------------------------------

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    /// <summary>
    /// Looks for a newer release. It only ever shows a button and never restarts by itself: the console may be running
    /// a show. The automatic check at startup stays quiet when it finds nothing or when there is no network.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy || _availableUpdate is not null)
        {
            if (manual)
            {
                _host.AddLog(_availableUpdate is null
                    ? "Already checking for updates."
                    : $"NX-DMX {_availableUpdate.TargetFullRelease.Version} is available. Press the green Update button on the left.");
            }

            return;
        }

        if (!_host.Updates.IsInstalled)
        {
            if (manual)
                _host.AddLog("Updates only work in an installed copy. Install NX-DMX with the Setup.exe from the GitHub release page.");
            return;
        }

        _updateBusy = true;
        try
        {
            var update = await _host.Updates.CheckAsync();
            if (update is null)
            {
                if (manual)
                    _host.AddLog($"NX-DMX {_host.Updates.CurrentVersion} is up to date.");
                return;
            }

            _availableUpdate = update;
            UpdateButton.Content = $"Update\n{update.TargetFullRelease.Version}";
            UpdateButton.Visibility = Visibility.Visible;
            _host.AddLog($"NX-DMX {update.TargetFullRelease.Version} is available. Press the green Update button on the left to install it.");
        }
        catch (Exception ex)
        {
            if (manual)
                _host.AddLog($"Update check failed: {ex.Message}", error: true);
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not { } update || _updateBusy)
            return;

        var version = update.TargetFullRelease.Version;
        _updateBusy = true;
        UpdateButton.IsEnabled = false;
        try
        {
            UpdateButton.Content = "0 %";
            await _host.Updates.DownloadAsync(update, percent =>
                Dispatcher.BeginInvoke(() => UpdateButton.Content = $"{percent} %"));

            var restartNow = MessageBox.Show(this,
                $"NX-DMX {version} has been downloaded.\n\n" +
                "Restart now? The DMX output stops while the update installs. Your show comes back from the autosave; " +
                "the programmer and the cuelist positions start empty.\n\n" +
                "Choose No to install it when you close NX-DMX instead.",
                "NX-DMX update", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

            if (restartNow)
            {
                _host.Autosave();
                _host.Updates.ApplyAndRestart(update);
                return;
            }

            _host.Updates.ApplyOnExit(update);
            UpdateButton.Content = "Installs\non exit";
            _host.AddLog($"NX-DMX {version} will be installed the next time you close the program.");
        }
        catch (Exception ex)
        {
            _host.AddLog($"Update failed: {ex.Message}", error: true);
            UpdateButton.Content = $"Update\n{version}";
            UpdateButton.IsEnabled = true;
        }
        finally
        {
            _updateBusy = false;
        }
    }
}
