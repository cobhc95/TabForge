using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the score editor's pure page arithmetic: system, staff and TAB tops, the vertical and horizontal scroll offsets for a bar,
//   the bar's cell slots, the in-bar cell clamp, the selection range test, the bar under a page x, the visible systems and the selection index clamps. Plain doubles and ints, no allocation, the same operation order as the view.
// Does not own: the metrics it is given (header, system height, staff margin; TabEditorControl.cs), the layout (ScoreLayoutEngine)
//   and snapping a cell past the bar end (TabEditorControl.Snap).
// Tests: no dedicated test class.
public static class ScoreEditorGeometry
{
    /// <summary>Cell slots of a bar; 16 when there is no song.</summary>
    public static int SlotsFor(SongProject? project, int measure) => project is null ? 16 : MusicTime.BarSlots(project, measure);

    public static double SystemTop(double header, int system, double systemHeight) => header + system * systemHeight;

    public static double StaffTop(double header, int system, double systemHeight, double staffMarginTop)
        => header + system * systemHeight + staffMarginTop;

    public static double TabTop(double staffTop, double staffToTab) => staffTop + staffToTab;

    /// <summary>Horizontal scroll that shows a bar whose left edge is at x, with a little lead-in.</summary>
    public static double HorizontalOffset(double measureX, double zoom) => Math.Max(0, measureX - 60) * zoom;

    /// <summary>True when (measure, cell) lies inside the inclusive range from (m1, c1) to (m2, c2).</summary>
    public static bool IsInRange(int measure, int cell, int m1, int c1, int m2, int c2)
    {
        if (measure < m1 || measure > m2) return false;
        if (measure == m1 && cell < c1) return false;
        if (measure == m2 && cell > c2) return false;
        return true;
    }

    /// <summary>The bar whose [X, X + Width) holds x; the system's last bar when none does. The list must not be empty.</summary>
    public static int BarAt(IReadOnlyList<(double X, double Width, int MeasureIndex)> measures, double x)
    {
        var hit = measures.FirstOrDefault(m => x >= m.X && x < m.X + m.Width);
        return hit.Width > 0 ? hit.MeasureIndex : measures[^1].MeasureIndex;
    }

    /// <summary>An index clamped into [0, count - 1]; 0 when count is 0.</summary>
    public static int ClampIndex(int index, int count) => Math.Clamp(index, 0, Math.Max(0, count - 1));

}
