using System.Windows;
using TabForge.Views.Fx;

namespace TabForge;

/// <summary>The FX window grows to a docked plug-in editor, stops at the work area, and scrolls what still does not fit.</summary>
public static partial class SelfTest
{
    private static void TestPluginEditorFit()
    {
        var work = new Rect(0, 0, 1920, 1040);
        var chrome = new Size(300, 120);

        // 150%: a 1560 x 1100 pixel editor is 1040 x 733.33 DIPs.
        var dip = PluginEditorFit.ToDip(1560, 1100, 1.5, 1.5);
        Check("editor fit: pixels become DIPs at 150%", Math.Abs(dip.Width - 1040) < 1e-9 && Math.Abs(dip.Height - 1100 / 1.5) < 1e-9, dip.ToString());

        var grown = PluginEditorFit.FitWindow(new Rect(100, 80, 980, 620), chrome, dip, work);
        Check("editor fit: the window grows to list + editor, top-left kept", grown == new Rect(100, 80, 1340, 120 + 1100 / 1.5), grown.ToString());

        // 100%: the editor is taller than the work area and the grown window would leave it on the right.
        var editor = PluginEditorFit.ToDip(1560, 1100, 1, 1);
        var clamped = PluginEditorFit.FitWindow(new Rect(400, 200, 980, 620), chrome, editor, work);
        Check("editor fit: the size stops at the work area less the margin", clamped.Width == 1860 && clamped.Height == 1000, clamped.ToString());
        Check("editor fit: a window that would leave the screen is moved in", clamped.Left == 60 && clamped.Top == 40 && clamped.Right <= work.Right && clamped.Bottom <= work.Bottom, clamped.ToString());

        var large = new Rect(10, 10, 1900, 1030);
        Check("editor fit: a window the user made larger is not shrunk or moved", PluginEditorFit.FitWindow(large, chrome, new Size(400, 300), work) == large);

        var fits = PluginEditorFit.Scroll(1600, 1200, 1560, 1100, 17);
        Check("editor fit: no bars when the editor fits", fits == new PluginEditorFit.ScrollLayout(1560, 1100, 0, 0), fits.ToString());

        // Too tall only: the vertical bar takes 17 of the width, which still leaves room for the editor's width.
        var tall = PluginEditorFit.Scroll(1600, 880, 1560, 1100, 17);
        Check("editor fit: the vertical range is the overflow", tall == new PluginEditorFit.ScrollLayout(1560, 880, 0, 220), tall.ToString());

        // Too tall, and the vertical bar makes it too wide as well: both bars, each range the overflow past the other bar.
        var both = PluginEditorFit.Scroll(1570, 880, 1560, 1100, 17);
        Check("editor fit: a bar that hides part of the editor brings the other bar", both == new PluginEditorFit.ScrollLayout(1553, 863, 7, 237), both.ToString());

        Check("editor fit: the scroll offset moves the plug-in's view the other way, in pixels", PluginEditorFit.ViewOrigin(7, 237, 1.5, 1.5) == (-10, -356) && PluginEditorFit.ViewOrigin(0, 0, 1.5, 1.5) == (0, 0));
    }
}
