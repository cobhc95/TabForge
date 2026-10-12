using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Visualization;

namespace TabForge.Views;

// Owns: the timeline's half of the master track lane: its colour and opacity, which bars each track's line fills, and drawing it.
// Does not own: the lane's rules (MasterLane.cs), the track list's half (ArrangementPanel.MasterLane.cs) and the per-bar note
//   activity it reads (ActivityOf in TrackTimeline.Render.cs).
// Tests: TestMasterLane.

internal sealed partial class TrackTimeline
{
    private Color? _masterLaneColour;
    private double _masterLaneOpacity = MasterLane.DefaultOpacityPercent / 100.0;

    /// <summary>The one flat colour of every line of the lane; null follows the theme's text colour.</summary>
    internal Color? MasterLaneColour
    {
        get => _masterLaneColour;
        set { if (_masterLaneColour == value) return; _masterLaneColour = value; InvalidateVisual(); }
    }

    private bool _masterLaneByTrack = true;
    // Frozen line brushes, rebuilt only when the mode, a track colour, the opacity, the uniform colour or the accent changes.
    private Brush[] _masterLaneBrushes = Array.Empty<Brush>();
    private string[] _masterLaneHexes = Array.Empty<string>();
    private bool _masterLaneBrushByTrack;
    private double _masterLaneBrushOpacity = -1;
    private Color _masterLaneBrushUniform, _masterLaneBrushAccent;

    /// <summary>Each line takes its track's colour (true); false gives every line <see cref="MasterLaneColour"/>.</summary>
    internal bool MasterLaneByTrack
    {
        get => _masterLaneByTrack;
        set { if (_masterLaneByTrack == value) return; _masterLaneByTrack = value; InvalidateVisual(); }
    }

    /// <summary>Opacity of the fill (0..1).</summary>
    internal double MasterLaneOpacity
    {
        get => _masterLaneOpacity;
        set { if (Math.Abs(_masterLaneOpacity - value) < 0.001) return; _masterLaneOpacity = value; InvalidateVisual(); }
    }

    /// <summary>Lines in the lane: one per track, 0 when the lane is hidden.</summary>
    internal int MasterLaneLineCount => MasterLane.IsShown(Project) ? Project!.Tracks.Count : 0;

    /// <summary>The track plays in the bar: it has notes there, or an audio or MIDI clip covers part of it.</summary>
    internal bool MasterLaneHasContent(int track, int bar)
    {
        if (Project is not { } project || track < 0 || track >= project.Tracks.Count || bar < 0 || bar >= BarCount) return false;
        var model = project.Tracks[track];
        if (bar < model.Measures.Count)
        {
            var measure = model.Measures[bar];
            if (measure.SimileOneBar || measure.SimileTwoBar || ActivityOf(track, bar, measure).HasContent) return true;
        }
        if (model.AudioClips.Count == 0) return false;
        var left = XOfBar(bar) + 0.5;
        var right = XOfBar(bar) + WidthOfBar(bar) - 0.5;
        for (var i = 0; i < model.AudioClips.Count; i++)
            if (XOfSec(model.AudioClips[i].EndSec) > left && XOfSec(model.AudioClips[i].StartSec) < right) return true;
        return false;
    }

    /// <summary>A track's block colour: the lookup the track's blocks are drawn with (TrackTimeline.Render.cs).</summary>
    internal Color TrackBlockColour(int track) => Parse(Project!.Tracks[track].ColorHex, _theme.Accent);

    /// <summary>The colour a line is drawn in (test probe).</summary>
    internal Color MasterLaneLineColour(int track) => _masterLaneByTrack ? TrackBlockColour(track) : (_masterLaneColour ?? _theme.Text);

    /// <summary>The frozen brushes the lines are drawn with: one per track, or one shared brush for a single colour.</summary>
    private Brush[] MasterLaneBrushes(SongProject project)
    {
        var uniform = _masterLaneColour ?? _theme.Text;
        var count = project.Tracks.Count;
        var valid = _masterLaneBrushByTrack == _masterLaneByTrack && _masterLaneBrushOpacity == _masterLaneOpacity
            && _masterLaneBrushAccent == _theme.Accent && _masterLaneBrushUniform == uniform;
        if (valid && _masterLaneByTrack)
        {
            valid = _masterLaneHexes.Length == count;
            for (var t = 0; valid && t < count; t++) valid = string.Equals(_masterLaneHexes[t], project.Tracks[t].ColorHex, StringComparison.Ordinal);
        }
        if (valid) return _masterLaneBrushes;
        _masterLaneBrushByTrack = _masterLaneByTrack; _masterLaneBrushOpacity = _masterLaneOpacity;
        _masterLaneBrushAccent = _theme.Accent; _masterLaneBrushUniform = uniform;
        if (_masterLaneByTrack)
        {
            _masterLaneHexes = new string[count];
            _masterLaneBrushes = new Brush[count];
            for (var t = 0; t < count; t++)
            {
                _masterLaneHexes[t] = project.Tracks[t].ColorHex;
                _masterLaneBrushes[t] = MasterLaneBrush(Parse(project.Tracks[t].ColorHex, _theme.Accent));
            }
        }
        else _masterLaneBrushes = new[] { MasterLaneBrush(uniform) };
        return _masterLaneBrushes;
    }

    private Brush MasterLaneBrush(Color colour)
    {
        var brush = Draw.Solid(colour, _masterLaneOpacity);
        brush.Freeze();
        return brush;
    }

    /// <summary>The bars a track's line fills (test probe).</summary>
    internal List<int> MasterLaneBars(int track)
    {
        var bars = new List<int>();
        for (var b = 0; b < BarCount; b++) if (MasterLaneHasContent(track, b)) bars.Add(b);
        return bars;
    }

    /// <summary>The hover cell under <paramref name="p"/> on the master strip: (bar, <see cref="MasterHoverTrack"/>), or (-1, -1).</summary>
    private (int Bar, int Track) MasterHoverCellAt(Point p, double inGrid)
    {
        if (Project is null || !MasterLane.IsShown(Project) || inGrid < 0 || inGrid >= MasterLane.OffsetOf(Project)) return (-1, -1);
        var bars = BarCount;
        if (bars == 0 || p.X >= XOfBar(bars)) return (-1, -1);
        return (BarAt(p.X), MasterHoverTrack);
    }

    /// <summary>The master strip's hover cell for a bar: the bar's column over the whole strip, inset like a track cell.</summary>
    private Rect? MasterCellBounds(int bar)
    {
        if (Project is not { } project || !MasterLane.IsShown(project) || bar < 0 || bar >= BarCount) return null;
        var top = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight - VerticalScrollOffset;
        return new Rect(XOfBar(bar), top + 1, Math.Max(1, WidthOfBar(bar) - 1), Math.Max(1, MasterLane.OffsetOf(project) - 2));
    }

    private void DrawMasterLane(DrawingContext dc, double gridTop, double width, int firstBar, int endBar)
    {
        if (Project is not { } project || !MasterLane.IsShown(project)) return;
        var height = MasterLane.OffsetOf(project);
        var top = gridTop - VerticalScrollOffset;
        if (top + height <= gridTop) return;   // scrolled under the ruler
        dc.DrawRectangle(Draw.Solid(_theme.Background), null, new Rect(0, top, width, height));
        var grid = Draw.Pen(_theme.BoardEdge, 1, 0.55);
        for (var b = firstBar; b <= endBar; b++) dc.DrawLine(grid, new Point(XOfBar(b) - 0.5, top), new Point(XOfBar(b) - 0.5, top + height));
        var count = project.Tracks.Count;
        var line = (height - 4) / count;
        var gap = line >= 3 ? 1 : 0;   // thin lines sit flush: a gap would leave nothing to see
        var brushes = MasterLaneBrushes(project);
        for (var t = 0; t < count; t++)
        {
            var fill = brushes[_masterLaneByTrack ? t : 0];
            var y = top + 2 + t * line;
            var run = -1;
            for (var b = firstBar; b <= endBar; b++)
            {
                if (b < endBar && MasterLaneHasContent(t, b)) { if (run < 0) run = b; continue; }
                if (run < 0) continue;
                var x = XOfBar(run);
                dc.DrawRectangle(fill, null, new Rect(x, y, Math.Max(1, XOfBar(b) - 1 - x), Math.Max(0.5, line - gap)));
                run = -1;
            }
        }
        dc.DrawLine(Draw.Pen(_theme.BoardEdge, 1), new Point(0, top + height - 0.5), new Point(width, top + height - 0.5));
    }
}
