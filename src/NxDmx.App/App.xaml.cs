using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using NxDmx.App.Services;

namespace NxDmx.App;

public partial class App : Application
{
    private bool _reported;

    public App()
    {
        // An error in one handler must not close the program in the middle of a show: it is written to a log, the
        // operator is told once, and everything keeps running. The show is autosaved after every change anyway.
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        e.Handled = true;

        if (_reported)
            return;

        _reported = true;
        MessageBox.Show(
            "Something unexpected went wrong. NX-DMX keeps running and the DMX output continues.\n\n" +
            $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
            "Your show is saved automatically. Details are in the file error.log in the NX-DMX data folder (SHOW view, Open backup folder).",
            "NX-DMX", MessageBoxButton.OK, MessageBoxImage.Warning);
        _reported = false;
    }

    private static void WriteCrashLog(Exception? exception)
    {
        if (exception is null)
            return;

        try
        {
            var path = Path.Combine(Path.GetDirectoryName(ShowStorage.SettingsPath)!, "error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {exception}\n\n", Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done about a log that cannot be written.
        }
    }
}
