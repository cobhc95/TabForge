using System.Windows;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the keyboard pop-out checks: opening lends the pane's keyboard view to its own window, closing (button, leaving Keyboard mode, closing the main window) puts it back,
//   the same Keyboard mode controller and view serve both places (no second session), F11 / Esc full screen, and the command, menu and hotkey rows.
// Does not own: the keyboard view itself (TestKeyboardModeKeyView) or the layout swap (TestKeyboardModeLayout).
// Tests: TestKeyboardModePopout.
public static partial class SelfTest
{
    private static void TestKeyboardModePopout()
    {
        RunInWindowFixture((w, context) =>
        {
            w.Width = 1400; w.Height = 900;
            BandDockSettle(w);
            var mode = w.KeyboardMode;
            var pane = w.KeyboardModePane;
            Func<bool> open = () => System.Windows.Application.Current.Windows.OfType<KeyboardModePopoutWindow>().Any();
            Action toggle = () => w.Learn.RequestPopout();
            Func<KeyboardModePopoutWindow?> win0 = () => System.Windows.Application.Current.Windows.OfType<KeyboardModePopoutWindow>().FirstOrDefault();
            var track = w.Editor.Project!.Tracks[0];
            w.TrackMixerGrid.SelectedIndex = 0;
            var view = pane.Keys.Element;
            var controller = w.Learn;
            var surface = controller.Keys;

            track.Kind = TrackKind.Guitar;
            mode.Enter(); BandDockSettle(w);
            toggle();
            Check("learn popout: a guitar track pops out too", open() && pane.IsKeysDetached);
            mode.Exit();

            track.Kind = TrackKind.Keys;
            mode.Enter(); BandDockSettle(w);
            Check("learn popout: closed, the view is in the pane", view.Parent is not null && !pane.IsKeysDetached && !open());
            toggle();
            var win = win0();
            Check("learn popout: the command opens a window that hosts the view and the pane lets go of it", open() && win is not null && pane.IsKeysDetached && !pane.Children.Contains(view) && view.IsDescendantOf(win!));
            Check("learn popout: the same controller and view serve it (no second session)", ReferenceEquals(w.Learn, controller) && ReferenceEquals(w.Learn.Keys, surface) && ReferenceEquals(pane.Keys.Element, view));

            win!.ToggleFullScreen();
            Check("learn popout: F11 toggles full screen", win.IsFullScreen && win.WindowState == WindowState.Maximized && win.WindowStyle == WindowStyle.None);
            win.ToggleFullScreen();
            Check("learn popout: leaving full screen restores the window", !win.IsFullScreen && win.WindowStyle == WindowStyle.SingleBorderWindow);

            toggle();
            Check("learn popout: closing puts the view back in the pane and visible", !open() && !pane.IsKeysDetached && pane.Children.Contains(view) && view.Visibility == Visibility.Visible && view.Parent == pane);

            if (!open()) toggle();
            mode.Exit(); BandDockSettle(w);
            Check("learn popout: leaving Keyboard mode closes it and the view returns", !open() && !pane.IsKeysDetached && pane.Children.Contains(view));

            mode.Enter(); BandDockSettle(w);
            if (!open()) toggle();
            Check("learn popout: opens again after a close", open() && ReferenceEquals(w.Learn, controller));
            typeof(MainWindow).GetMethod("ReleaseWindowResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(w, null);
            Check("learn popout: closing the main window closes it and releases the view", !open() && pane.Children.Contains(view));

            Check("learn popout: the hotkey, menu row and command exist", HotkeyCatalog.All.Any(a => a.Id == KeyboardModePopoutFeatureModule.CommandId && a.DefaultGesture.Length > 0));
        });
    }
}
