using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

/// <summary>
/// Dynamics markings (ppp..fff) engraved as bold italic letters under the staff (under the TAB when the
/// notation staff is hidden). A marking appears on the first note of the track and then only where the dynamic
/// changes. The marks are computed once per score generation (with the palm-mute passages), never per frame,
/// and they take part in the bar's width and in the palm-mute lane stacking so nothing overlaps.
/// </summary>
public sealed partial class TabEditorControl
{
    private bool _showDynamics = true;
    private Dictionary<TabCell, string> _dynamicMarks = new(ReferenceEqualityComparer.Instance);

    /// <summary>Engrave dynamics markings (Preferences &gt; Score &gt; Labels).</summary>
    public bool ShowDynamics
    {
        get => _showDynamics;
        set
        {
            if (_showDynamics == value) return;
            _showDynamics = value;
            InvalidateScoreLayout();
        }
    }

    private const double DynamicFontSize = 13;
    private const double DynamicHeight = 15;

    /// <summary>The beats that carry a dynamics marking: the first note of the track, then every change of dynamic.</summary>
    internal static Dictionary<TabCell, string> BuildDynamicMarks(TrackModel track)
    {
        var marks = new Dictionary<TabCell, string>(ReferenceEqualityComparer.Instance);
        var previous = -1;
        foreach (var measure in track.Measures)
            foreach (var cell in measure.Cells)
            {
                var principal = cell.Notes.FirstOrDefault(n => !n.IsGraceNote);
                if (principal is null) continue;
                var dynamic = Dynamics.NearestIndex(principal.Velocity);
                if (dynamic == previous) continue;
                previous = dynamic;
                marks[cell] = Dynamics.Names[dynamic];
            }
        return marks;
    }

    private static readonly Dictionary<(string Name, uint Colour, int Dpi), FormattedText> DynamicTextCache = new();

    /// <summary>Bold italic serif letters, the usual engraving of a dynamic.</summary>
    private static FormattedText DynamicText(string name, Color colour)
    {
        var dpi = (int)Math.Round(RenderDraw.PixelsPerDip * 1000);
        var key = (name, ColourKey(colour), dpi);
        if (DynamicTextCache.TryGetValue(key, out var cached)) return cached;
        var created = new FormattedText(name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            TypefaceFor("Times New Roman", FontWeights.Bold, true), DynamicFontSize, Brush(colour), RenderDraw.PixelsPerDip);
        if (DynamicTextCache.Count >= 256) DynamicTextCache.Clear();
        DynamicTextCache[key] = created;
        return created;
    }

    private bool BarHasDynamic(MeasureModel measure)
    {
        if (_dynamicMarks.Count == 0) return false;
        foreach (var cell in measure.Cells)
            if (_dynamicMarks.ContainsKey(cell)) return true;
        return false;
    }

    /// <summary>
    /// Top of the dynamics row under the staff of one bar: 8 px under the staff, lower when a note, ledger line or
    /// stem of the bar reaches further down (the same extents the palm-mute lane clears).
    /// </summary>
    private double StaffDynamicTop(int measureIndex, double staffBottom)
    {
        var lowest = staffBottom;
        if (_staffLayoutCache is not null && measureIndex >= 0 && measureIndex < _staffLayoutCache.GetLength(0))
            for (var v = 0; v < 2; v++)
                if (_staffLayoutCache[measureIndex, v] is { } cached)
                    foreach (var b in cached.Beats)
                    {
                        foreach (var n in b.Notes) lowest = Math.Max(lowest, n.Y + 4);
                        if (b.IsDrum) lowest = Math.Max(lowest, b.MaxY + 4);
                        if (b.HasStem) lowest = Math.Max(lowest, Math.Max(b.StemStartY, b.StemEndY) + 3);
                        if (b.LowerStemTopY is not null) lowest = Math.Max(lowest, b.LowerStemEndY + 3);
                    }
        return Math.Max(staffBottom + 8, lowest + 5);
    }

    /// <summary>Tab-only: the row sits under the TAB, below the fingering marks and the lyric lines of the bar.</summary>
    private double TabDynamicTop(MeasureModel measure, double tabBottom)
    {
        var extra = 0.0;
        void Scan(IReadOnlyList<TabCell> cells)
        {
            foreach (var cell in cells)
            {
                var lyricRows = string.IsNullOrWhiteSpace(cell.Lyrics) ? 0 : Math.Min(cell.Lyrics.Split('\n').Length, 3);
                extra = Math.Max(extra, FingeringHeight(cell) + lyricRows * 12);
            }
        }
        Scan(measure.Cells);
        if (Voice2HasContent(measure)) Scan(measure.Voice2Cells);
        return tabBottom + 13 + extra + 3;
    }

    private void DrawDynamics(DrawingContext dc, TrackModel track, MeasureModel measure, int measureIndex,
        StaffNotationMeasureLayout layout, double staffTop, double tabTop, int strings, Color ink, bool showStaff)
    {
        if (_dynamicMarks.Count == 0) return;
        double? top = null;
        foreach (var beat in layout.Beats)
        {
            if (!_dynamicMarks.TryGetValue(beat.Cell, out var name)) continue;
            top ??= showStaff ? StaffDynamicTop(measureIndex, staffTop + 4 * StaffGap)
                              : TabDynamicTop(measure, tabTop + (strings - 1) * StringGap);
            var text = DynamicText(name, ink);
            RenderDraw.DrawText(dc, text, new Point(beat.CenterX - text.Width / 2, top.Value));
        }
    }
}
