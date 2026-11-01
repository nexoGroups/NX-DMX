using System.IO;
using System.Text;

namespace NxDmx.App.Services;

/// <summary>
/// Where the app keeps its files. The autosave holds the current show after every change, so a crash, a power cut or an
/// update restart brings it back at the next start. <c>session.txt</c> remembers which show file the autosave belongs to.
/// </summary>
internal static class ShowStorage
{
    /// <summary>Another place for the app data (autosave, settings), for tests and portable use: set NXDMX_DATA.</summary>
    public const string DataOverrideVariable = "NXDMX_DATA";

    private static readonly string Folder =
        Environment.GetEnvironmentVariable(DataOverrideVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NX-DMX");

    private static string AutosavePath => Path.Combine(Folder, "autosave.nxshow");

    private static string SessionPath => Path.Combine(Folder, "session.txt");

    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    /// <summary>Default folder of the open and save dialogs.</summary>
    public static string ShowsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NX-DMX Shows");

    public static string? ReadAutosave() => ReadOrNull(AutosavePath);

    public static string? ReadSessionPath()
    {
        var path = ReadOrNull(SessionPath)?.Trim();
        return string.IsNullOrEmpty(path) ? null : path;
    }

    public static void WriteAutosave(string text, string? showPath)
    {
        Write(AutosavePath, text);
        Write(SessionPath, showPath ?? "");
    }

    /// <summary>Writes to a temporary file first, so a failure half-way never leaves a broken show behind.</summary>
    public static void Write(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    public static string Read(string path) => File.ReadAllText(path, Encoding.UTF8);

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
