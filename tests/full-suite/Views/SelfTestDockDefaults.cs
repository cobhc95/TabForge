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
            Check("dock defaults: View and tempo is docked below Sections in the right column", ViewTempoBelowSections(win, layout));
            var before = layout.CaptureLayout();
            DockLayoutTree.DropPanels(before, id => id != DockPaneTable.ViewTempoId);
            layout.RestoreLayout(before);
            win.UpdateLayout(); PumpUi(); win.UpdateLayout(); PumpUi();
            Check("dock defaults: a saved layout from before the View and tempo pane shows it below Sections", ViewTempoBelowSections(win, layout));
            Check("dock defaults: Lyrics is a tab after Sections, with Sections selected", LyricsTabAfterSections(layout));
            var noLyrics = layout.CaptureLayout();
            DockLayoutTree.DropPanels(noLyrics, id => id != "lyrics");
            layout.RestoreLayout(noLyrics);
            win.UpdateLayout(); PumpUi();
            Check("dock defaults: a saved layout from before the Lyrics pane gets it as a tab after Sections, not selected", LyricsTabAfterSections(layout) && win.SectionsPanelContent.IsVisible);

            var band = VisualDescendants<Button>(win).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Show or hide the Band view");
            Check("band button: it is in the toolbar with a tooltip", band.IsVisible && band.ToolTip is not null);
            band.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("band button: a click enters the Band layout", layout.IsPanelVisible("band"));
            band.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            win.UpdateLayout(); PumpUi(); win.UpdateLayout(); PumpUi();
            Check("band button: a second click leaves it", !layout.IsPanelVisible("band") && layout.IsPanelVisible("instrument"));

            Check("fretboard position: Top by default", !context.Store.Settings.Appearance.FretboardAtBottom);
            FretboardSideSurvivesModes(win, layout, bottom: false);
            Check("fretboard position: the menu offers Top and Bottom", HasPositionChoices());
            Check("fretboard position: the command is bindable", HotkeyCatalog.All.Any(h => h.Id == "View.FretboardPosition"));
            typeof(MainWindow).GetMethod("RunHotkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(win, new object[] { "View.FretboardPosition" });
            win.UpdateLayout(); PumpUi(); win.UpdateLayout(); PumpUi();
            Check("fretboard position: the command moves the pane below the score", context.Store.Settings.Appearance.FretboardAtBottom && !InstrumentAbove(win));
            Check("fretboard position: ...above the timeline", InstrumentAboveTimeline(win));
            FretboardSideSurvivesModes(win, layout, bottom: true);
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

    /// <summary>Entering and leaving the Band layout and Keyboard mode, alone, nested and repeated, and switching to a built-in layout, leave the fretboard on the setting's side.</summary>
    private static void FretboardSideSurvivesModes(MainWindow w, DockWorkspace dock, bool bottom)
    {
        var start = dock.CaptureLayout();
        void Band() => BandDockCommand(w, "View.BandView");
        void Keys() => w.KeyboardMode.Toggle();
        void Switch(string layout) => ((DockLayoutController)typeof(MainWindow).GetProperty("WorkspaceLayouts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!).SwitchLayout(layout);
        var rounds = new (string Name, Action[] Steps)[]
        {
            ("Band twice", new Action[] { Band, Band, Band, Band }),
            ("Keyboard mode twice", new Action[] { Keys, Keys, Keys, Keys }),
            ("Band, then Keyboard mode inside it", new Action[] { Band, Keys, Keys, Band }),
            ("Keyboard mode, then Band", new Action[] { Keys, Band, Band }),
            ("Band from the Layouts menu, left with the button", new Action[] { () => Switch("Band"), Band }),
            ("the Compose layout", new Action[] { () => Switch("Compose") }),
            ("the Practice layout", new Action[] { () => Switch("Practice") }),
            ("fretboard closed in Keyboard mode, reopened after", new Action[] { Keys, () => dock.SetPanelVisible("instrument", false), Keys, () => dock.SetPanelVisible("instrument", true) }),
        };
        foreach (var (round, steps) in rounds)
        {
            foreach (var step in steps) { step(); BandDockSettle(w); }
            if (w.KeyboardMode.IsOn) Keys();
            if (dock.IsPanelVisible("band")) Band();
            BandDockSettle(w);
            Check($"fretboard side ({(bottom ? "bottom" : "top")}): {round} leaves the fretboard {(bottom ? "below" : "above")} the score",
                dock.IsPanelVisible("instrument") && InstrumentAbove(w) != bottom, BandDockLayout(dock));
        }
        dock.ApplyLayout(start);
        BandDockSettle(w);
    }

    private static bool LyricsTabAfterSections(DockWorkspace dock)
    {
        var host = DockLayoutTree.FindPanelHost(dock.CaptureLayout().Root, "sections");
        return host is not null && host.SelectedPanel == "sections" && host.Panels.IndexOf("lyrics") == host.Panels.IndexOf("sections") + 1;
    }

    private static bool ViewTempoBelowSections(MainWindow w, DockWorkspace dock)
    {
        var pane = w.ViewTempoPanelContent.TranslatePoint(new Point(0, 0), w);
        var sections = w.SectionsPanelContent.TranslatePoint(new Point(0, 0), w);
        return dock.IsPanelVisible(DockPaneTable.ViewTempoId) && w.TempoBox.IsVisible && w.ZoomCombo.IsVisible &&
            pane.Y > sections.Y && Math.Abs(pane.X - sections.X) < 2 && pane.X > w.Editor.TranslatePoint(new Point(0, 0), w).X;
    }

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
