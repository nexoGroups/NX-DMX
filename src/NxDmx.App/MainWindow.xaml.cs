using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NxDmx.Core;
using NxDmx.Core.ArtNet;
using NxDmx.Core.Engine;
using NxDmx.Core.Storage;
using Microsoft.Win32;
using Velopack;

namespace NxDmx.App;

public partial class MainWindow : Window
{
    private const int MaxLogEntries = 500;

    private readonly LightingConsole _console = new();
    private readonly DmxEngine _engine;
    private readonly ObservableCollection<LogEntry> _log = [];
    private readonly List<string> _history = [];
    private readonly DispatcherTimer _statusTimer;
    private readonly UpdateService _updates = new();

    private string? _showPath;
    private string _savedJson = "";
    private string _autosavedJson = "";
    private string? _autosavedPath;
    private UpdateInfo? _availableUpdate;
    private bool _updateBusy;
    private ArtNetOutput? _artNet;
    private string? _setupError;
    private int _historyIndex;
    private int _cuelistNumber = 1;
    private int _monitorUniverse = 1;
    private long _lastFrameCount;
    private DateTime _lastFrameTime = DateTime.UtcNow;
    private double _frameRate;

    public MainWindow()
    {
        InitializeComponent();

        _engine = new DmxEngine(_console);
        LogList.ItemsSource = _log;

        var startupMessage = LoadStartupShow();
        _console.Changed += OnConsoleChanged;

        ApplyArtNet();
        _engine.Start();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        VersionButton.Content = "v" + _updates.CurrentVersion;
        Title = "NX-DMX " + _updates.CurrentVersion;

        RefreshViews();
        UpdateShowState();
        AddLog("NX-DMX ready. Select with Fixture 1 Thru 4, set with At 50, finish with Please. Help lists everything.");
        if (startupMessage is not null)
            AddLog(startupMessage);
    }

    /// <summary>
    /// Brings back the autosaved show of the last session. On the very first start there is none, so a small demo rig
    /// is patched, through the normal commands, so there is something to drive. Returns a note for the log.
    /// </summary>
    private string? LoadStartupShow()
    {
        string? note = null;
        if (ShowStorage.ReadAutosave() is { } autosave)
        {
            try
            {
                _console.ImportShow(autosave);
                _showPath = ShowStorage.ReadSessionPath();
                _autosavedJson = _console.ExportShow();
                _savedJson = BaselineOf(_showPath);
                return "Restored your show from the last session.";
            }
            catch (ShowFileException ex)
            {
                note = $"The autosaved show could not be restored ({ex.Message}) and was ignored.";
            }
        }

        _console.Execute("Patch Fixture 1 Thru 8 Type RGBDim At 1.1");
        _console.Execute("Patch Fixture 9 Thru 12 Type Mover At 1.33");
        _console.Execute("Patch Fixture 21 Thru 24 Type Dimmer At 1.65");
        _console.ClearHistory();
        _savedJson = _console.ExportShow();
        return note;
    }

    /// <summary>The text a show file would have after loading, for telling unsaved changes from saved ones.</summary>
    private static string BaselineOf(string? path)
    {
        if (path is null || !File.Exists(path))
            return "";

        try
        {
            return ShowFile.Serialize(ShowFile.Deserialize(ShowStorage.Read(path)));
        }
        catch (Exception ex) when (ex is ShowFileException or IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private void OnConsoleChanged()
    {
        RefreshViews();
        UpdateShowState();
    }

    // ---- command line ----------------------------------------------------------------------------------------

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CommandLine.Focus();
        await CheckForUpdatesAsync(manual: false);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        _statusTimer.Stop();
        _engine.Dispose();
        _artNet?.Dispose();
    }

    private void CommandLine_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Submit();
                e.Handled = true;
                break;
            case Key.Escape:
                CommandLine.Clear();
                _historyIndex = _history.Count;
                e.Handled = true;
                break;
            case Key.Up when _historyIndex > 0:
                ShowHistory(--_historyIndex);
                e.Handled = true;
                break;
            case Key.Down when _historyIndex < _history.Count:
                if (++_historyIndex < _history.Count)
                    ShowHistory(_historyIndex);
                else
                    CommandLine.Clear();
                e.Handled = true;
                break;
        }
    }

    private void ShowHistory(int index)
    {
        CommandLine.Text = _history[index];
        CommandLine.CaretIndex = CommandLine.Text.Length;
    }

    private void Submit()
    {
        var line = CommandLine.Text.Trim();
        CommandLine.Clear();
        if (line.Length == 0)
            return;

        if (_history.Count == 0 || _history[^1] != line)
            _history.Add(line);
        _historyIndex = _history.Count;

        Run(line);
    }

    private void Run(string line)
    {
        AddLog("> " + line, echo: true);
        var result = _console.Execute(line);
        if (result.Message.Length > 0)
            AddLog(result.Message, error: !result.Success);
    }

    private void AddLog(string text, bool echo = false, bool error = false)
    {
        _log.Add(new LogEntry(text, echo, error));
        while (_log.Count > MaxLogEntries)
            _log.RemoveAt(0);

        Dispatcher.BeginInvoke(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    // ---- keypad ----------------------------------------------------------------------------------------------

    /// <summary>Types the key's text into the command line, adding a space where words would run together.</summary>
    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        var text = (string)((FrameworkElement)sender).Tag;
        var current = CommandLine.Text;
        var needsSpace = current.Length > 0
            && !char.IsWhiteSpace(current[^1])
            && !char.IsAsciiDigit(text[0])
            && text[0] != '.';

        CommandLine.Text = current + (needsSpace ? " " : "") + text;
        CommandLine.CaretIndex = CommandLine.Text.Length;
        CommandLine.Focus();
    }

    /// <summary>Keys that act at once, like Oops: they do not go through the command line text.</summary>
    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        Run((string)((FrameworkElement)sender).Tag);
        CommandLine.Focus();
    }

    /// <summary>Like on grandMA: the first press empties the command line, the next ones step back through Clear.</summary>
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (CommandLine.Text.Length > 0)
            CommandLine.Clear();
        else
            Run("Clear");

        CommandLine.Focus();
    }

    private void Backspace_Click(object sender, RoutedEventArgs e)
    {
        if (CommandLine.Text.Length > 0)
            CommandLine.Text = CommandLine.Text[..^1];

        CommandLine.CaretIndex = CommandLine.Text.Length;
        CommandLine.Focus();
    }

    private void Please_Click(object sender, RoutedEventArgs e)
    {
        Submit();
        CommandLine.Focus();
    }

    // ---- cuelist panel ---------------------------------------------------------------------------------------

    private void CuelistGo_Click(object sender, RoutedEventArgs e) => Run($"Go Cuelist {_cuelistNumber}");

    private void CuelistBack_Click(object sender, RoutedEventArgs e) => Run($"Back Cuelist {_cuelistNumber}");

    private void CuelistOff_Click(object sender, RoutedEventArgs e) => Run($"Off Cuelist {_cuelistNumber}");

    private void PreviousCuelist_Click(object sender, RoutedEventArgs e) => StepCuelist(-1);

    private void NextCuelist_Click(object sender, RoutedEventArgs e) => StepCuelist(1);

    private void StepCuelist(int direction)
    {
        var numbers = _console.Read((show, _) => show.Cuelists.Keys.ToList());
        if (numbers.Count == 0)
            return;

        var index = numbers.IndexOf(_cuelistNumber);
        _cuelistNumber = numbers[index < 0 ? 0 : Math.Clamp(index + direction, 0, numbers.Count - 1)];
        RefreshViews();
    }

    // ---- DMX monitor -----------------------------------------------------------------------------------------

    private void PreviousUniverse_Click(object sender, RoutedEventArgs e) =>
        _monitorUniverse = Math.Max(1, _monitorUniverse - 1);

    private void NextUniverse_Click(object sender, RoutedEventArgs e) =>
        _monitorUniverse = Math.Min(ArtNetPacket.MaxUniverse, _monitorUniverse + 1);

    private static string FormatUniverse(byte[]? data)
    {
        if (data is null)
            return "No fixtures are patched in this universe.";

        var text = new StringBuilder();
        for (var row = 0; row < 32; row++)
        {
            text.Append($"{row * 16 + 1:000}: ");
            for (var column = 0; column < 16; column++)
            {
                var value = data[row * 16 + column];
                text.Append(value == 0 ? "  ." : value.ToString().PadLeft(3)).Append(' ');
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    // ---- Art-Net settings ------------------------------------------------------------------------------------

    private void ApplyArtNet_Click(object sender, RoutedEventArgs e)
    {
        ApplyArtNet();
        CommandLine.Focus();
    }

    private void Settings_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ApplyArtNet();
        CommandLine.Focus();
        e.Handled = true;
    }

    private void OutputEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            _engine.Output = OutputEnabled.IsChecked == true ? _artNet : null;
    }

    private void ApplyArtNet()
    {
        try
        {
            var target = ParseAddress(TargetIp.Text, "Art-Net target");
            var local = LocalIp.Text.Trim().Length > 0 ? ParseAddress(LocalIp.Text, "Local IP") : null;

            var output = new ArtNetOutput(target, localAddress: local);
            var previous = _artNet;
            _artNet = output;
            _engine.Output = OutputEnabled.IsChecked == true ? output : null;
            previous?.Dispose();
            _setupError = null;
        }
        catch (Exception ex) when (ex is FormatException or SocketException)
        {
            _setupError = ex.Message;
        }
    }

    private static IPAddress ParseAddress(string text, string what)
    {
        if (!IPAddress.TryParse(text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new FormatException($"{what}: '{text.Trim()}' is not an IPv4 address");

        return address;
    }

    // ---- show files ------------------------------------------------------------------------------------------

    private const string DialogFilter = "NX-DMX show (*.nxshow)|*.nxshow|All files (*.*)|*.*";

    private bool HasUnsavedChanges => _console.ExportShow() != _savedJson;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.S:
                if (shift) SaveShowAs(); else SaveShow();
                break;
            case Key.O:
                OpenShow();
                break;
            case Key.N:
                NewShow();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void NewShow_Click(object sender, RoutedEventArgs e) => NewShow();

    private void OpenShow_Click(object sender, RoutedEventArgs e) => OpenShow();

    private void SaveShow_Click(object sender, RoutedEventArgs e) => SaveShow();

    private void SaveShowAs_Click(object sender, RoutedEventArgs e) => SaveShowAs();

    private void NewShow()
    {
        if (!ConfirmDiscard())
            return;

        _console.ImportShow(ShowFile.Serialize(new Show()));
        _showPath = null;
        _savedJson = _console.ExportShow();
        AfterShowReplaced();
        AddLog("New empty show. Patch fixtures with: Patch Fixture 1 Thru 8 Type RGBDim At 1.1");
    }

    private void OpenShow()
    {
        if (!ConfirmDiscard())
            return;

        var dialog = new OpenFileDialog
        {
            Filter = DialogFilter,
            InitialDirectory = EnsureShowsFolder(),
            Title = "Open show",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _console.ImportShow(ShowStorage.Read(dialog.FileName));
        }
        catch (Exception ex) when (ex is ShowFileException or IOException or UnauthorizedAccessException)
        {
            AddLog($"Could not open {Path.GetFileName(dialog.FileName)}: {ex.Message}", error: true);
            MessageBox.Show(this, ex.Message, "Open show", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _showPath = dialog.FileName;
        _savedJson = _console.ExportShow();
        AfterShowReplaced();
        AddLog($"Opened {Path.GetFileName(dialog.FileName)}.");
    }

    /// <summary>Saves under the current name, or asks for one. Returns false when the operator cancelled or it failed.</summary>
    private bool SaveShow() => _showPath is null ? SaveShowAs() : WriteShow(_showPath);

    private bool SaveShowAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = DialogFilter,
            DefaultExt = ShowFile.Extension,
            AddExtension = true,
            InitialDirectory = _showPath is null ? EnsureShowsFolder() : Path.GetDirectoryName(_showPath),
            FileName = _showPath is null ? "My show" : Path.GetFileName(_showPath),
            Title = "Save show",
        };

        return dialog.ShowDialog(this) == true && WriteShow(dialog.FileName);
    }

    private bool WriteShow(string path)
    {
        var json = _console.ExportShow();
        try
        {
            ShowStorage.Write(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Could not save {Path.GetFileName(path)}: {ex.Message}", error: true);
            MessageBox.Show(this, ex.Message, "Save show", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _showPath = path;
        _savedJson = json;
        UpdateShowState();
        AddLog($"Saved {Path.GetFileName(path)}.");
        return true;
    }

    /// <summary>Asks what to do with unsaved changes. Returns false when the operator cancelled.</summary>
    private bool ConfirmDiscard()
    {
        if (!HasUnsavedChanges)
            return true;

        var answer = MessageBox.Show(this,
            $"Save the changes to \"{ShowTitle()}\" first?",
            "NX-DMX", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        return answer switch
        {
            MessageBoxResult.Yes => SaveShow(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private static string EnsureShowsFolder()
    {
        try
        {
            Directory.CreateDirectory(ShowStorage.ShowsFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The dialog then simply opens in its default folder.
        }

        return ShowStorage.ShowsFolder;
    }

    private void AfterShowReplaced()
    {
        _cuelistNumber = _console.Read((show, _) => show.Cuelists.Keys.FirstOrDefault(1));
        RefreshViews();
        UpdateShowState();
        CommandLine.Focus();
    }

    private string ShowTitle() => _showPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(_showPath);

    /// <summary>Updates the show name and the unsaved marker, and keeps the autosave current.</summary>
    private void UpdateShowState()
    {
        var json = _console.ExportShow();
        var dirty = json != _savedJson;

        ShowName.Text = ShowTitle();
        ShowDirty.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        Title = $"NX-DMX {_updates.CurrentVersion} · {ShowTitle()}{(dirty ? " *" : "")}";

        if (json != _autosavedJson || _showPath != _autosavedPath)
            Autosave(json);
    }

    private void Autosave(string json)
    {
        try
        {
            ShowStorage.WriteAutosave(json, _showPath);
            _autosavedJson = json;
            _autosavedPath = _showPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting the operator; the next change tries again.
        }
    }

    // ---- updates ---------------------------------------------------------------------------------------------

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(manual: true);
        CommandLine.Focus();
    }

    /// <summary>
    /// Looks for a newer release. It only ever shows a button and never restarts by itself: the console may be running
    /// a show. The automatic check at startup stays quiet when it finds nothing or when there is no network.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy || _availableUpdate is not null)
        {
            if (manual)
                AddLog(_availableUpdate is null
                    ? "Already checking for updates."
                    : $"NX-DMX {_availableUpdate.TargetFullRelease.Version} is available. Press the green Update button in the top bar.");
            return;
        }

        if (!_updates.IsInstalled)
        {
            if (manual)
                AddLog("Updates only work in an installed copy. Install NX-DMX with the Setup.exe from the GitHub release page.");
            return;
        }

        _updateBusy = true;
        try
        {
            var update = await _updates.CheckAsync();
            if (update is null)
            {
                if (manual)
                    AddLog($"NX-DMX {_updates.CurrentVersion} is up to date.");
                return;
            }

            _availableUpdate = update;
            UpdateButton.Content = $"Update to {update.TargetFullRelease.Version}";
            UpdateButton.Visibility = Visibility.Visible;
            AddLog($"NX-DMX {update.TargetFullRelease.Version} is available. Press the green Update button in the top bar to install it.");
        }
        catch (Exception ex)
        {
            if (manual)
                AddLog($"Update check failed: {ex.Message}", error: true);
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
            UpdateButton.Content = "Downloading 0 %";
            await _updates.DownloadAsync(update, percent =>
                Dispatcher.BeginInvoke(() => UpdateButton.Content = $"Downloading {percent} %"));

            var restartNow = MessageBox.Show(this,
                $"NX-DMX {version} has been downloaded.\n\n" +
                "Restart now? The DMX output stops while the update installs. Your show comes back from the autosave; " +
                "the programmer and the cuelist positions start empty.\n\n" +
                "Choose No to install it when you close NX-DMX instead.",
                "NX-DMX update", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

            if (restartNow)
            {
                Autosave(_console.ExportShow());
                _updates.ApplyAndRestart(update);
                return;
            }

            _updates.ApplyOnExit(update);
            UpdateButton.Content = "Installs on exit";
            AddLog($"NX-DMX {version} will be installed the next time you close the program.");
        }
        catch (Exception ex)
        {
            AddLog($"Update failed: {ex.Message}", error: true);
            UpdateButton.Content = $"Update to {version}";
            UpdateButton.IsEnabled = true;
        }
        finally
        {
            _updateBusy = false;
        }
    }

    // ---- refreshing the panels -------------------------------------------------------------------------------

    private void RefreshViews()
    {
        var snapshot = _console.Read((show, programmer) => ConsoleSnapshot.Build(show, programmer, _cuelistNumber));

        FixtureGrid.ItemsSource = snapshot.Fixtures;
        CueGrid.ItemsSource = snapshot.Cues;
        GroupGrid.ItemsSource = snapshot.Groups;
        CuelistTitle.Text = snapshot.CuelistTitle;
        SelectionText.Text = snapshot.Selection.Length == 0
            ? ""
            : $"SEL {snapshot.Selection}   PRG {snapshot.ProgrammerValueCount}";
    }

    private void UpdateStatus()
    {
        var now = DateTime.UtcNow;
        var frames = _engine.FramesSent;
        var elapsed = (now - _lastFrameTime).TotalSeconds;
        if (elapsed >= 1)
        {
            _frameRate = (frames - _lastFrameCount) / elapsed;
            _lastFrameCount = frames;
            _lastFrameTime = now;
        }

        var frame = _engine.LastFrame;
        var universes = frame is null ? "" : string.Join(", ", frame.Universes.Keys);

        if (_setupError is not null)
            SetStatus($"Setup error: {_setupError}", "BadBrush");
        else if (_engine.Output is null)
            SetStatus("Output off", "MutedBrush");
        else if (_engine.LastError is { } error)
            SetStatus($"Send error: {error}", "BadBrush");
        else
            SetStatus($"{_frameRate:0} Hz · universes {(universes.Length == 0 ? "none" : universes)}", "GoodBrush");

        MonitorTitle.Text = $"DMX OUTPUT · UNIVERSE {_monitorUniverse}";
        byte[]? data = null;
        frame?.Universes.TryGetValue(_monitorUniverse, out data);
        MonitorText.Text = FormatUniverse(data);
    }

    private void SetStatus(string text, string brushKey)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(brushKey);
    }
}
