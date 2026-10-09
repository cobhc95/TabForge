using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;

namespace TabForge.Views;

// TabEditorControl: page geometry, score layout, duration-based spacing and cursor positioning.
public sealed partial class TabEditorControl
{
    // ---------- geometry ----------

    // Horizontal scroll mode: the page is exactly as wide as the single line of bars.
    private double PageWidth => HorizontalScroll && _layout.HorizontalPageWidth > 1 ? _layout.HorizontalPageWidth
        : PageWidthOverride > 1 ? PageWidthOverride : BasePageWidth;
    // Title block centre: over the first page width in one-line mode (the page there is the whole song).
    private double HeaderCentreX => HorizontalScroll ? BasePageWidth / 2 : PageWidth / 2;
    private int TuningRows => ScoreLayoutEngine.HasStringTuning(Track) ? Math.Max(1, (Track!.StringTunings.Count + 1) / 2) : 0;
    private double HeaderHeight => 82 + TuningRows * 11;
    private double SystemTop(int system) => HeaderHeight + system * SystemHeight;
    private double StaffTop(int system) => HeaderHeight + system * SystemHeight + StaffMarginTop;
    private double TabTop(int system) => StaffTop(system) + StaffToTab;
    private double GridLeft => ScoreLayoutEngine.PagePad + 46;
    internal double GridWidth => Math.Max(200, PageWidth - ScoreLayoutEngine.PagePad * 2 - 46);

    /// <summary>
    /// Horizontal scroll mode: the whole score is one continuous line that runs to the right (no page
    /// wrapping); the view scrolls and follows playback horizontally.
    /// </summary>
    public bool HorizontalScroll
    {
        get => _horizontalScroll;
        set
        {
            if (_horizontalScroll == value) return;
            _horizontalScroll = value;
            _layout.HorizontalPageWidth = 0;
            InvalidateScoreLayout();
            InvalidateMeasure();
        }
    }
    private bool _horizontalScroll;

    /// <summary>
    /// Vertical scroll offset that brings the given measure's system near the top of the viewport.
    /// Exposed so the window never hardcodes the score layout geometry (it used to scroll by a
    /// different system height than the renderer draws, so auto-scroll drifted during playback).
    /// </summary>
    public double ScrollOffsetForMeasure(int measure)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return 0;
        var system = Layout.GetLayout(track).SystemForMeasure(Math.Clamp(measure, 0, track.Measures.Count - 1));
        // The first system scrolls to the top so the title and tuning block above it stay in view.
        return system == 0 ? 0 : Math.Max(0, SystemTop(system) - 20) * _zoom;
    }

    /// <summary>Left edge (rendered pixels) of a measure, for horizontal scrolling to the cursor.</summary>
    public double HorizontalOffsetForMeasure(int measure)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return 0;
        var layout = Layout.GetLayout(track);
        var index = Math.Clamp(measure, 0, track.Measures.Count - 1);
        return Math.Max(0, layout.Measure(index).X - 60) * _zoom;
    }

    /// <summary>Top edge (page coordinates) of the system that contains the given measure.</summary>
    public double SystemTopForMeasure(int measure)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return 0;
        var system = Layout.GetLayout(track).SystemForMeasure(Math.Clamp(measure, 0, track.Measures.Count - 1));
        return SystemTop(system) * _zoom;
    }

    private int SlotsFor(int measure)
    {
        var p = _project;
        return p is null ? 16 : MusicTime.BarSlots(p, measure);
    }

    /// <summary>A cursor cell kept inside a bar: past the bar's slots only on a real beat of an overfull bar, otherwise the nearest allowed position.</summary>
    private int CoerceCell(int measure, int cell) => cell < SlotsFor(measure) ? Math.Max(0, cell) : Snap(measure, cell);

    /// <summary>Invalidate the natural-width and system-break cache after score content changes; the cursor is clamped into the changed song (a shorter time signature, fewer bars).</summary>
    public void InvalidateScoreLayout()
    {
        CoerceSelection(); _layout.Invalidate();
    }

    void IScoreLayoutHost.LayoutChanged() { InvalidateStructure(); InvalidateMeasure(); InvalidateVisual(); }

    /// <summary>
    /// The window that hosted this editor has closed for good: forget the song and every cache that points into it. A UI Automation client can keep this
    /// control (through its automation peer) alive after the window is gone; it must then pin a small control, not the song.
    /// </summary>
    internal void ReleaseDocument()
    {
        _project = null;
        _layout.Release();
        _playback.Release();
        _describer?.Release();
        InvalidateScoreLayout();
    }

    public void SetActiveVoice(int voiceIndex)
    {
        var next = Math.Clamp(voiceIndex, 0, 1);
        if (_activeVoiceIndex == next) return;
        _activeVoiceIndex = next;
        SelectionChangedNow(seekPlayback: false);
    }

    private List<TabCell> CellsFor(MeasureModel measure, bool create = false)
        => measure.CellsForVoice(_activeVoiceIndex, create);



    public void SetPosition(int measure, int cell, int @string, bool seekPlayback = true)
    {
        var track = Track;
        if (track is null) return;
        SelectedMeasure = Math.Clamp(measure, 0, Math.Max(0, track.Measures.Count - 1));
        SelectedCell = CoerceCell(SelectedMeasure, cell);
        SelectedString = Math.Clamp(@string, 0, Math.Max(0, track.StringTunings.Count - 1));
        SelectionChangedNow(seekPlayback);
    }

    public void SetBar(int measure, bool seekPlayback = true) => SetPosition(measure, 0, SelectedString, seekPlayback);
}
