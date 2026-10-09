using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;

namespace TabForge.Views;

/// <summary>
/// standard technique engraving for the TAB staff: bend curves, whammy diagrams, tremolo slashes,
/// trills, grace frets, wah, pick strokes, brush/arpeggio arrows and let-ring spans. Everything here is
/// drawn into the per-system cached drawing, so none of it runs per frame.
/// </summary>
public sealed partial class TabEditorControl
{
    // ---------------- pure helpers (unit-tested) ----------------

    // ---------------- layout audit hooks (read-only; nothing is recorded while drawing) ----------------

    /// <summary>Renders once off-screen and returns the cached per-system drawings for the layout audit.</summary>
    internal IReadOnlyList<(int System, Drawing Drawing)> AuditSystemDrawings()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) OnRender(dc);
        return _systemCache.Snapshot();
    }

    // ---- per-bar audit hooks (Diagnostics/BarAudit.cs; read-only) ----

    /// <summary>Vertical bands of one system, for cropping and bounds checks.</summary>
    internal readonly record struct AuditSystemBox(double SystemTop, double StaffTop, double StaffBottom, double TabTop, double TabBottom, double SystemBottom, double PageWidth, double StringGapPx);

    internal AuditSystemBox AuditSystemMetrics(int system)
    {
        var strings = Math.Max(1, Track?.StringTunings.Count ?? 6);
        var staffTop = StaffTop(system);
        var tabTop = TabTop(system);
        return new AuditSystemBox(SystemTop(system), staffTop, staffTop + 4 * StaffGap, tabTop, tabTop + (strings - 1) * StringGap, SystemTop(system) + SystemHeight, PageWidth, StringGap);
    }

    internal ScorePageLayout AuditLayout() => Layout.GetLayout(Track);

    /// <summary>Text size factor (the score text size setting over its 12 pt reference).</summary>
    internal static double AuditTextScale => ScoreText.TextSize / 12.0;

    internal static bool AuditIsGeometryTechnique(string technique) => ScoreMarkText.GeometryTechniques.Contains(technique);

    internal static string AuditShortTechnique(string technique) => ScoreMarkText.ShortTechnique(technique);

    /// <summary>The engraved beats of a bar (both voices) after a render: where each cell's centre landed.</summary>
    internal IReadOnlyList<(int Voice, StaffNotationBeat Beat)> AuditBeats(int measure)
    {
        var result = new List<(int, StaffNotationBeat)>();
        if (measure < 0 || measure >= _layout.StaffLayoutBars) return result;
        for (var voice = 0; voice < 2; voice++)
            if (_layout.CachedStaffLayout(measure, voice) is { } layout)
                foreach (var beat in layout.Beats) result.Add((voice, beat));
        return result;
    }

    /// <summary>The bar under page x in a system (for audit reports).</summary>
    internal int AuditBarAt(int system, double x)
    {
        var track = Track;
        if (track is null) return 0;
        var layout = Layout.GetLayout(track);
        if (system < 0 || system >= layout.SystemCount) return 0;
        var measures = layout.Systems[system].Measures;
        var hit = measures.FirstOrDefault(m => x >= m.X && x < m.X + m.Width);
        return hit.Width > 0 ? hit.MeasureIndex : measures[^1].MeasureIndex;
    }

    // ---------------- drawing ----------------


}
