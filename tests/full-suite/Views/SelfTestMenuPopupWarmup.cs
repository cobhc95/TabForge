using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static void TestMenuPopupWarmup()
    {
        using var alive = KeepAlive();
        var window = new Window { Content = new TextBox { Text = "focus target" }, Width = 480, Height = 260 };
        try
        {
            ShowTestWindow(window);
            var focusTarget = (TextBox)window.Content;
            focusTarget.Focus();
            Keyboard.Focus(focusTarget);
            var item = new MenuItem { Header = "Never invoke", IsCheckable = true, IsChecked = true };
            var commands = 0;
            item.Click += (_, _) => commands++;
            var menu = new ContextMenu
            {
                PlacementTarget = window,
                Placement = PlacementMode.Bottom,
                Opacity = .4,
                HasDropShadow = true
            };
            menu.Items.Add(item);
            var focusable = item.Focusable;
            var tabStop = item.IsTabStop;
            var checkedState = item.IsChecked;
            var shadow = menu.HasDropShadow;
            var focus = Keyboard.FocusedElement;
            var logicalFocus = FocusManager.GetFocusedElement(window);
            var foreground = GetForegroundWindow();
            var warm = MenuPopupWarmup.OpenAndClose(menu);
            Check("menu popup: creates and closes a real HWND without activation", warm.Handle != IntPtr.Zero && warm.NoActivate && !warm.SkippedOpen && !menu.IsOpen);
            Check("menu popup: preserves logical focus, keyboard focus state and foreground window",
                ReferenceEquals(focusTarget, logicalFocus) && warm.FocusPreserved && ReferenceEquals(focus, Keyboard.FocusedElement) && ReferenceEquals(logicalFocus, FocusManager.GetFocusedElement(window)) && foreground == GetForegroundWindow(),
                $"keyboard focus {Keyboard.FocusedElement?.GetType().Name ?? "none"}, logical focus {FocusManager.GetFocusedElement(window)?.GetType().Name ?? "none"}, foreground {foreground == GetForegroundWindow()}");
            Check("menu popup: restores temporary appearance and item state without running a command", menu.Opacity == .4 && menu.HasDropShadow == shadow && item.Focusable == focusable && item.IsTabStop == tabStop && item.IsChecked == checkedState && menu.Items.Count == 1 && commands == 0,
                $"opacity {menu.Opacity}; shadow {menu.HasDropShadow}/{shadow}; focusable {item.Focusable}/{focusable}; tab stop {item.IsTabStop}/{tabStop}; checked {item.IsChecked}/{checkedState}; items {menu.Items.Count}; commands {commands}");

            var failed = false;
            try { MenuPopupWarmup.OpenAndClose(menu, () => throw new InvalidOperationException("injected popup failure")); }
            catch (InvalidOperationException) { failed = true; }
            Check("menu popup: an open-path exception closes the popup and restores menu and focus state",
                failed && !menu.IsOpen && menu.Opacity == .4 && menu.HasDropShadow == shadow && item.Focusable == focusable && item.IsTabStop == tabStop && menu.Items.Count == 1 && ReferenceEquals(focus, Keyboard.FocusedElement) && ReferenceEquals(logicalFocus, FocusManager.GetFocusedElement(window)),
                $"threw {failed}; open {menu.IsOpen}; opacity {menu.Opacity}; shadow {menu.HasDropShadow}/{shadow}; focusable {item.Focusable}/{focusable}; tab stop {item.IsTabStop}/{tabStop}; items {menu.Items.Count}; focus {ReferenceEquals(focus, Keyboard.FocusedElement)}");

            menu.Opacity = 0;
            menu.HasDropShadow = false;
            menu.IsOpen = true;
            PumpUi();
            var alreadyOpen = MenuPopupWarmup.OpenAndClose(menu);
            Check("menu popup: an already-open popup is left open and unchanged", alreadyOpen.SkippedOpen && menu.IsOpen && menu.Opacity == 0 && !menu.HasDropShadow && menu.Items.Count == 1);
            menu.IsOpen = false;
        }
        finally { window.Close(); }
    }
}
