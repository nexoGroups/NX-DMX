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
    public static void Write(string path, string text, IProgress<double>? progress = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        if (progress is null)
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
        }
        else
        {
            // In pieces, so the progress is the part of the file that is really on disk.
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
            for (var written = 0; written < bytes.Length;)
            {
                var count = Math.Min(ChunkSize, bytes.Length - written);
                file.Write(bytes, written, count);
                written += count;
                progress.Report((double)written / bytes.Length);
            }
        }

        File.Move(temp, path, overwrite: true);
        progress?.Report(1);
    }

    private const int ChunkSize = 1 << 20;

    /// <param name="progress">Reports the part of the file read so far.</param>
    public static string Read(string path, IProgress<double>? progress = null)
    {
        if (progress is null)
            return File.ReadAllText(path, Encoding.UTF8);

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[file.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = file.Read(bytes, read, Math.Min(ChunkSize, bytes.Length - read));
            if (count == 0)
                break;

            read += count;
            progress.Report((double)read / bytes.Length);
        }

        progress.Report(1);
        using var reader = new StreamReader(new MemoryStream(bytes, 0, read), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

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
