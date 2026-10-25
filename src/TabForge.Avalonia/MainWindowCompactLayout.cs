using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;

namespace TabForge.Avalonia;

// Owns: the window parts that compact mode and the Band view both hide and fit: the window's menu row (hidden while either mode is on, its toolbar icons held in the strip) and the
//   Arrangement pane's compact content, whose pane is fitted to its content height once the timeline first lays out.
// Does not own: the modes and what each one restores (MainWindowCompactMode, MainWindowBandChrome), the layout maths (DockWorkspaceController).
// Tests: TabForge.Avalonia.Tests.CompactBandHeaderTests.
public static class MainWindowCompactLayout
{
    /// <summary>The dock panel holding the track list and the arrangement.</summary>
    private const string ArrangementPanelId = "timeline";

    /// <summary>The name MainWindowMenu gives the menu row's border.</summary>
    private const string MenuRowName = "MainMenuRow";

    /// <summary>
    /// Shows or hides the window's menu row (built by MainWindowMenu). Hidden, the whole row leaves the layout, so the strip sits right under the
    /// tab bar, and the row's toolbar icons (MenuRowToggles) move into the strip before its close button; shown, they go back to the row.
    /// </summary>
    public static void SetMenuRowVisible(Window window, CompactTrackStrip strip, bool visible)
    {
        if (window.GetLogicalDescendants().OfType<Border>().FirstOrDefault(b => b.Name == MenuRowName) is not { Child: DockPanel row } menuRow)
            throw new System.InvalidOperationException($"The window has no {MenuRowName}.");
        menuRow.IsVisible = visible;
        if (visible)
        {
            if (strip.ReleaseToolbar() is { } toolbar) row.Children.Insert(0, toolbar);
            return;
        }
        if (strip.Toolbar is not null) return;
        if (row.Children.OfType<Control>().FirstOrDefault(c => c.Name == MenuRowToggles.PanelName) is not { } toggles) return;
        row.Children.Remove(toggles);
        strip.HoldToolbar(toggles);
    }

    /// Fits the timeline pane to the arrangement's compact content: the transport bar plus its compact rows. The transport bar is measured by the
    /// layout pass, not yet by the call, so the fit waits for the timeline's first layout after compact content turns on, then detaches.
    /// A mode swap shows new content, so a splitter the user dragged is replaced, as a song shown is.
    public static void FitTimelinePaneOnNextLayout(DockWorkspaceController dock, ArrangementView arrangement)
    {
        void FitOnce(object? sender, System.EventArgs e)
        {
            arrangement.Timeline.LayoutUpdated -= FitOnce;
            dock.FitPanelHeight(ArrangementPanelId, arrangement.NaturalHeight, replaceUserSplit: true);
        }
        arrangement.Timeline.LayoutUpdated += FitOnce;
    }
}
