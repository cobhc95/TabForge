using TabForge.Models;
using TabForge.Services;
using TabForge.Platform;
using TabForge.Platform.Testing;
using TabForge.Presenters;

namespace TabForge.Views.Score;

// Owns: a headless score host with the editor's page metrics at zoom 1, dark paper unless Light is set, an optional cursor, selection and hover (EditorSelectionState) and the editor's hit rule, no playback; it lays out a track
//   and draws the header and the systems that hold its first bars through a GeometryRecorder, so the WPF and Avalonia adapters can be compared.
// Does not own: the layout or drawing (ScoreLayoutEngine, ScoreRenderer) or the toolkit adapters.
// Tests: --score-geometry (WPF), ScoreGeometryProofTests (Avalonia).
// Diagnostic tool, used by --score-geometry and the Avalonia proof.
internal sealed class ScoreGeometryProbe : IScoreLayoutHost, IScoreRenderHost, IScoreAppearanceHost
{
    /// <summary>The fixed page width the preview draws at, in DIPs (the page view's fit width divides the viewport by it).</summary>
    public const double PageWidth = 1280;
    private SongProject _project;
    private TrackModel _track;
    private readonly ScoreLayoutEngine _layout;
    /// <summary>The index of the track the page draws (0 for the whole-song score; a Band panel sets its own track).</summary>
    public int TrackIndex { get; private set; }
    private readonly ScoreRenderer _renderer;

    /// <summary>True (the score's page): the header block of title, authors and tuning is drawn above the first system. A Band panel turns it off, as the WPF Band rows draw none.</summary>
    public bool DrawsHeader { get; set; } = true;

    /// <param name="measure">The adapter's own context: the layout measures text with it.</param>
    internal ScoreGeometryProbe(SongProject project, IDrawContext measure)
    {
        _project = project;
        _track = project.Tracks[0];
        Appearance = new ScoreAppearance(this);
        UseDefaultSettingsPaper(Appearance);
        _layout = new ScoreLayoutEngine(this, new ScoreTextWidths(measure));
        _renderer = new ScoreRenderer(this, _layout, new StaffNotationRenderer(), new NoColourText());
        // The probe is the drawing host and keeps no voice of its own: it draws and hit-tests the first voice. The active voice, the clipboard and the edits belong to ScorePreviewEditor (its CellsFor).
        Selection.CellsOf = m => m >= 0 && m < _track.Measures.Count ? _track.Measures[m].CellsForVoice(0) : null;
    }

    // The paper, ink and staff-line colours the WPF editor shows with default settings (MainWindow.ApplyScoreAppearanceSettings
    // reads them from AppearanceSettings at full staff-line opacity), so the light paper is the app's soft grey, not the bare
    // ScoreAppearance default the WPF render identity keeps.
    private static void UseDefaultSettingsPaper(ScoreAppearance appearance)
    {
        var settings = new TabForge.Services.AppearanceSettings();
        appearance.DarkPaperColor = TabForge.Platform.Theme.Palette.Parse(settings.DarkScorePaperColour);
        appearance.LightPaperColor = TabForge.Platform.Theme.Palette.Parse(settings.LightScorePaperColour);
        appearance.DarkInkColor = TabForge.Platform.Theme.Palette.Parse(settings.DarkScoreInkColour);
        appearance.LightInkColor = TabForge.Platform.Theme.Palette.Parse(settings.LightScoreInkColour);
        appearance.DarkStaffLineColor = TabForge.Platform.Theme.Palette.Parse(settings.DarkScoreLinesColour);
        appearance.LightStaffLineColor = TabForge.Platform.Theme.Palette.Parse(settings.LightScoreLinesColour);
    }

    /// <summary>The editor's selection range and hovered beat, drawn by the renderer; empty by default.</summary>
    internal EditorSelectionState Selection { get; } = new();

    /// <summary>Light paper, ink and faint ink when true (the editor's light appearance); dark when false, the default the proofs compare.</summary>
    internal bool Light { get => !Appearance.DarkPaper; set => Appearance.DarkPaper = !value; }

    /// <summary>Draws the header and every system that starts before <paramref name="bars"/> through a recorder around <paramref name="target"/>.</summary>
    internal List<DrawRecord> Draw(IDrawContext target, int bars)
    {
        var recorder = new GeometryRecorder(target);
        DrawPage(recorder, bars);
        return recorder.Records;
    }

    /// <summary>Draws the header and every system that starts before <paramref name="bars"/> straight to <paramref name="d"/>, with no recording.</summary>
    /// <param name="visibleRows">The page rows (top, bottom) the viewer shows; systems outside them are skipped. Null draws every system.</param>
    internal void DrawPage(IDrawContext d, int bars, bool withPlayhead = true, (double Top, double Bottom)? visibleRows = null)
    {
        var ink = Appearance.InkColor;
        var faint = TabForge.Platform.Theme.Palette.Get("ScoreFaintInk", light: Light);
        ScoreText.NormalInk = ink;
        var (pageWidth, pageHeight) = ContentSize();
        d.Rect(Appearance.PaperColor, null, 0, 0, pageWidth, bars >= _track.Measures.Count ? Math.Max(800, pageHeight) : 800);   // a partial draw (the proofs) keeps the fixed sheet
        if (DrawsHeader) _renderer.DrawHeader(d, _project, _track, ink, faint);
        var facts = _layout.EnsureScoreFacts(_track, _project);
        var systemIndex = -1;
        foreach (var system in _layout.GetLayout(_track).Systems)
        {
            systemIndex++;
            if (system.FirstMeasure >= bars) break;
            if (visibleRows is var (top, bottom) && (SystemRowTop(systemIndex) + SystemHeight < top || SystemRowTop(systemIndex) > bottom)) continue;
            _renderer.DrawSystem(d, _track, system, ink, faint, Appearance.StaffLineColor, Appearance.AccentColor, Appearance.CursorColor,
                Appearance.PlaybackColor, Appearance.ErrorColor, facts.PalmMutePassages, facts.FadePassages);
        }
        if (withPlayhead) DrawPlayhead(d);
    }

    /// <summary>Draws only the playhead line (the part of the page that moves every frame); nothing when stopped.</summary>
    internal void DrawPlayhead(IDrawContext d)
    {
        if (Play.PlayheadLine(this) is not var (x, top, bottom)) return;
        var colour = Appearance.PlaybackColor;   // the same highlight colour as the lit notes (Preferences > Appearance > Playback highlight)
        d.Line(new UiStroke(colour, Appearance.PlayheadThickness), x, top, x, bottom);
        var radius = PlayheadDotRadius;
        d.Ellipse(colour, (UiStroke?)null, x, top, radius, radius);
    }

    /// <summary>Radius of the round dot on the top end of the playing line: it grows with the line's thickness (setting PlayheadThickness).</summary>
    internal double PlayheadDotRadius => Math.Max(PlayheadDotMinRadius, Appearance.PlayheadThickness * PlayheadDotPerThickness);

    private const double PlayheadDotMinRadius = 4;
    private const double PlayheadDotPerThickness = 1.75;

    /// <summary>The beat and string under a page point, by the editor's hit rule (ScoreHitRule).</summary>
    internal (int Measure, int Cell, int String) HitTest(double x, double y) =>
        ScoreHitRule.HitTest(_layout, _track, x, y, HeaderHeight, SystemHeight, TabTop, StringGap, SlotsFor, measure => measure.CellsForVoice(0));

    /// <summary>The page y where the beats begin, below the header block: a point above it is not on a beat.</summary>
    internal double BeatAreaTop => HeaderHeight;

    /// <summary>The page height of one system row (its staves and the room under them), before the zoom.</summary>
    internal double SystemBandHeight => SystemHeight;

    /// <summary>The rows a PDF page split works from: the header block's height, one system's height, and the systems of the shown track.</summary>
    internal (double HeaderHeight, double SystemHeight, int Systems) PrintRows() =>
        (HeaderHeight, SystemHeight, _track.IsAudio ? 1 : _layout.GetLayout(_track).SystemCount);

    /// <summary>Shows <paramref name="project"/> (an undo step restored a new song object): its first track, laid out again.</summary>
    internal void ReplaceSong(SongProject project)
    {
        _project = project;
        _track = project.Tracks[Math.Min(TrackIndex, project.Tracks.Count - 1)];
        _layout.Invalidate();
    }

    /// <summary>Draws track <paramref name="index"/> of the song from now on (the Band panels use this); laid out again.</summary>
    internal void ShowTrack(int index)
    {
        TrackIndex = index;
        _track = _project.Tracks[Math.Min(index, _project.Tracks.Count - 1)];
        _layout.Invalidate();
    }

    /// <summary>Drops the laid-out score so the next draw lays it out again (after a spacing, text or page-layout setting changed).</summary>
    internal void InvalidateLayout() => _layout.Invalidate();

    /// <summary>The editor cursor (bar, beat, string); null hides it.</summary>
    internal (int Measure, int Cell, int String)? Cursor { get; set; }

    private sealed class NoColourText : IColourText { public UiColor? Parse(string? text) => null; }

    // ---- page metrics (the editor's formulas, TabEditorControl.cs and .Geometry.cs) ----
    public ScoreAppearance Appearance { get; }
    IScoreLayoutSettings IScoreLayoutHost.Appearance => Appearance;
    public SongProject? Project => _project;
    public TrackModel? Track => _track;
    /// <summary>Tablature, standard notation or both (the score's Notation rows set it).</summary>
    public NotationMode Notation
    {
        get => _notation;
        set { if (_notation == value) return; _notation = value; _layout.Invalidate(); }
    }
    private NotationMode _notation = NotationMode.TabAndStaff;
    // Removed: a per-track "no TAB for keys" notation. The score follows the song-wide Notation for every track; only the instrument panel follows the instrument.
    /// <summary>One line scrolling sideways (the Horizontal row) instead of wrapped lines.</summary>
    public bool HorizontalScroll { get; set; }
    /// <summary>The page's left and right margin; the main score keeps <see cref="DefaultPagePadding"/>, a Band card sets its own small one.</summary>
    public double PagePadding { get; set; } = DefaultPagePadding;
    /// <summary>The room left of the first bar for the clef and the T-A-B letters; a Band card sets only what those need.</summary>
    public double LabelGutter { get; set; } = DefaultLabelGutter;
    public const double DefaultPagePadding = ScoreLayoutEngine.PagePad, DefaultLabelGutter = 46;
    public double GridLeft => PagePadding + LabelGutter;
    public double GridWidth => Math.Max(200, CurrentPageWidth - PagePadding * 2 - LabelGutter);
    public double FretFontSize => 11.0 * Appearance.ScoreSpacing;
    public double HeaderCentreX => (HorizontalScroll ? PageWidth : CurrentPageWidth) / 2;

    /// <summary>The logical page width the continuous view sets from its viewport (0 = the fixed <see cref="PageWidth"/>); a change lays the score out again.</summary>
    public double PageWidthOverride
    {
        get => _pageWidthOverride;
        set { if (Math.Abs(_pageWidthOverride - value) < 0.5) return; _pageWidthOverride = value; _layout.Invalidate(); }
    }
    private double _pageWidthOverride;

    /// <summary>The page width now: the whole line in horizontal mode, else the override, else the fixed page.</summary>
    public double CurrentPageWidth => HorizontalScroll && _layout.HorizontalPageWidth > 1 ? _layout.HorizontalPageWidth
        : _pageWidthOverride > 1 ? _pageWidthOverride : PageWidth;

    /// <summary>The page size the scroll viewer sizes the score to (logical units, before zoom): the width above and the header plus every system.</summary>
    public (double Width, double Height) ContentSize()
    {
        var systems = _track.IsAudio ? 1 : _layout.GetLayout(_track).SystemCount;
        return (CurrentPageWidth, HeaderHeight + systems * SystemHeight + 48);
    }
    public double StaffGap => 9.0 * Appearance.ScoreSpacing;
    public double StringGap => 15.0 * Appearance.ScoreSpacing;
    /// <summary>True for a Band card's one-line view: the blank above and below the tab is cut to what the number row, the marks and the lyrics need. The main score keeps its spacing.</summary>
    public bool TightLineBand { get => _tightLineBand; set { if (_tightLineBand == value) return; _tightLineBand = value; _layout.Invalidate(); } }
    private bool _tightLineBand;
    // The tight band applies to the tab-only line; a staff keeps the room its clef and ledger ink need.
    private bool IsTight => _tightLineBand && Notation == NotationMode.TabOnly;
    /// <summary>The room above the staff line (tab-only: above the tab's marks) for the bar number row: the main score's, and a Band card's tight one.</summary>
    public const double MarginTop = 38, TightMarginTop = 26;
    /// <summary>How far above the staff line the bar number row reaches (its distance and text height); the tight margin keeps at least this.</summary>
    public const double NumberRowReach = 14 + 11;
    /// <summary>
    /// The room (logical units, before the zoom) a tight tab-only line leaves above the tab of <paramref name="system"/> that none of its bars' marks use: the track's reserve for
    /// the bars with the most marks, less what this system's bars need. 0 for any other line.
    /// </summary>
    public double UnusedTopRoom(int system)
    {
        if (!IsTight || _track.IsAudio) return 0;
        var layout = _layout.GetLayout(_track);
        if (system < 0 || system >= layout.SystemCount) return 0;
        var bars = layout.Systems[system];
        return Math.Max(0, _layout.ExtraAbove - _layout.ExtraAboveNeeded(bars.FirstMeasure, bars.LastMeasure)) * Appearance.ScoreSpacing;
    }
    private double StaffMarginTop => ((IsTight ? TightMarginTop : MarginTop) + _layout.ExtraAbove) * Appearance.ScoreSpacing;
    // The standard staff's four-line block and the room under it are not drawn in tab-only mode, so the TAB sits below the lane its own
    // marks stack into (P.M., let ring, technique labels: the highest sits TabOnlyMarksRoom above the strings), with no empty band above it.
    private double StaffToTab => Notation == NotationMode.TabOnly
        ? (IsTight ? TightMarksRoom : TabOnlyMarksRoom) * Appearance.ScoreSpacing * Appearance.SystemVerticalSpacing
        : 4 * StaffGap + (StaffToTabRoom + _layout.ExtraBelow) * Appearance.ScoreSpacing * Appearance.SystemVerticalSpacing;
    private const double StaffToTabRoom = 52.0;
    private const double TabOnlyMarksRoom = 30.0, TightMarksRoom = 20.0, TightBelowTab = 10.0;
    private int Strings => Math.Max(1, _track.StringTunings.Count is > 0 and var n ? n : 6);
    private double SystemHeight => StaffMarginTop + StaffToTab + (Strings - 1) * StringGap + Math.Max((IsTight ? TightBelowTab : 20.0) * Appearance.ScoreSpacing, _layout.ExtraTabBelow);
    private double HeaderHeight => !DrawsHeader ? 0 : 82 +(ScoreLayoutEngine.HasStringTuning(_track) ? Math.Max(1, (_track.StringTunings.Count + 1) / 2) : 0) * 11;
    private double SystemRowTop(int system) => ScoreEditorGeometry.SystemTop(HeaderHeight, system, SystemHeight);
    public double StaffTop(int system) => ScoreEditorGeometry.StaffTop(HeaderHeight, system, SystemHeight, StaffMarginTop);
    public double TabTop(int system) => ScoreEditorGeometry.TabTop(StaffTop(system), StaffToTab);
    public int SlotsFor(int measure) => ScoreEditorGeometry.SlotsFor(_project, measure);
    public ScoreLayoutEngine Layout => _layout;
    public double Zoom => 1;

    // ---- cursor, selection and hover from the editor; no playback ----
    public bool HideCursor => Cursor is null;
    public int SelectedMeasure => Cursor?.Measure ?? -1;
    public int SelectedCell => Cursor?.Cell ?? -1;
    public int SelectedString => Cursor?.String ?? -1;
    public bool HasSelection => Selection.HasSelection;
    public (int m1, int c1, int m2, int c2) SelectionRange() => Selection.HasSelection ? Selection.Range() : (-1, -1, -1, -1);
    public (int m1, int c1, int m2, int c2)? LoopTint { get; set; }
    public IReadOnlyList<(int First, int Last)>? MirrorBars { get; set; }
    public int HoverMeasure => Selection.HoverMeasure;
    public int HoverCell => Selection.HoverCell;
    public int ActiveVoiceIndex => 0;   // the drawing host's voice is always the first; ScorePreviewEditor owns the active voice
    /// <summary>The score's playing state (stopped by default); the views set it from the transport.</summary>
    internal ScorePlayState Play { get; } = new();
    public bool PlaybackActive => Play.Active;
    public int PlaybackMeasure => Play.Measure;
    public int PlaybackCell => Play.Cell;
    public double PlaybackFraction => Play.Fraction;
    public HashSet<(int bar, int cell, int s)> SoundingNotes => Play.Sounding;
    public HashSet<(int bar, int cell, int s)> StruckNotes => Play.Struck;
    public (UiRect Rect, UiColor Colour)? PlayingBarBand(TrackModel track, ScoreSystemPosition system, ScoreMeasurePosition position) => Play.PlayingBar(this, track, position);
    public bool InHorizontalBand(ScoreMeasurePosition measure) => true;

    public void LayoutChanged() { }
    public void RepaintAll() { }
    public void RepaintKeepingDrawings() { }
    public void AppearanceChanged(ScoreAppearanceChange change) { if (change == ScoreAppearanceChange.Layout) _layout.Invalidate(); }
}
