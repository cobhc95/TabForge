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

namespace TabForge.Views;

public enum NotationMode { TabAndStaff, TabOnly, StaffOnly }

internal readonly record struct PalmMutePassage(
    int FirstMeasure, double FirstStartSlots,
    int LastMeasure, double LastEndSlots,
    int EventCount);

internal readonly record struct FadePassage(
    int FirstMeasure, double FirstStartSlots,
    int LastMeasure, double LastEndSlots,
    bool IsFadeOut);

public sealed partial class TabEditorControl : FrameworkElement
{
    public const double BasePageWidth = 1280;
    private const double PagePad = 44;
    private const double RhythmicPixelsPerSlot = 7.5;
    private static string _scoreFontFamily = "Segoe UI";
    private static double _scoreTextSize = 13.5;
    private static bool _scoreTextBold;
    private static bool _scoreTextItalic;

    // Score metrics derive from a single spacing factor so a player can make the tablature easier to
    // read on stage without touching the code. The defaults reproduce the original fixed layout.
    private double _scoreSpacing = 1.0;
    private double _systemVerticalSpacing = 1.0;
    private double _measureHorizontalSpacing = 1.0;
    private bool _centerSystems;
    private double StaffGap => 9.0 * _scoreSpacing;
    private double StringGap => 15.0 * _scoreSpacing;
    private double StaffMarginTop => 46.0 * _scoreSpacing;
    private double StaffHeight => 4 * StaffGap;
    // Keep a generous clear band between standard notation and tablature, matching printed scores.
    private double StaveGap => 64.0 * _scoreSpacing * _systemVerticalSpacing;
    private double SystemHeight => StaffMarginTop + StaffHeight + StaveGap + 5 * StringGap + 28.0 * _scoreSpacing;

    /// <summary>Readability scale for the tablature: line spacing and fret numbers.</summary>
    public double ScoreSpacing
    {
        get => _scoreSpacing;
        set
        {
            var clamped = Math.Clamp(value, 0.85, 1.6);
            if (Math.Abs(clamped - _scoreSpacing) < 0.001) return;
            _scoreSpacing = clamped;
            InvalidateScoreLayout();
        }
    }

    /// <summary>Height of one system (staff + tablature + annotations) at the current spacing.</summary>
    public double SystemHeightNow => SystemHeight * _zoom;

    /// <summary>Clear vertical space between the standard staff and the tablature staff.</summary>
    public double StaffToTabGapNow => StaveGap * _zoom;

    private SongProject? _project;
    private int _selectedTrackIndex;
    private int _activeVoiceIndex;
    private DateTime _lastDigit = DateTime.MinValue;
    private int _lastDigitMeasure = -1, _lastDigitCell = -1, _lastDigitString = -1;
    private double _zoom = 1.0;
    private int _anchorMeasure = -1, _anchorCell = -1;
    private bool _selecting;
    private int _hoverMeasure = -1, _hoverCell = -1;
    private int _selectionEndMeasure = -1, _selectionEndCell = -1;
    private bool _selectionShouldSeekPlayback = true;
    private Point _leftMouseDownPoint;
    private long _leftMouseDownTicks;
    /// <summary>A score range needs the button held this long; a click made mid-movement stays a click.</summary>
    private const int RangeSelectHoldMs = 140;
    private bool _leftMouseDownPending;
    private ScorePageLayout? _scoreLayout;
    private TrackModel? _scoreLayoutTrack;
    private SongProject? _scoreLayoutProject;
    private double _scoreLayoutGridWidth = double.NaN;
    private bool _scoreLayoutHorizontal;
    private double _scoreLayoutSpacing = double.NaN;
    private double _scoreLayoutSystemSpacing = double.NaN;
    private double _scoreLayoutMeasureSpacing = double.NaN;
    private int _scoreGeneration;
    private TrackModel? _scoreFactsTrack;
    private SongProject? _scoreFactsProject;
    private int _scoreFactsGeneration = -1;
    private IReadOnlyList<PalmMutePassage> _palmMutePassages = Array.Empty<PalmMutePassage>();
    private IReadOnlyList<FadePassage> _fadePassages = Array.Empty<FadePassage>();
    private BarState[]? _barStateCache;
    private bool[]? _barStateComputed;
    private MarkerModel?[]? _markerByMeasure;
    private SongProject? _markerCacheProject;
    private int _markerCacheGeneration = -1;
    private StaffNotationMeasureLayout?[,]? _staffLayoutCache;
    private StaffLayoutCacheKey[,]? _staffLayoutKeys;

    /// <summary>Unscaled score-surface width in DIPs; the host derives it from the viewport and zoom.</summary>
    private double _pageWidthOverride;
    public double PageWidthOverride
    {
        get => _pageWidthOverride;
        set
        {
            if (Math.Abs(_pageWidthOverride - value) < 0.1) return;
            _pageWidthOverride = value;
            InvalidateScoreLayout();
        }
    }

    public double SystemVerticalSpacing
    {
        get => _systemVerticalSpacing;
        set
        {
            var clamped = Math.Clamp(value, 0.7, 1.6);
            if (Math.Abs(clamped - _systemVerticalSpacing) < 0.001) return;
            _systemVerticalSpacing = clamped;
            InvalidateScoreLayout();
        }
    }

    public double MeasureHorizontalSpacing
    {
        get => _measureHorizontalSpacing;
        set
        {
            var clamped = Math.Clamp(value, 0.8, 1.6);
            if (Math.Abs(clamped - _measureHorizontalSpacing) < 0.001) return;
            _measureHorizontalSpacing = clamped;
            InvalidateScoreLayout();
        }
    }

    public event EventHandler? EditStarting;
    public event EventHandler? Edited;
    public event EventHandler? SelectionChanged;
    public event EventHandler? PlayRequested;
    public event EventHandler<NotePreviewEventArgs>? NotePreview;

    public NotationMode Notation { get; set; } = NotationMode.TabAndStaff;
    public LedgerLineMode LedgerLines { get; set; } = LedgerLineMode.Minimal;
    public bool DarkPaper { get; set; } = true;
    public Color DarkPaperColor { get; set; } = Color.FromRgb(0x15, 0x18, 0x1D);
    public Color LightPaperColor { get; set; } = Colors.White;
    public Color DarkInkColor { get; set; } = Color.FromRgb(0xE7, 0xEA, 0xEF);
    public Color LightInkColor { get; set; } = Color.FromRgb(0x11, 0x11, 0x11);
    public Color DarkStaffLineColor { get; set; } = Color.FromRgb(0x34, 0x39, 0x40);
    public Color LightStaffLineColor { get; set; } = Color.FromRgb(0xD5, 0xD5, 0xD5);
    public Color AccentColor { get; set; } = Color.FromRgb(0x4C, 0x9A, 0xFF);
    public Color CursorColor { get; set; } = Color.FromRgb(0xF2, 0xC1, 0x4E);
    /// <summary>Colour of sounding notes, fret numbers and the playhead.</summary>
    public Color PlaybackColor { get; set; } = Color.FromRgb(0x3F, 0xB9, 0x50);
    public Color DurationGlowColor { get; set; } = Color.FromRgb(0x3F, 0xB9, 0x50);
    public double DurationGlowOpacity { get; set; } = 0;
    /// <summary>Background tint behind the beat that is sounding (TuxGuitar tints the played beat).</summary>
    public Color HighlightBackground { get; set; } = Color.FromRgb(0x1E, 0x3A, 0x2A);
    /// <summary>Draw the sounding-beat band at all.</summary>
    public bool HighlightPlayedBeat { get; set; } = true;
    public bool ShowSectionHeadings { get; set; } = true;
    public bool ShowBarNumbers { get; set; } = true;
    public int BarNumberFrequency { get; set; } = 1;
    public double LedgerLineOpacity { get; set; } = 1;
    public double HoverHighlightIntensity { get; set; } = 0.27;
    public double SelectionHighlightIntensity { get; set; } = 0.25;
    public Color SelectionColor { get; set; } = Color.FromRgb(0x4C, 0x9A, 0xFF);
    public Color HoverColor { get; set; } = Color.FromRgb(0x98, 0xA1, 0xAE);
    /// <summary>Font size of tablature fret numbers (scaled by <see cref="ScoreSpacing"/>).</summary>
    private double FretFontSize => 11.0 * _scoreSpacing;

    public static void ConfigureScoreTextStyle(string? fontFamily, double size, bool bold, bool italic)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
        try { _ = new FontFamily(family); }
        catch (ArgumentException) { family = "Segoe UI"; } // not a usable family name
        var clampedSize = Math.Clamp(size, 8, 24);
        if (_scoreFontFamily == family && Math.Abs(_scoreTextSize - clampedSize) < 0.001 &&
            _scoreTextBold == bold && _scoreTextItalic == italic) return;
        _scoreFontFamily = family;
        _scoreTextSize = clampedSize;
        _scoreTextBold = bold;
        _scoreTextItalic = italic;
        TypefaceCache.Clear();
        TextCache.Clear();
    }
    public int CurrentDurationDenominator { get; set; } = 4;
    public bool AutoAdvanceAfterEntry { get; set; } = true;
    /// <summary>Default: + shortens the note, - lengthens it. True swaps them.</summary>
    public bool ReversePlusMinusDuration { get; set; }
    /// <summary>When true, duration/dot/tuplet changes that would overfill a bar are refused.</summary>
    public bool PreventBarOverflow { get; set; }
    private void PlusDuration() { if (ReversePlusMinusDuration) Longer(); else Shorter(); }
    private void MinusDuration() { if (ReversePlusMinusDuration) Shorter(); else Longer(); }
    public int CurrentDots { get; set; } = 0;
    public bool CurrentTriplet { get; set; }
    public int CurrentTupletNumerator { get; private set; }
    public int CurrentTupletDenominator { get; private set; }
    public int CurrentVelocity { get; private set; } = 100;

    public int SelectedMeasure { get; private set; }
    public int SelectedCell { get; private set; }
    public int SelectedString { get; private set; }
    public int ActiveVoiceIndex => _activeVoiceIndex;
    /// <summary>False during mouse-only cursor selection so clicking a bar never starts/relocates audio.</summary>
    public bool SelectionShouldSeekPlayback => _selectionShouldSeekPlayback;
    public int PlaybackMeasure { get; set; } = -1;
    /// <summary>Print/PDF export: draw no edit cursor (selection and playback are never set on the private export control).</summary>
    internal bool HideCursor { get; set; }
    public int PlaybackCell { get; set; } = -1;

    // ---- playback feedback (fed from the canonical timeline) ----
    /// <summary>Timeline being played, used to highlight the exact sounding notes.</summary>
    public ScoreTimeline? Timeline { get; set; }
    /// <summary>Track whose notes should be highlighted (the selected track).</summary>
    public int PlaybackTrackIndex { get; set; }
    /// <summary>Absolute playback time in milliseconds.</summary>
    public double PlaybackMs { get; set; }
    /// <summary>Fraction through the playing bar (0..1) for the exact caret position.</summary>
    public double PlaybackFraction { get; set; }
    /// <summary>Maps source-bar indexes from an already-running timeline to the reordered score.</summary>
    public int[]? PlaybackBarRemap { get; set; }
    /// <summary>True while the transport is running (playing or paused): dims the edit cursor so the
    /// green playhead is the only tracker.</summary>
    public bool PlaybackActive { get; set; }

    public TabEditorControl()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        MouseDown += OnMouseDown;
        MouseRightButtonDown += OnRightDown;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => { if (_hoverMeasure != -1) { _hoverMeasure = -1; _hoverCell = -1; InvalidateVisual(); } };
        MouseUp += OnMouseUp;
    }

    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.5, 2.0); InvalidateMeasure(); InvalidateVisual(); }
    }

    public SongProject? Project
    {
        get => _project;
        set { _project = value; CoerceSelection(); InvalidateScoreLayout(); }
    }

    public int SelectedTrackIndex
    {
        get => _selectedTrackIndex;
        set { _selectedTrackIndex = Math.Max(0, value); CoerceSelection(); InvalidateScoreLayout(); }
    }

    public TrackModel? Track => _project is not null && _selectedTrackIndex >= 0 && _selectedTrackIndex < _project.Tracks.Count
        ? _project.Tracks[_selectedTrackIndex] : null;

    public bool HasSelection => _selecting && (_anchorMeasure != _selectionEndMeasure || _anchorCell != _selectionEndCell);

    /// <summary>Inclusive measure range changed by the next edit, used by overview activity invalidation.</summary>
    public (int FirstMeasure, int LastMeasure) AffectedMeasureRange
    {
        get
        {
            if (!HasSelection || _anchorMeasure < 0 || _selectionEndMeasure < 0)
                return (SelectedMeasure, SelectedMeasure);
            return (Math.Min(_anchorMeasure, _selectionEndMeasure), Math.Max(_anchorMeasure, _selectionEndMeasure));
        }
    }
}
