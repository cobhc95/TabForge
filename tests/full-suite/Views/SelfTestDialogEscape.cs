using System.Windows;
using System.Windows.Controls;
using TabForge.Views;

namespace TabForge;

/// <summary>Every small dialog closes on Esc however it was opened: DialogHost presses the dialog's Cancel button itself.</summary>
public static partial class SelfTest
{
    private static void TestDialogEscape()
    {
        var clicks = 0;
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => clicks++;
        var window = new Window { Content = new StackPanel { Children = { new TextBox(), new Button { Content = "OK", IsDefault = true }, cancel } }, Width = 200, Height = 120, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.UpdateLayout();
            var pressed = DialogHost.PressCancel(window);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);   // automation Invoke is queued
            Check("dialog Esc: the fallback presses the dialog's Cancel button", pressed && clicks == 1, clicks.ToString());
            cancel.IsEnabled = false;
            var pressedDisabled = DialogHost.PressCancel(window);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Check("dialog Esc: a disabled Cancel button is not pressed", !pressedDisabled && clicks == 1);
            cancel.IsEnabled = true;
            var plain = new Window { Content = new Button { Content = "OK" }, ShowInTaskbar = false };
            plain.Show();
            Check("dialog Esc: a dialog with no Cancel button is left alone", !DialogHost.PressCancel(plain));
            plain.Close();
        }
        finally { window.Close(); }
    }
}
