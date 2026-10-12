using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Docking;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// The Band view is never docked into a normal layout: the Panels menu entry switches to the Band layout and back (as View > Band view),
/// a new tab while it is open keeps the Band layout, and settings saved in the Band layout come back as that layout after a restart.
/// </summary>
public static partial class SelfTest
{
    private static void TestBandNeverDocked() => RunInWindowFixture((w, context) =>
    {
        w.Width = 1400; w.Height = 900;
        BandDockSettle(w);
        var dock = VisualDescendants<DockWorkspace>(w).First();
        var settings = context.Store.Settings;
        bool InBandLayout() => dock.IsPanelVisible("band") && !HasEditorNode(dock.CaptureLayout().Root) && settings.LastLayout == "Band";
        bool InScoreLayout() => !dock.IsPanelVisible("band") && HasEditorNode(dock.CaptureLayout().Root);
        Check("band dock: the default layout has no Band view", InScoreLayout());
        var panelsBefore = string.Join(",", DockWorkspace.PanelsOf(dock.CaptureLayout()).OrderBy(x => x));

        var item = w.DockPanelsMenu.Items.OfType<MenuItem>().First(m => m.Tag as string == "band");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        BandDockSettle(w);
        Check("band dock: Panels > Band view switches to the Band layout, not under the timeline", InBandLayout(), BandDockLayout(dock));
        var bandTabs = VisualDescendants<TabForge.Docking.DockTabSurface>(w).Where(t => t.PanelId == "band").ToList();
        var view = w.Band.View;
        double MidY(FrameworkElement e) => e.TranslatePoint(new Point(0, e.ActualHeight / 2), view).Y;
        var floated = dock.CaptureLayout();
        floated.Root = TabForge.Docking.DockLayoutTree.Split("Horizontal", 0.5, DockWorkspace.EditorNode(), floated.Root!);
        TabForge.Docking.DockLayoutTree.DropPanels(floated, _ => true);
        Check("band dock: the pane has no tab, no panel menu and no drag; its BAND caption and the first capsule share one row; a layout holding it beside the score has it closed",
            bandTabs.All(t => !t.IsVisible && t.ContextMenu is null) && TabForge.Docking.DockHostView.IsFixedPanel("band") && view.Caption.IsVisible && view.Caption.Text == "BAND VIEW" &&
            view.Pills.Count > 0 && Math.Abs(MidY(view.Caption) - MidY(view.Pills[0])) <= 3 && view.Caption.TranslatePoint(new Point(view.Caption.ActualWidth, 0), view).X <= view.Pills[0].TranslatePoint(new Point(0, 0), view).X &&
            !DockWorkspace.PanelsOf(floated).Contains("band") && floated.ClosedPanels.Contains("band"),
            $"tabs {bandTabs.Count(t => t.IsVisible)} visible, pills {view.Pills.Count}, menus {bandTabs.Count(t => t.ContextMenu is not null)}, closed {floated.ClosedPanels.Contains("band")}, held {DockWorkspace.PanelsOf(floated).Contains("band")}, caption right {view.Caption.TranslatePoint(new Point(view.Caption.ActualWidth, 0), view).X:0}, pill left {view.Pills[0].TranslatePoint(new Point(0, 0), view).X:0}, caption y {MidY(view.Caption):0.#}, pill y {(view.Pills.Count > 0 ? MidY(view.Pills[0]) : -1):0.#}");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        BandDockSettle(w);
        Check("band dock: Panels > Band view again goes back to the score layout", InScoreLayout(), BandDockLayout(dock));
        var panelsAfter = string.Join(",", DockWorkspace.PanelsOf(dock.CaptureLayout()).OrderBy(x => x));
        Check("band dock: leaving the Band view brings back the same panels as before (not a rebuilt named layout)", panelsAfter == panelsBefore, $"{panelsBefore} -> {panelsAfter}");

        BandDockCommand(w, "View.BandView");
        BandDockSettle(w);
        typeof(MainWindow).GetMethod("NewTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(w, null);
        BandDockSettle(w);
        Check("band dock: a new tab while the Band view is open keeps the Band layout", InBandLayout() && w.OpenDocuments.Count == 2, BandDockLayout(dock));

        var file = Path.Combine(Path.GetTempPath(), "tf-band-dock-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        AppSettings saved;
        try { SettingsFileService.SaveAtomic(file, settings); saved = SettingsFileService.Load(file); }
        finally { try { File.Delete(file); } catch (IOException) { } }
        Check("band dock: settings saved in the Band layout name it and hold no score",
            saved.LastLayout == "Band" && saved.Workspace is { } ws && DockWorkspace.PanelsOf(ws).Contains("band") && !HasEditorNode(ws.Root));

        BandDockCommand(w, "View.BandView");
        BandDockSettle(w);
        Check("band dock: leaving the Band layout with a second tab open brings the score back", InScoreLayout(), BandDockLayout(dock));

        settings.LastLayout = saved.LastLayout;   // a restart: the saved workspace comes back
        dock.ApplyLayout(saved.Workspace!);
        BandDockSettle(w);
        Check("band dock: after a restart the Band view is the Band layout", InBandLayout(), BandDockLayout(dock));
        BandDockCommand(w, "View.BandView");
        BandDockSettle(w);
        Check("band dock: after a restart View > Band view still goes back to a score layout", InScoreLayout(), BandDockLayout(dock));
        foreach (var d in w.OpenDocuments.ToList()) d.MarkClean();
    });

    private static void BandDockSettle(MainWindow w)
    {
        for (var i = 0; i < 3; i++) { w.UpdateLayout(); PumpUi(); }
    }

    private static void BandDockCommand(MainWindow w, string id) =>
        typeof(MainWindow).GetMethod("RunHotkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(w, new object[] { id });

    private static string BandDockLayout(DockWorkspace dock) => string.Join(",", DockWorkspace.PanelsOf(dock.CaptureLayout())) + (HasEditorNode(dock.CaptureLayout().Root) ? ",score" : "");
}
