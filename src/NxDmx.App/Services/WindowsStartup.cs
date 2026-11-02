using System.IO;
using Microsoft.Win32;

namespace NxDmx.App.Services;

/// <summary>Starting NX-DMX when the user signs in to Windows, through the per-user Run key (no admin rights needed).</summary>
public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NX-DMX";

    /// <summary>Whether NX-DMX is set to start with Windows (for any path, not only this copy).</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string { Length: > 0 };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Turns it on (with the path of this program) or off. Returns an error text, or null when it worked.</summary>
    public static string? Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var path = Environment.ProcessPath ?? throw new InvalidOperationException("The path of the program is unknown.");
                key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
