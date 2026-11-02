using System.IO;
using System.Text.Json;
using NxDmx.Core.Remote;

namespace NxDmx.App.Services;

/// <summary>
/// Settings of this computer and installation, as opposed to the show: which network adapter to send from, which
/// remote interfaces are on, which view was open. Stored as JSON next to the autosave. A damaged file is ignored.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>IPv4 address of the adapter Art-Net is sent from. Empty lets Windows choose.</summary>
    public string LocalAddress { get; set; } = "";

    /// <summary>Where universes without their own node go. 255.255.255.255 reaches every node on the local network.</summary>
    public string BroadcastAddress { get; set; } = "255.255.255.255";

    public bool OutputEnabled { get; set; } = true;

    public bool HttpEnabled { get; set; } = true;
    public int HttpPort { get; set; } = 8765;
    public bool OscEnabled { get; set; } = true;
    public int OscPort { get; set; } = 8000;
    public bool TextEnabled { get; set; } = true;
    public int TextPort { get; set; } = 7000;
    public bool RemoteAllowNetwork { get; set; }
    public string RemoteToken { get; set; } = "";

    public string LastView { get; set; } = "Fixtures";
    public bool KeypadVisible { get; set; } = true;

    /// <summary>Starts NX-DMX when Windows starts. Off until the operator turns it on.</summary>
    public bool StartWithWindows { get; set; }

    public bool CheckUpdatesOnStart { get; set; } = true;

    /// <summary>Opens the start window (continue, new show, open show) when NX-DMX starts.</summary>
    public bool ShowStartWindow { get; set; } = true;

    /// <summary>Show files opened or saved last, newest first.</summary>
    public List<string> RecentShows { get; set; } = [];

    /// <summary>Opens the view that was open when the program was closed. Off always starts in Fixtures.</summary>
    public bool RememberView { get; set; } = true;

    /// <summary>Asks before deleting fixtures, universes and profiles.</summary>
    public bool ConfirmDeletes { get; set; } = true;

    /// <summary>Size of the whole interface in percent. 100 is normal, larger sizes are easier to hit with a finger.</summary>
    public int UiScalePercent { get; set; } = 100;

    public bool FaderBarVisible { get; set; }

    /// <summary>The fader bar is shown in a window of its own instead of in the main window.</summary>
    public bool FadersPopped { get; set; }

    public int FaderPage { get; set; } = 1;

    public RemoteOptions ToRemoteOptions() =>
        new(HttpEnabled, HttpPort, OscEnabled, OscPort, TextEnabled, TextPort, RemoteAllowNetwork, RemoteToken);

    public static AppSettings Load()
    {
        try
        {
            var path = ShowStorage.SettingsPath;
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            ShowStorage.Write(ShowStorage.SettingsPath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to store them must never interrupt the operator.
        }
    }
}
