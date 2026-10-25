using System;
using System.Collections.Generic;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the room past the song's last bar that a clip drag shows (empty "ghost" bars with their numbers in the ruler) and the seconds-to-x mapping there.
// Does not own: the drag (TimelineClipGestureController), the bars the drop adds (ClipAutoBars); no model change happens while ghosts show.
public sealed partial class TimelineView
{
    private int _ghostBars;

    /// <summary>Set for the length of a clip move or end trim: seconds and x past the last bar then map linearly instead of stopping at the edge.</summary>
    internal bool PastEnd { get; set; }

    /// <summary>The empty bars drawn after the last bar during a clip drag past the song's end (preview only); 0 otherwise.</summary>
    internal int GhostBars
    {
        get => _ghostBars;
        set
        {
            value = Math.Max(0, value);
            if (_ghostBars == value) return;
            _ghostBars = value;
            Width = _boundaries[^1] + GhostWidth;
            InvalidateVisual();
        }
    }

    /// <summary>The width of one ghost bar: the last bar's.</summary>
    private double BarWidthAtEnd => BarCount > 0 ? _boundaries[^1] - _boundaries[^2] : BarUnitWidth * _zoomFactor;

    private double GhostWidth => _ghostBars * BarWidthAtEnd;

    /// <summary>True while a clip drag may run past the end (<see cref="PastEnd"/>) and time past the song's end continues as bars of the last bar's length: the performed order reaches the last written bar.</summary>
    internal bool ContinuesPastEnd => PastEnd && BarCount > 0 && !ClipAutoBars.EndsInJump(_song) && SongExtent.Timing(_song).BarSec > 0;

    private double ExtraXOfSeconds(double seconds) =>
        _boundaries[^1] + Math.Max(0, seconds - SongExtent.Timing(_song).EndSec) / SongExtent.Timing(_song).BarSec * BarWidthAtEnd;

    private double ExtraSecondsOfX(double x) =>
        SongExtent.Timing(_song).EndSec + (x - _boundaries[^1]) / BarWidthAtEnd * SongExtent.Timing(_song).BarSec;

    /// <summary>Draws the ghost bars' ticks and numbers in the ruler, after the last bar.</summary>
    private void DrawGhostRuler(IDrawContext d, Func<string, UiColor> ink, UiStroke edge)
    {
        if (_ghostBars == 0) return;
        var barWidth = BarWidthAtEnd;
        var labelEvery = barWidth < 18 ? 8 : barWidth < 28 ? 4 : 1;
        for (var i = 0; i < _ghostBars; i += 1)
        {
            if (i % labelEvery != 0) continue;
            var x = _boundaries[^1] + i * barWidth;
            d.DrawText(d.Text(BarNumberText(BarCount + i + 1), Font, 12, ink("InstrumentLegible").WithOpacity(GhostOpacity), d.PixelsPerDip), x + 3, 3);
            d.Line(edge, Math.Round(x) + 0.5, RulerHeight - 6, Math.Round(x) + 0.5, RulerHeight - 0.5);
            d.Line(edge, Math.Round(x) + 0.5, RulerHeight + SectionHeight, Math.Round(x) + 0.5, Bounds.Height);
        }
    }

    // The ruler's number texts, made once per number (a frame must not format a string per ghost bar). Ceiling: one string per bar number drawn. Thread: UI thread only.
    private readonly List<string> _barNumberTexts = new();

    private string BarNumberText(int number)
    {
        while (_barNumberTexts.Count <= number) _barNumberTexts.Add(_barNumberTexts.Count.ToString());
        return _barNumberTexts[number];
    }

    private const double GhostOpacity = 0.45;
}
