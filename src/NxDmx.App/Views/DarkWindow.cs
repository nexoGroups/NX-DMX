using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace NxDmx.App.Views;

/// <summary>Base of the small windows (dialogs and the fader pop-up): black like the main window, with a black title bar.</summary>
public class DarkWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public DarkWindow()
    {
        Background = Ui.Brush("BgBrush");
        Foreground = Ui.Brush("TextBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        UseLayoutRounding = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var dark = 1;
            var black = 0x000000;
            var text = 0xDADADA;
            _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            _ = DwmSetWindowAttribute(handle, 35, ref black, sizeof(int));
            _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        };

        // Escape closes a dialog, like Cancel.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && CloseOnEscape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    protected virtual bool CloseOnEscape => true;
}
