using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Audio;
using TabForge.Docking;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// The default workspace in a real off-screen window: the Sections pane is shown with a size, the fretboard pane sits above the score
/// (first window, a second window), a saved layout naming removed panels keeps the rest, the Band toolbar button toggles the Band layout,
/// and the fretboard position (Top / Bottom) moves the pane, comes from the menu and the command, and is saved.
/// </summary>
public static partial class SelfTest
{
    private static void TestDockDefaultsAndFretboardPosition() => RunInWindowFixture((a, context) =>
    {
        var second = new MainWindow(AudioEngineClient.Instance, new Shell.AppOptions());
        try
        {
            ShowTestWindow(second);
            foreach (var (w, label) in new[] { (a, "first window"), (second, "new window") })
            {
                w.Width = 1400; w.Height = 900;
                w.UpdateLayout(); PumpUi(); w.UpdateLayout(); PumpUi();
                var dock = VisualDescendants<DockWorkspace>(w).First();
                Check($"dock defaults ({label}): Sections is a visible pane", dock.IsPanelVisible("sections"));
                Check($"dock defaults ({label}): Sections content is loaded and has a size",
                    w.SectionsPanelContent.IsLoaded && w.SectionsPanelContent.ActualWidth > 20 && w.SectionsPanelContent.ActualHeight > 20,
                    $"{w.SectionsPanelContent.ActualWidth:0}x{w.SectionsPanelContent.ActualHeight:0}");
                Check($"dock defaults ({label}): the fretboard pane is above the score", InstrumentAbove(w));
            }

            var win = a;
            var layout = VisualDescendants<DockWorkspace>(win).First();
            var old = layout.CaptureLayout();
            DockLayoutTree.FindPanelHost(old.Root, "sections")!.Panels.AddRange(new[] { "practice", "playback" });
            layout.RestoreLayout(old);
            win.UpdateLayout(); PumpUi();
            Check("dock defaults: a saved layout naming removed panels keeps Sections and the fretboard", layout.IsPanelVisible("sections") && layout.IsPanelVisible("instrument") && !layout.IsPanelVisible("practice"));
            Check("dock defaults: ...and Sections still has a size", win.SectionsPanelContent.ActualHeight > 20);

            var band = VisualDescendants<Button>(win).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Show or hide the Band view");
            Check("band button: it is in the toolbar with a tooltip", band.IsVisible && band.ToolTip is not null);
            band.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("band button: a click enters the Band layout", layout.IsPanelVisible("band"));
            band.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            win.UpdateLayout(); PumpUi(); win.UpdateLayout(); PumpUi();
            Check("band button: a second click leaves it", !layout.IsPanelVisible("band") && layout.IsPanelVisible("instrument"));

            Check("fretboard position: Top by default", !context.Store.Settings.Appearance.FretboardAtBottom);
            Check("fretboard position: the menu offers Top and Bottom", HasPositionChoices());
            Check("fretboard position: the command is bindable", HotkeyCatalog.All.Any(h => h.Id == "View.FretboardPosition"));
            typeof(MainWindow).GetMethod("RunHotkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(win, new object[] { "View.FretboardPosition" });
            win.UpdateLayout(); PumpUi(); win.UpdateLayout(); PumpUi();
            Check("fretboard position: the command moves the pane below the score", context.Store.Settings.Appearance.FretboardAtBottom && !InstrumentAbove(win));
            Check("fretboard position: ...above the timeline", InstrumentAboveTimeline(win));
            var file = Path.Combine(Path.GetTempPath(), "tf-fretpos-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            try
            {
                SettingsFileService.SaveAtomic(file, context.Store.Settings);
                Check("fretboard position: Bottom is saved", SettingsFileService.Load(file).Appearance.FretboardAtBottom);
            }
            finally { try { File.Delete(file); } catch (IOException) { } }
            layout.SetInstrumentPosition(false); win.UpdateLayout(); PumpUi(); win.UpdateLayout(); PumpUi();
            Check("fretboard position: Top moves it back above the score", InstrumentAbove(win));
            context.Store.Settings.Appearance.FretboardAtBottom = false;
        }
        finally { foreach (var s in second.OpenDocuments.ToList()) s.MarkClean(); second.Close(); }
    });

    private static bool InstrumentAbove(MainWindow w) =>
        w.InstrumentHost.TranslatePoint(new Point(0, 0), w).Y < w.Editor.TranslatePoint(new Point(0, 0), w).Y;

    private static bool InstrumentAboveTimeline(MainWindow w) =>
        w.InstrumentHost.TranslatePoint(new Point(0, 0), w).Y > w.Editor.TranslatePoint(new Point(0, 0), w).Y &&
        w.InstrumentHost.TranslatePoint(new Point(0, 0), w).Y < w.ArrangementHost.TranslatePoint(new Point(0, 0), w).Y;

    private static bool HasPositionChoices()
    {
        var state = new InstrumentMenuState(false, false, "Guitar", "Fretboard", new[] { "Fretboard" }, new[] { "C" }, new[] { "Major" }, null, false, false, false, false, true);
        var position = InstrumentMenus.Build(state, _ => "").FirstOrDefault(m => m.Header == InstrumentMenus.Position);
        return position?.Children is { Count: 2 } c && !c[0].Checked && c[1].Checked && c.All(x => x.Id == InstrumentMenus.PositionId);
    }
}
