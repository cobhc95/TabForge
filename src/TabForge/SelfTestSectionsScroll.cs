using System.Windows;
using System.Windows.Controls;

namespace TabForge;

/// <summary>The Sections pane scrolls instead of clipping its buttons when the pane is shorter than its content (large UI scale).</summary>
public static partial class SelfTest
{
    private static void TestSectionsPaneScrollsWhenShort()
    {
        var window = NewLifetimeWindow();
        try
        {
            if (window.SectionsPanelContent is not ScrollViewer sv) { Check("sections pane: is a scroll viewer", false); return; }
            // The pane's cell is capped (a short pane is what UI scale 1.5 on a laptop screen gives), then released.
            sv.MaxHeight = 120; window.UpdateLayout(); window.UpdateLayout();
            Check("sections pane: a short pane scrolls", sv.ScrollableHeight > 0, $"{sv.ScrollableHeight:0} (viewport {sv.ViewportHeight:0}, extent {sv.ExtentHeight:0})");
            sv.MaxHeight = 700; window.UpdateLayout(); window.UpdateLayout();
            Check("sections pane: a tall pane does not scroll", sv.ScrollableHeight < 0.5, $"{sv.ScrollableHeight:0} (viewport {sv.ViewportHeight:0}, extent {sv.ExtentHeight:0})");
        }
        finally { window.Close(); }
    }
}
