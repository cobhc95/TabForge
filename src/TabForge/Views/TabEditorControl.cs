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

// Owns: the control's public state: the strings and spacing of a system, the horizontal scroll and zoom, the edit host and the
//   standalone-edit event, the font and text styles, and the editing preferences (auto-advance, bar overflow, fill with rests).
// Does not own: the page maths (TabEditorControl.Geometry.cs), the edit runner (TabEditorControl.Layout.cs), drawing
//   (TabEditorControl.Rendering.cs) and the score engine.
// Tests: TestTabEditorLifetime.

internal readonly record struct PalmMutePassage(
    int FirstMeasure, double FirstStartSlots,
    int LastMeasure, double LastEndSlots,
    int EventCount);

internal readonly record struct FadePassage(
    int FirstMeasure, double FirstStartSlots,
    int LastMeasure, double LastEndSlots,
    bool IsFadeOut);

public sealed partial class TabEditorControl : FrameworkElement, IScoreLayoutHost, IScoreAppearanceHost, IPlaybackOverlayHost, IScoreRenderHost, IEditorInputHost
{
    public const double BasePageWidth = 1280;

    // Score metrics derive from a single spacing factor so a player can make the tablature easier to
    // read on stage without touching the code. The defaults reproduce the original fixed layout.
    private double StaffGap => 9.0 * Appearance.ScoreSpacing;
    private double StringGap => 15.0 * Appearance.ScoreSpacing;
    private double StaffMarginTop => (38.0 + _layout.ExtraAbove) * Appearance.ScoreSpacing;
    private double StaffHeight => 4 * StaffGap;
    // Keep a generous clear band between standard notation and tablature, matching printed scores (grown when low notes reach into it).
    private double StaveGap => (52.0 + _layout.ExtraBelow) * Appearance.ScoreSpacing * Appearance.SystemVerticalSpacing;
    private double SystemHeight => StaffMarginTop + StaffToTab + (TabStringCount - 1) * StringGap + Math.Max(20.0 * Appearance.ScoreSpacing, _layout.ExtraTabBelow);
    /// <summary>Strings of the shown track (6 when there is none): the tab part of a system follows it.</summary>
    private int TabStringCount => Math.Max(1, Track?.StringTunings.Count is > 0 and var n ? n : 6);

    /// <summary>Distance from the staff top to the TAB top: the staff and its gap, or just room for the marks when only the TAB is shown.</summary>
    private double StaffToTab => Notation == NotationMode.TabOnly ? 30.0 * Appearance.ScoreSpacing : StaffHeight + StaveGap;

    /// <summary>Height of one system (staff + tablature + annotations) at the current spacing.</summary>
    public double SystemHeightNow => SystemHeight * _zoom;

    /// <summary>Clear vertical space between the standard staff and the tablature staff.</summary>
    public double StaffToTabGapNow => StaveGap * _zoom;

    private SongProject? _project;
    private int _selectedTrackIndex;
    private int _activeVoiceIndex;
    private double _zoom = 1.0;
    private readonly EditorSelectionState _sel = new();
    private readonly EditorInputController _input;
    private bool _selectionShouldSeekPlayback = true;

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

    /// <summary>Where edits run (one undo step, dirty, timeline); when unset, the window that shows the editor if it implements the interface.</summary>
    public IScoreEditHost? EditHost { get; set; }

    /// <summary>Raised before an edit when no <see cref="EditHost"/> runs the edits (a standalone control).</summary>
    public event EventHandler? EditStarting;
    public event EventHandler? Edited;
    public event EventHandler? SelectionChanged;
    public event EventHandler? PlayRequested;
    /// <summary>Set by the host: appends a bar and moves the caret into it (Right arrow past the last beat).</summary>
    public Action? AppendBarAtEnd { get; set; }
    public event EventHandler<NotePreviewEventArgs>? NotePreview;

    public NotationMode Notation
    {
        get => _notation;
        set
        {
            if (_notation == value) return;
            _notation = value;
            InvalidateScoreLayout();   // the tab-only view drops the empty staff band, so the system height changes
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
    private NotationMode _notation = NotationMode.TabAndStaff;
    /// <summary>Font size of tablature fret numbers (scaled by <see cref="Appearance.ScoreSpacing"/>).</summary>
    private double FretFontSize => 11.0 * Appearance.ScoreSpacing;

    public static void ConfigureScoreTextStyle(string? fontFamily, double size, bool bold, bool italic) => ScoreText.ConfigureStyle(fontFamily, size, bold, italic);

    /// <summary>Per-area score text styles (Score &gt; Appearance &gt; Text &amp; fonts).</summary>
    public static void ConfigureTextAreas(IReadOnlyDictionary<string, TabForge.Services.ScoreTextAreaStyle>? styles) => ScoreText.ConfigureAreas(styles);
    public int CurrentDurationDenominator { get; set; } = 4;
    public bool AutoAdvanceAfterEntry { get; set; } = false;
    /// <summary>Default: + shortens the note, - lengthens it. True swaps them.</summary>
    public bool ReversePlusMinusDuration { get; set; }
    /// <summary>When true, duration/dot/tuplet changes that would overfill a bar are refused.</summary>
    public bool PreventBarOverflow { get; set; }
    /// <summary>Fill incomplete bars with rests (the setting): edits keep every edited bar complete.</summary>
    public bool FillBarsWithRests { get; set; }
    /// <summary>Deleting notes leaves merged rests (the fewest that fill the bar) instead of a rest of the same length.</summary>
    public bool MergeRestsOnDelete { get; set; } = true;
    private void PlusDuration() { if (ReversePlusMinusDuration) _edits.Longer(); else _edits.Shorter(); }
    private void MinusDuration() { if (ReversePlusMinusDuration) _edits.Shorter(); else _edits.Longer(); }
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
    /// <summary>Print/PDF export: draw no edit cursor (selection and playback are never set on the private export control).</summary>
    internal bool HideCursor { get; set; }

    // ---- playback feedback (state lives in PlaybackOverlay) ----
    // ---- playback feedback: state and geometry live in PlaybackOverlay (the Playback property) ----
    private readonly PlaybackOverlay _playback;
    private readonly ScoreLayoutEngine _layout;
    private readonly ScoreRenderer _renderer;

    /// <summary>How the score looks: colours, spacing, labels and the playing-bar band.</summary>
    internal ScoreAppearance Appearance { get; }

    internal ScoreLayoutEngine Layout => _layout;

    public TabEditorControl()
    {
        Appearance = new ScoreAppearance(this);
        _playback = new PlaybackOverlay(this);
        _layout = new ScoreLayoutEngine(this, _staff);
        _renderer = new ScoreRenderer(this, _layout, _staff);
        Focusable = true;
        SnapsToDevicePixels = true;
        _input = new EditorInputController(this, _sel);
        MouseDown += _input.OnMouseDown;
        MouseRightButtonDown += _input.OnRightDown;
        MouseMove += _input.OnMouseMove;
        MouseLeave += _input.OnMouseLeave;
        MouseUp += _input.OnMouseUp;
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
        set { _selectedTrackIndex = Math.Max(0, value); CoerceSelection(); InvalidateScoreLayout(); InvalidateMeasure(); }
    }

    public TrackModel? Track => _project is not null && _selectedTrackIndex >= 0 && _selectedTrackIndex < _project.Tracks.Count
        ? _project.Tracks[_selectedTrackIndex] : null;

    public bool HasSelection => _sel.HasSelection;

    /// <summary>Inclusive measure range changed by the next edit, used by overview activity invalidation.</summary>
    public (int FirstMeasure, int LastMeasure) AffectedMeasureRange
    {
        get
        {
            if (!HasSelection || _sel.AnchorMeasure < 0 || _sel.EndMeasure < 0)
                return (SelectedMeasure, SelectedMeasure);
            return (Math.Min(_sel.AnchorMeasure, _sel.EndMeasure), Math.Max(_sel.AnchorMeasure, _sel.EndMeasure));
        }
    }
}
