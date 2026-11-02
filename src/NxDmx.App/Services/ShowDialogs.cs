using System.IO;
using System.Windows;
using Microsoft.Win32;
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

    public static void Open(ConsoleHost host, Window owner)
    {
        if (!ConfirmDiscard(host, owner))
            return;

        var dialog = new OpenFileDialog { Filter = ShowFilter, InitialDirectory = EnsureShowsFolder(), Title = "Open show" };
        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            host.OpenShow(dialog.FileName);
        }
        catch (Exception ex) when (ex is ShowFileException or IOException or UnauthorizedAccessException)
        {
            host.AddLog($"Could not open {Path.GetFileName(dialog.FileName)}: {ex.Message}", error: true);
            MessageBox.Show(owner, ex.Message, "Open show", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            host.SaveShow(path);
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
