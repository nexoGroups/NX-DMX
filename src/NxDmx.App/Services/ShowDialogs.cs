using System.IO;
using System.Windows;
using Microsoft.Win32;
using NxDmx.App.Views;
using NxDmx.Core.Storage;

namespace NxDmx.App.Services;

/// <summary>The file dialogs and questions around show files (New, Open, Save, Save as), shared by the window and the Show view.</summary>
public static class ShowDialogs
{
    private const string ShowFilter = "NX-DMX show (*.nxshow)|*.nxshow|All files (*.*)|*.*";

    public static void New(ConsoleHost host, Window owner)
    {
        if (ConfirmDiscard(host, owner))
            host.NewShow();
    }

    /// <summary>A new show with a small rig already patched and programmed.</summary>
    public static void NewPrebuilt(ConsoleHost host, Window owner)
    {
        if (ConfirmDiscard(host, owner))
            host.NewPrebuiltShow();
    }

    public static void Open(ConsoleHost host, Window owner)
    {
        if (!ConfirmDiscard(host, owner))
            return;

        var dialog = new OpenFileDialog { Filter = ShowFilter, InitialDirectory = EnsureShowsFolder(), Title = "Open show" };
        if (dialog.ShowDialog(owner) == true)
            OpenFile(host, owner, dialog.FileName);
    }

    /// <summary>Opens a show file under a progress window that can be cancelled. The caller has asked about unsaved changes already.</summary>
    public static bool OpenFile(ConsoleHost host, Window owner, string path)
    {
        try
        {
            // The views are refreshed once, inside the progress window, when the show is read: not once per change on the way.
            using (host.PauseRefresh())
            {
                Busy.Run(owner, $"Opening {Path.GetFileName(path)}", job => host.LoadShowFile(path, job), cancellable: true,
                    onScreen: saved => host.CompleteOpen(path, saved));
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            host.AddLog($"Opening {Path.GetFileName(path)} was cancelled. The show is as it was.");
        }
        catch (Exception ex) when (ex is ShowFileException or IOException or UnauthorizedAccessException)
        {
            host.AddLog($"Could not open {Path.GetFileName(path)}: {ex.Message}", error: true);
            MessageBox.Show(owner, ex.Message, "Open show", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (ex is IOException or UnauthorizedAccessException)
                host.ForgetRecent(path);
        }

        return false;
    }

    /// <summary>Saves under the current name, or asks for one. Returns false when the operator cancelled or it failed.</summary>
    public static bool Save(ConsoleHost host, Window owner) => host.ShowPath is null ? SaveAs(host, owner) : Write(host, owner, host.ShowPath);

    public static bool SaveAs(ConsoleHost host, Window owner)
    {
        var dialog = new SaveFileDialog
        {
            Filter = ShowFilter,
            DefaultExt = ShowFile.Extension,
            AddExtension = true,
            InitialDirectory = host.ShowPath is null ? EnsureShowsFolder() : Path.GetDirectoryName(host.ShowPath),
            FileName = host.ShowPath is null ? "My show" : Path.GetFileName(host.ShowPath),
            Title = "Save show",
        };

        return dialog.ShowDialog(owner) == true && Write(host, owner, dialog.FileName);
    }

    private static bool Write(ConsoleHost host, Window owner, string path)
    {
        try
        {
            Busy.Run(owner, $"Saving {Path.GetFileName(path)}", job => host.SaveShowFile(path, job), onScreen: saved => host.CompleteSave(path, saved));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            host.AddLog($"Could not save {Path.GetFileName(path)}: {ex.Message}", error: true);
            MessageBox.Show(owner, ex.Message, "Save show", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Asks what to do with unsaved changes. Returns false when the operator cancelled.</summary>
    public static bool ConfirmDiscard(ConsoleHost host, Window owner)
    {
        if (!host.HasUnsavedChanges)
            return true;

        var answer = MessageBox.Show(owner,
            $"Save the changes to \"{host.ShowTitle}\" first?",
            "NX-DMX", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        return answer switch
        {
            MessageBoxResult.Yes => Save(host, owner),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    /// <summary>Asks before something is deleted, unless the operator turned the question off in the settings.</summary>
    public static bool ConfirmDelete(ConsoleHost host, Window owner, string text, string title) =>
        !host.Settings.ConfirmDeletes
        || MessageBox.Show(owner, text, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

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
}
