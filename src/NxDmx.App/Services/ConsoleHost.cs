using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;
using NxDmx.Core;
using NxDmx.Core.ArtNet;
using NxDmx.Core.Commands;
using NxDmx.Core.Engine;
using NxDmx.Core.Remote;
using NxDmx.Core.Storage;

namespace NxDmx.App.Services;

public sealed record LogEntry(DateTime Time, string Text, bool IsEcho, bool IsError, string Source)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>A network adapter the operator can send Art-Net from.</summary>
public sealed record NetworkChoice(string Address, string Text, string Broadcast = "")
{
    public override string ToString() => Text;
}

/// <summary>
/// Everything the views share: the console and its output, the command line, the log, the show file, the remote
/// interfaces and the settings. The views only talk to this class, which keeps them free of console plumbing.
/// </summary>
public sealed class ConsoleHost : IDisposable
{
    private const int MaxLogEntries = 500;

    /// <summary>Verbs that wait for a target: after one of them, clicking an object completes the command.</summary>
    private static readonly HashSet<string> PendingVerbs = ["record", "update", "delete", "label", "copy"];

    private readonly Dispatcher _dispatcher;
    private readonly List<string> _history = [];
    private readonly DispatcherTimer _statusTimer;
    private int _historyIndex;
    private int _changePending;
    private string _commandLine = "";
    private string _savedJson = "";
    private string _autosavedJson = "";
    private string? _autosavedPath;
    private ArtNetOutput? _artNet;
    private long _lastFrameCount;
    private DateTime _lastFrameTime = DateTime.UtcNow;

    public ConsoleHost(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Settings = AppSettings.Load();
        Console = new LightingConsole();
        Engine = new DmxEngine(Console);
        Updates = new UpdateService();
        Remote = new RemoteServer(new RemoteApi(Console));
        Remote.Api.Activity += OnRemoteActivity;

        StartupNote = LoadStartupShow();
        Snapshot = ReadSnapshot();
        Console.Changed += OnConsoleChanged;

        ApplyOutput();
        Engine.Start();
        ApplyRemote();
        MarkSaved();

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _statusTimer.Tick += (_, _) => UpdateRate();
        _statusTimer.Start();
    }

    public LightingConsole Console { get; }

    public DmxEngine Engine { get; }

    public RemoteServer Remote { get; }

    public UpdateService Updates { get; }

    public AppSettings Settings { get; }

    public ObservableCollection<LogEntry> Log { get; } = [];

    public ObservableCollection<RemoteActivity> RemoteLog { get; } = [];

    /// <summary>Current copy of the console state. Replaced after every change, always on the UI thread.</summary>
    public ConsoleSnapshot Snapshot { get; private set; }

    /// <summary>A message about the start (restored or failed autosave) for the log.</summary>
    public string? StartupNote { get; }

    /// <summary>The show on screen at the start is the one of the last session, brought back from the automatic backup.</summary>
    public bool RestoredSession { get; private set; }

    public string? ShowPath { get; private set; }

    public bool IsDirty { get; private set; }

    public string ShowTitle => ShowPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(ShowPath);

    public string? OutputSetupError { get; private set; }

    public double FrameRate { get; private set; }

    public IReadOnlyList<RemoteStatus> RemoteStatus => Remote.Status;

    /// <summary>The console state changed (a command ran, a show was loaded). Raised on the UI thread, merged when frequent.</summary>
    public event Action? StateChanged;

    public event Action? CommandLineChanged;

    /// <summary>Show name, saved marker, output or remote status changed.</summary>
    public event Action? ShellChanged;

    // ---- console changes -------------------------------------------------------------------------------------

    private int _refreshPaused;
    private int _missedChange;

    /// <summary>
    /// Holds back the refresh of the views while a long job (opening a show) changes the console on another thread, so the
    /// interface stays free for the progress window. The caller refreshes when it is done; disposing catches up on what was missed.
    /// </summary>
    public IDisposable PauseRefresh()
    {
        Interlocked.Increment(ref _refreshPaused);
        return new RefreshPause(this);
    }

    private sealed class RefreshPause(ConsoleHost host) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            if (Interlocked.Decrement(ref host._refreshPaused) == 0 && Interlocked.Exchange(ref host._missedChange, 0) == 1)
                host._dispatcher.BeginInvoke(host.RefreshNow);
        }
    }

    private void OnConsoleChanged()
    {
        if (Volatile.Read(ref _refreshPaused) > 0)
        {
            Volatile.Write(ref _missedChange, 1);
            return;
        }

        // Commands can come from the remote servers' threads; the views must be refreshed on the UI thread, once.
        if (Interlocked.Exchange(ref _changePending, 1) == 0)
        {
            _dispatcher.BeginInvoke(() =>
            {
                // A synchronous refresh in between (see Run) has already taken care of this change.
                if (Interlocked.Exchange(ref _changePending, 0) == 1)
                    Refresh();
            }, DispatcherPriority.Normal);
        }
    }

    /// <summary>Re-reads the console state and tells the views. Called by the views, and after every command from the UI.</summary>
    public void RefreshNow()
    {
        Interlocked.Exchange(ref _changePending, 0);
        Refresh();
    }

    private void Refresh()
    {
        Snapshot = ReadSnapshot();
        UpdateShowState();
        StateChanged?.Invoke();
    }

    private ConsoleSnapshot ReadSnapshot() => Console.Read(ConsoleSnapshot.Build);

    private void OnRemoteActivity(RemoteActivity activity)
    {
        _dispatcher.BeginInvoke(() =>
        {
            RemoteLog.Add(activity);
            while (RemoteLog.Count > 200)
                RemoteLog.RemoveAt(0);

            AddLog($"{activity.Source}: {activity.Line}", echo: true, source: activity.Source);
            if (activity.Message.Length > 0)
                AddLog(activity.Message, error: !activity.Success, source: activity.Source);
        });
    }

    // ---- command line ----------------------------------------------------------------------------------------

    public string CommandLine
    {
        get => _commandLine;
        set
        {
            if (_commandLine == value)
                return;

            _commandLine = value;
            CommandLineChanged?.Invoke();
        }
    }

    /// <summary>Types text into the command line, adding a space where words would run together (the keypad does this).</summary>
    public void Insert(string text)
    {
        var current = CommandLine;
        var needsSpace = current.Length > 0
            && !char.IsWhiteSpace(current[^1])
            && !char.IsAsciiDigit(text[0])
            && text[0] != '.'
            && text[0] != ',';

        CommandLine = current + (needsSpace ? " " : "") + text;
    }

    /// <summary>
    /// A click on an object or value in a view (a fixture tile, a colour, a cue) puts its command into the command
    /// line, like pressing the keys. A click on its own runs at once. After Record, Update or Delete it completes
    /// that command and runs it. In the middle of something being typed it only appends, and Please finishes it.
    /// </summary>
    public void Click(string command)
    {
        var line = CommandLine.Trim();
        if (line.Length == 0)
        {
            Run(command);
            return;
        }

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verbOnly = words.Length == 1 && PendingVerbs.Contains(words[0].ToLowerInvariant());

        // A view that already knows the verb (the "+ New group" tile says "Record Group 3") replaces the pending one.
        if (verbOnly && command.StartsWith(words[0] + " ", StringComparison.OrdinalIgnoreCase))
        {
            CommandLine = "";
            Run(command);
            return;
        }

        CommandLine = line + " " + command;

        if (verbOnly && !words[0].Equals("label", StringComparison.OrdinalIgnoreCase))
            Submit();
    }

    /// <summary>True while the command line holds just Record, Update, Delete or Label, waiting for the object to be clicked.</summary>
    public bool IsVerbPending
    {
        get
        {
            var words = CommandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 1 && PendingVerbs.Contains(words[0].ToLowerInvariant());
        }
    }

    public void Submit()
    {
        var line = CommandLine.Trim();
        CommandLine = "";
        if (line.Length == 0)
            return;

        if (_history.Count == 0 || _history[^1] != line)
            _history.Add(line);
        _historyIndex = _history.Count;

        Run(line);
    }

    /// <summary>Runs commands (separated by <c>;</c>) and writes them and their answers to the log.</summary>
    public CommandResult Run(string line)
    {
        var parts = CommandText.Split(line);

        // A series of commands is tried on a copy first: either all of it works or none of it is applied.
        if (Console.Preflight(parts) is { Success: false } refused)
        {
            AddLog("> " + string.Join("; ", parts), echo: true);
            AddLog(refused.Message, error: true);
            RefreshNow();
            Feedback = (refused.Message, true);
            FeedbackChanged?.Invoke();
            return refused;
        }

        var last = CommandResult.Ok();
        foreach (var part in parts)
        {
            AddLog("> " + part, echo: true);
            last = Console.Execute(part);
            if (last.Message.Length > 0)
                AddLog(last.Message, error: !last.Success);
            if (!last.Success)
                break;
        }

        // The views must see the result before the next click, so refresh now instead of waiting for the queued update.
        RefreshNow();

        // Only the first line of a longer answer (Help) goes next to the command line.
        Feedback = (last.Message.Split('\n')[0], !last.Success);
        FeedbackChanged?.Invoke();
        return last;
    }

    /// <summary>
    /// Runs a live command without logging it or refreshing at once: for faders that are being dragged, where a command
    /// every few milliseconds would flood the log. The views still follow through the queued refresh.
    /// </summary>
    public void RunQuiet(string line) => Console.Execute(line);

    /// <summary>A setting changed (from the settings page or a button elsewhere): the window re-applies size, keypad and fader bar.</summary>
    public event Action? SettingsChanged;

    /// <summary>Stores the settings and tells the window to apply them.</summary>
    public void SaveSettings()
    {
        Settings.Save();
        SettingsChanged?.Invoke();
    }

    // ---- faders and highlight --------------------------------------------------------------------------------

    /// <summary>What the fader under the pointer controls; views mark it, so the operator sees what is on a fader.</summary>
    public FaderInfo? Highlight { get; private set; }

    public event Action? HighlightChanged;

    public void SetHighlight(FaderInfo? fader)
    {
        var next = fader is { IsAssigned: true } ? fader : null;
        if (Highlight == next)
            return;

        Highlight = next;
        HighlightChanged?.Invoke();
    }

    /// <summary>Page of the fader bar (8 faders each). Shared by the docked bar and the pop-up window.</summary>
    public int FaderPage
    {
        get => Math.Clamp(Settings.FaderPage, 1, FaderPages);
        set
        {
            Settings.FaderPage = Math.Clamp(value, 1, FaderPages);
            FaderLayoutChanged?.Invoke();
        }
    }

    public const int FadersPerPage = 8;

    public static int FaderPages => NxDmx.Core.Playback.FaderConfig.MaxFaders / FadersPerPage;

    /// <summary>The fader bar was shown, hidden, popped out or docked, or its page changed.</summary>
    public event Action? FaderLayoutChanged;

    public void SetFaderBar(bool visible, bool popped)
    {
        Settings.FaderBarVisible = visible;
        Settings.FadersPopped = popped;
        Settings.Save();
        FaderLayoutChanged?.Invoke();
    }

    // ---- Art-Net nodes ---------------------------------------------------------------------------------------

    /// <summary>The Art-Net nodes found by the last search, for choosing the receiver of a universe.</summary>
    public IReadOnlyList<NxDmx.Core.ArtNet.ArtNetNode> Nodes { get; private set; } = [];

    public event Action? NodesChanged;

    /// <summary>
    /// Polls the network for Art-Net nodes (two seconds). Returns an error text, or null when the search ran, with or
    /// without finding anything. The result is kept in <see cref="Nodes"/>.
    /// </summary>
    public async Task<string?> FindNodesAsync()
    {
        try
        {
            var target = IPAddress.TryParse(Settings.BroadcastAddress.Trim(), out var parsed) ? parsed : IPAddress.Broadcast;
            IPAddress? local = IPAddress.TryParse(Settings.LocalAddress.Trim(), out var adapter) ? adapter : null;
            Nodes = await NxDmx.Core.ArtNet.ArtNetDiscovery.DiscoverAsync(target, local, TimeSpan.FromSeconds(2));
            NodesChanged?.Invoke();
            AddLog(Nodes.Count == 0 ? "No Art-Net nodes answered." : $"Found {Nodes.Count} Art-Net node{(Nodes.Count == 1 ? "" : "s")}: {string.Join(", ", Nodes.Select(n => n.Text))}");
            return null;
        }
        catch (SocketException ex)
        {
            var message = ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied
                ? "Another program is using the Art-Net port 6454 exclusively. Close it to search for nodes."
                : $"The search failed: {ex.Message}";
            AddLog(message, error: true);
            return message;
        }
    }

    /// <summary>The answer to the last command typed or clicked.</summary>
    public (string Text, bool IsError) Feedback { get; private set; } = ("", false);

    /// <summary>Shows a message next to the log tabs and writes it to the log, for things a view refuses to do itself.</summary>
    public void Notify(string text, bool isError = false)
    {
        AddLog(text, error: isError);
        Feedback = (text, isError);
        FeedbackChanged?.Invoke();
    }

    public event Action? FeedbackChanged;

    /// <summary>Walks back through earlier lines (Up) or forward (Down). Returns the text to show, or null at the end.</summary>
    public string? HistoryStep(int direction)
    {
        if (direction < 0 && _historyIndex > 0)
            return _history[--_historyIndex];

        if (direction > 0 && _historyIndex < _history.Count)
        {
            _historyIndex++;
            return _historyIndex < _history.Count ? _history[_historyIndex] : "";
        }

        return null;
    }

    public void ResetHistoryPosition() => _historyIndex = _history.Count;

    public void AddLog(string text, bool echo = false, bool error = false, string source = "UI")
    {
        Log.Add(new LogEntry(DateTime.Now, text, echo, error, source));
        while (Log.Count > MaxLogEntries)
            Log.RemoveAt(0);
    }

    // ---- show files ------------------------------------------------------------------------------------------

    /// <summary>
    /// Brings back the autosaved show of the last session. On the very first start there is none, so a small demo rig
    /// is patched, through the normal commands, so there is something to drive.
    /// </summary>
    private string? LoadStartupShow()
    {
        string? note = null;
        if (ShowStorage.ReadAutosave() is { } autosave)
        {
            try
            {
                Console.ImportShow(autosave);
                ShowPath = ShowStorage.ReadSessionPath();
                _autosavedJson = Console.ExportShow();
                _savedJson = BaselineOf(ShowPath);
                RestoredSession = true;
                return "Restored your show from the last session.";
            }
            catch (ShowFileException ex)
            {
                note = $"The autosaved show could not be restored ({ex.Message}) and was ignored.";
            }
        }

        if (DemoShow.Build(Console) is { } failure)
            note = $"The pre-built show could not be built ({failure})";

        _savedJson = Console.ExportShow();
        return note;
    }

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

    private void MarkSaved()
    {
        UpdateShowState();
        ShellChanged?.Invoke();
    }

    public bool HasUnsavedChanges => Console.ExportShow() != _savedJson;

    public void NewShow()
    {
        Console.ImportShow(ShowFile.Serialize(new Show()));
        ShowPath = null;
        _savedJson = Console.ExportShow();
        AddLog("New empty show. Add a universe and patch fixtures in the Patch view, or type: Patch Fixture 1 Thru 8 Type RGBDim At 1.1");
        RefreshNow();
        ShellChanged?.Invoke();
    }

    /// <summary>A new show with a small rig already patched and programmed, to have something to drive at once.</summary>
    public void NewPrebuiltShow()
    {
        var failure = DemoShow.Build(Console);
        ShowPath = null;
        _savedJson = Console.ExportShow();
        AddLog(failure is null
            ? "New pre-built show: 8 RGB washes, 4 movers, 4 dimmers, 3 groups and a cuelist of 3 cues."
            : $"The pre-built show could not be built completely: {failure}", error: failure is not null);
        RefreshNow();
        ShellChanged?.Invoke();
    }

    // Opening and saving are done in two parts. The first part is the slow one (reading and writing the file, building
    // the show) and may run on another thread under a progress window; the second part brings the interface up to date
    // and runs on the interface thread.

    /// <summary>
    /// Reads the show file and replaces the show with it. Safe to run off the interface thread. Throws
    /// <see cref="ShowFileException"/>, an <see cref="IOException"/> or <see cref="OperationCanceledException"/> and leaves the show
    /// alone when it cannot be opened. Returns the text the show is compared with to see unsaved changes.
    /// </summary>
    public string LoadShowFile(string path, BusyJob job)
    {
        var text = ShowStorage.Read(path, job.Step(0, 0.1, "Reading the file…"));
        Console.ImportShow(text, job.Step(0.1, 0.7, "Reading the show…"));
        job.PointOfNoReturn();
        return Console.ExportShow(job.Step(0.7, 1, "Preparing the show…"));
    }

    public void CompleteOpen(string path, string savedText)
    {
        ShowPath = path;
        _savedJson = savedText;
        AddRecent(path);
        AddLog($"Opened {Path.GetFileName(path)}.");
        RefreshNow();
        ShellChanged?.Invoke();
    }

    /// <summary>Writes the show to the file. Safe to run off the interface thread. Returns the text that was written.</summary>
    public string SaveShowFile(string path, BusyJob job)
    {
        var text = Console.ExportShow(job.Step(0, 0.85, "Collecting the show…"));
        ShowStorage.Write(path, text, job.Step(0.85, 1, "Writing the file…"));
        return text;
    }

    public void CompleteSave(string path, string savedText)
    {
        ShowPath = path;
        _savedJson = savedText;
        AddRecent(path);
        AddLog($"Saved {Path.GetFileName(path)}.");
        UpdateShowState();
        ShellChanged?.Invoke();
    }

    private const int MaxRecentShows = 8;

    /// <summary>The show files opened or saved last that still exist, newest first.</summary>
    public IReadOnlyList<string> RecentShows => Settings.RecentShows.Where(File.Exists).ToList();

    public void AddRecent(string path)
    {
        Settings.RecentShows.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Settings.RecentShows.Insert(0, path);
        if (Settings.RecentShows.Count > MaxRecentShows)
            Settings.RecentShows.RemoveRange(MaxRecentShows, Settings.RecentShows.Count - MaxRecentShows);

        Settings.Save();
    }

    public void ForgetRecent(string path)
    {
        Settings.RecentShows.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Settings.Save();
    }

    /// <summary>Updates the unsaved marker and keeps the autosave current.</summary>
    private void UpdateShowState()
    {
        var json = Console.ExportShow();
        var dirty = json != _savedJson;
        var changed = dirty != IsDirty;
        IsDirty = dirty;

        if (json != _autosavedJson || ShowPath != _autosavedPath)
            Autosave(json);

        if (changed)
            ShellChanged?.Invoke();
    }

    public void Autosave() => Autosave(Console.ExportShow());

    private void Autosave(string json)
    {
        try
        {
            ShowStorage.WriteAutosave(json, ShowPath);
            _autosavedJson = json;
            _autosavedPath = ShowPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting the operator; the next change tries again.
        }
    }

    // ---- Art-Net output --------------------------------------------------------------------------------------

    /// <summary>(Re)creates the Art-Net socket from the settings and switches the engine output on or off.</summary>
    public void ApplyOutput()
    {
        try
        {
            if (!IPAddress.TryParse(Settings.BroadcastAddress.Trim(), out var target) || target.AddressFamily != AddressFamily.InterNetwork)
                throw new FormatException($"Default destination: '{Settings.BroadcastAddress.Trim()}' is not an IPv4 address");

            IPAddress? local = null;
            if (Settings.LocalAddress.Trim().Length > 0)
            {
                if (!IPAddress.TryParse(Settings.LocalAddress.Trim(), out local) || local.AddressFamily != AddressFamily.InterNetwork)
                    throw new FormatException($"Network adapter: '{Settings.LocalAddress.Trim()}' is not an IPv4 address");
            }

            var output = new ArtNetOutput(target, localAddress: local);
            var previous = _artNet;
            _artNet = output;
            Engine.Output = Settings.OutputEnabled ? output : null;
            previous?.Dispose();
            OutputSetupError = null;
        }
        catch (Exception ex) when (ex is FormatException or SocketException)
        {
            // Keep the old socket, so a typo in the settings does not silence a running show.
            OutputSetupError = ex.Message;
        }

        Settings.Save();
        ShellChanged?.Invoke();
    }

    public void SetOutputEnabled(bool enabled)
    {
        Settings.OutputEnabled = enabled;
        Engine.Output = enabled ? _artNet : null;
        Settings.Save();
        ShellChanged?.Invoke();
    }

    public string OutputStatus
    {
        get
        {
            if (OutputSetupError is not null)
                return $"Setup error: {OutputSetupError}";
            if (Engine.Output is null)
                return "Output off";
            if (Engine.LastError is { } error)
                return $"Send error: {error}";

            var universes = Engine.LastFrame?.Universes.Count ?? 0;
            return $"{FrameRate:0} Hz · {universes} universe{(universes == 1 ? "" : "s")}";
        }
    }

    /// <summary>Red when something is wrong, green while sending, grey when off.</summary>
    public OutputHealth Health =>
        OutputSetupError is not null || (Engine.Output is not null && Engine.LastError is not null) ? OutputHealth.Error
        : Engine.Output is null ? OutputHealth.Off
        : OutputHealth.Sending;

    private void UpdateRate()
    {
        var now = DateTime.UtcNow;
        var frames = Engine.FramesSent;
        var elapsed = (now - _lastFrameTime).TotalSeconds;
        if (elapsed >= 1)
        {
            FrameRate = (frames - _lastFrameCount) / elapsed;
            _lastFrameCount = frames;
            _lastFrameTime = now;
        }

        ShellChanged?.Invoke();
    }

    /// <summary>IPv4 adapters that are up, for choosing where Art-Net is sent from.</summary>
    public static IReadOnlyList<NetworkChoice> NetworkAdapters()
    {
        var choices = new List<NetworkChoice> { new("", "Automatic (Windows chooses)") };
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                foreach (var address in adapter.GetIPProperties().UnicastAddresses
                             .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    choices.Add(new NetworkChoice(address.Address.ToString(), $"{adapter.Name} – {address.Address}", BroadcastOf(address)));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Without the adapter list the operator can still type the address.
        }

        return choices;
    }

    /// <summary>The broadcast address of the adapter's subnet (192.168.1.255 for 192.168.1.20/24).</summary>
    private static string BroadcastOf(UnicastIPAddressInformation info)
    {
        var address = info.Address.GetAddressBytes();
        var mask = info.IPv4Mask?.GetAddressBytes();
        if (mask is null || mask.Length != 4)
            return "";

        return new IPAddress(address.Select((b, i) => (byte)(b | ~mask[i])).ToArray()).ToString();
    }

    // ---- remote ----------------------------------------------------------------------------------------------

    public void ApplyRemote()
    {
        Remote.Apply(Settings.ToRemoteOptions());
        Settings.Save();
        ShellChanged?.Invoke();
    }

    // ---- lifetime --------------------------------------------------------------------------------------------

    public void Dispose()
    {
        _statusTimer.Stop();
        Settings.Save();
        Remote.Dispose();
        Engine.Dispose();
        _artNet?.Dispose();
    }
}

public enum OutputHealth
{
    Off,
    Sending,
    Error,
}
