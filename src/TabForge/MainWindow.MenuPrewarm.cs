using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabForge.Views;

namespace TabForge;

// MainWindow, context-menu prewarm: once at idle after the first song opens, builds and discards one hidden menu with every
// item kind (plain, checkable, radio, sub-menu) so the styles and templates are loaded before the first right-click.
// Does not own the menus themselves (ScoreMenus, TrackRowMenus, timeline menus).
public partial class MainWindow
{
    private bool _menusPrewarmed;

    private void SchedulePrewarmMenus() =>
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (_menusPrewarmed) return;
            _menusPrewarmed = true;
            var specs = new List<MenuSpec>
            {
                new() { Header = "Item", Shortcut = "Shortcut" },
                new() { Header = "Checked", Checkable = true, Checked = true },
                new() { Header = "Radio", Checkable = true, Radio = true },
                new() { Header = "Menu", Children = new List<MenuSpec> { new() { Header = "Child" } } },
            };
            var menu = NewSpecMenu("prewarm", specs, _ => { }, Arrangement);
            menu.ApplyTemplate();
            foreach (var item in menu.Items.OfType<MenuItem>()) { item.ApplyTemplate(); item.Measure(new Size(300, 100)); }
            // The clip and track-row menus are built once too (never shown), so their first right-click skips the cold build.
            var clipMenu = NewTimelineMenu("prewarm", TimelineMenus.Clip(new ClipMenuState(true, false, false, false), MenuKey), _ => { });
            var rowMenu = _project.Tracks.Count > 0 ? TrackRowMenu(0) : null;
            foreach (var m in new[] { clipMenu, rowMenu })
                if (m is not null) foreach (var item in m.Items.OfType<MenuItem>()) { item.ApplyTemplate(); item.Measure(new Size(300, 100)); }
        });
}
