using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>
/// Runs a long job off the interface thread and shows its progress in a window while it lasts. A job that is done
/// quickly never shows the window. The call returns when the job has ended, so it can be used where the answer is needed
/// at once (before the program closes, say). Throws what the job throws; a cancelled job throws
/// <see cref="OperationCanceledException"/>.
/// </summary>
public static class Busy
{
    /// <summary>Jobs shorter than this finish without a window: a bar that flashes up and goes is only noise.</summary>
    private static readonly TimeSpan ShowAfter = TimeSpan.FromMilliseconds(400);

    /// <param name="work">The job. It runs on another thread and reports its progress through the <see cref="BusyJob"/>.</param>
    /// <param name="onScreen">
    /// What the job leaves to do on the interface thread once its result is there (showing the new show on screen). It
    /// runs while the window is still open, labelled as such, so the wait after the bar is full is not unexplained.
    /// </param>
    public static T Run<T>(Window? owner, string title, Func<BusyJob, T> work, bool cancellable = false, Action<T>? onScreen = null)
    {
        var job = new BusyJob(cancellable);
        var task = Task.Run(() => work(job));

        if (task.Wait(ShowAfter))
        {
            var quick = task.GetAwaiter().GetResult();
            onScreen?.Invoke(quick);
            return quick;
        }

        var window = new ProgressWindow(title, job, cancellable);
        if (owner is { IsLoaded: true })
            window.Owner = owner;
        else
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        ExceptionDispatchInfo? screenError = null;
        task.ContinueWith(finished => window.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (finished.IsCompletedSuccessfully && onScreen is not null)
                {
                    // The bar stands still while the screen is rebuilt, so say what is going on and let that be painted first.
                    window.ShowScreenPhase();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    onScreen(finished.Result);
                }
            }
            catch (Exception ex)
            {
                screenError = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                window.Finish();
            }
        }), TaskScheduler.Default);

        window.ShowDialog();
        var result = task.GetAwaiter().GetResult();
        screenError?.Throw();
        return result;
    }

    public static void Run(Window? owner, string title, Action<BusyJob> work, bool cancellable = false) =>
        Run(owner, title, job =>
        {
            work(job);
            return 0;
        }, cancellable);
}
