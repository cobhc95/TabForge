using System.Windows;
using System.Windows.Controls;
using TabForge.Docking;
using TabForge.Views;

namespace TabForge;

/// <summary>Starting the app (auto-fitting the track list to a window of another size) must not rewrite the user's saved dock ratios.</summary>
public static partial class SelfTest
{
    private static void TestDockRatioNotRewrittenByAutoFit()
    {
        var owner = new Window { Width = 900, Height = 520 };
        var workspace = new DockWorkspace(owner);
        workspace.SetEditorContent(new Grid());
        foreach (var (id, host, anchor) in new[]
        {
            ("instrument", "instrument", "score-editor"), ("timeline", "timeline", "score-editor"),
            ("tools", "tools", "structure"), ("structure", "tools", "tools"),
            ("rhythm", "tools", "tools"), ("layout", "tools", "tools"),
            ("sections", "side", "practice"), ("practice", "side", "sections"),
            ("playback", "side", "sections")
        })
            workspace.RegisterPanel(id, id, new Border(), 180, 100, host, anchor);
        workspace.RestoreLayout(null);

        static DockNodeState? SplitAbove(DockNodeState? node, string panelId)
        {
            if (node is null) return null;
            if (node.Kind == "split" && node.Orientation == "Vertical" && node.Second is { Kind: "tabs" } s && s.Panels.Contains(panelId)) return node;
            return SplitAbove(node.First, panelId) ?? SplitAbove(node.Second, panelId);
        }

        var saved = workspace.CaptureLayout();
        var node = SplitAbove(saved.Root, "timeline");
        Check("dock ratio: the default layout has a split above the timeline", node is not null);
        if (node is null) { owner.Close(); return; }
        node.Ratio = 0.774;
        var changes = 0;
        workspace.LayoutChanged += (_, _) => changes++;
        workspace.RestoreLayout(saved);
        changes = 0;

        // Laid out by hand (no window is shown on the desktop).
        workspace.Measure(new Size(900, 500));
        workspace.Arrange(new Rect(0, 0, 900, 500));
        workspace.UpdateLayout();
        // A small window: the fit has to clamp, so the live ratio moves away from 0.774.
        var fitted = workspace.FitPanelHeight("timeline", 150);
        var after = SplitAbove(workspace.CaptureLayout().Root, "timeline");
        Check("dock ratio: fitting the track list to the window leaves the saved ratio at the user's 0.774",
            fitted && after is not null && Math.Abs(after.Ratio - 0.774) < 1e-9, after?.Ratio.ToString("0.0000"));
        Check("dock ratio: the auto-fit is not a layout change that gets saved", changes == 0, changes.ToString());
        Check("dock ratio: saving twice stays at the user's ratio", Math.Abs(SplitAbove(workspace.CaptureLayout().Root, "timeline")!.Ratio - 0.774) < 1e-9);
        owner.Close();
    }
}
