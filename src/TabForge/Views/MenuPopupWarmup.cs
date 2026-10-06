using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;

namespace TabForge.Views;

// Owns: temporary popup lifetime for diagnostic real-HWND and visual-tree measurements without activating the window.
// Does not own: menu creation, item state, or command handlers.
// Tests: TestMenuPopupWarmup.
internal static class MenuPopupWarmup
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    internal readonly record struct Result(IntPtr Handle, double OpenMs, bool FocusPreserved, bool NoActivate, bool SkippedOpen,
        bool SourceWasImmediate, double DispatcherWaitMs, double LayoutMs, double CloseFlushMs);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    public static Result OpenAndClose(ContextMenu menu, Action? afterOpen = null)
    {
        if (menu.IsOpen)
        {
            var existing = PresentationSource.FromVisual(menu) as HwndSource;
            var handle = existing?.Handle ?? IntPtr.Zero;
            return new Result(handle, 0, true,
                handle != IntPtr.Zero && (GetWindowLong(handle, GwlExStyle) & WsExNoActivate) != 0, true, true, 0, 0, 0);
        }

        var oldOpacity = menu.Opacity;
        var oldShadow = menu.HasDropShadow;
        var oldFocus = Keyboard.FocusedElement;
        var focusable = OwnedControls(menu).Select(c => (Control: c, c.Focusable, c.IsTabStop)).ToArray();
        var timer = Stopwatch.StartNew();
        try
        {
            menu.Opacity = 0;
            menu.HasDropShadow = false;
            foreach (var (control, _, _) in focusable) { control.Focusable = false; control.IsTabStop = false; }
            menu.IsOpen = true;
            afterOpen?.Invoke();
            var source = PresentationSource.FromVisual(menu) as HwndSource;
            var sourceWasImmediate = source is not null;
            var wait = Stopwatch.StartNew();
            if (source is null)
            {
                menu.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(static () => { }));
                source = PresentationSource.FromVisual(menu) as HwndSource;
            }
            var dispatcherWaitMs = sourceWasImmediate ? 0 : wait.Elapsed.TotalMilliseconds;
            var layout = Stopwatch.StartNew();
            menu.UpdateLayout();
            var layoutMs = layout.Elapsed.TotalMilliseconds;
            var handle = source?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) throw new InvalidOperationException("The context menu did not create a popup HWND.");
            var noActivate = (GetWindowLong(handle, GwlExStyle) & WsExNoActivate) != 0;
            if (!noActivate) throw new InvalidOperationException("The context menu popup HWND was not created as non-activating.");
            menu.IsOpen = false;
            var closeFlush = Stopwatch.StartNew();
            menu.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(static () => { }));
            var closeFlushMs = closeFlush.Elapsed.TotalMilliseconds;
            if (oldFocus is IInputElement input && !ReferenceEquals(oldFocus, Keyboard.FocusedElement)) Keyboard.Focus(input);
            return new Result(handle, timer.Elapsed.TotalMilliseconds,
                ReferenceEquals(oldFocus, Keyboard.FocusedElement), noActivate, false, sourceWasImmediate, dispatcherWaitMs, layoutMs, closeFlushMs);
        }
        finally
        {
            if (menu.IsOpen) menu.IsOpen = false;
            menu.Opacity = oldOpacity;
            menu.HasDropShadow = oldShadow;
            foreach (var (control, canFocus, canTabStop) in focusable) { control.Focusable = canFocus; control.IsTabStop = canTabStop; }
            if (oldFocus is IInputElement input && !ReferenceEquals(oldFocus, Keyboard.FocusedElement)) Keyboard.Focus(input);
        }
    }

    private static IEnumerable<Control> OwnedControls(ItemsControl owner)
    {
        if (owner is Control control) yield return control;
        foreach (var item in owner.Items.OfType<Control>())
        {
            yield return item;
            if (item is MenuItem submenu)
                foreach (var child in OwnedControls(submenu)) yield return child;
        }
    }
}
