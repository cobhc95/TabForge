using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

public enum InstrumentPanelKind { Fretboard, Keyboard, Drums }

/// <summary>The note under a click: its MIDI note, and for the fretboard its string and fret. Nothing sounds in the preview.</summary>
public readonly record struct InstrumentNotePick(int Midi, int? StringIndex, int? Fret);

/// <summary>
/// The instrument panel (fretboard, keyboard or drum key map) with the score cursor's notes lit; a click raises <see cref="NotePicked"/>.
/// Geometry and hit tests come from the Core rules (<see cref="FretboardGeometryRules"/>, <see cref="KeyboardGeometryRules"/>,
/// <see cref="PercussionKeyMapRules"/>); colours are the Instrument palette keys the panel reads.
/// Owns: the Avalonia drawing of the three layouts (the fretboard with its string and fret numbers and the colour legend), the click mapping,
/// the score-cursor lit notes and the playing notes (now, next, upcoming, recent) from <see cref="InstrumentPlaybackLists"/>. The playing
/// notes replace the cursor's while playback shows them, and the panel repaints only when the lists change (note boundaries).
/// Does not own: the rules, the artwork of the panel (shaded markers, strum arrows, scale highlights), the
/// Scales button (<see cref="InstrumentPanelStrip"/>), the instrument icons, the playback clock or audio.
/// Over 300 lines: the fretboard layout and its marker drawing are here. The playing notes (InstrumentPanelView.Playback.cs), the labels
/// (InstrumentPanelView.Labels.cs), the keyboard and drum layouts (InstrumentPanelView.Layouts.cs) and the fretboard drawing with its markers
/// and legend (InstrumentPanelView.Fretboard.cs) are partial files of this class.
/// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests, TabForge.Avalonia.Tests.InstrumentPlaybackListsTests.
/// </summary>
public sealed partial class InstrumentPanelView : Control
{
    public const double PanelHeight = 150;
    /// <summary>The width at the pane's right edge kept clear for the close X: its 20 DIP button, 2 DIPs in from the edge, and a margin.</summary>
    public const double CloseButtonReserve = 30;

    /// <summary>The board's caps in this panel: the board keeps the real proportions of its frets and strings (scaled to the pane, centred, clear of
    /// the legend), so there is no width or aspect cap; the string gap cap of 44 only sets how far the pane's height grows.
    /// Only this panel uses them.</summary>
    internal static readonly FretboardBoardLimits BoardLimits = new(double.PositiveInfinity, 44, double.PositiveInfinity, ClearsLegend: true, RealProportions: true);
    internal const string Font = "Segoe UI";
    /// <summary>The placeholder texts and size: no track selected, and an audio track selected.</summary>
    private const string NoTrackPlaceholderText = "Select a track to see the instrument";
    private const string AudioTrackPlaceholderText = "Audio track — no instrument view";
    private const double NoTrackFontSize = 13;
    private const double NoTrackLift = 8;
    // The legend column beside the board: four rows, the Scales button under them (sizes).
    private const double LegendGap = 14;
    private const double LegendTopMinimum = 10;
    private const int LegendRows = 4;
    private const double LegendRowHeight = 18;
    private const double LegendScalesGap = 6;
    private const double ScalesButtonHeight = 24;
    /// <summary>Text on a lit fret marker (black on the lit green), whatever the theme.</summary>
    private static readonly int[] StandardTuning = { 64, 59, 55, 50, 45, 40 };
    private static readonly int[] InlayFrets = { 3, 5, 7, 9, 12, 15, 17, 19, 21, 24 };

    /// <summary>Fade of a note the instrument shows after it stops: quick release (ms), then the trace over the hold.</summary>
    private const double ReleaseFadeMs = 320;
    private const double QuickReleaseWeight = 0.55;
    private const double TraceWeight = 0.16;
    private const double RecentFloor = 0.06;
    private const double RecentCeiling = 0.7;
    /// <summary>Upcoming movements fade by depth from this brightness, never below the floor.</summary>
    private const double UpcomingStart = 0.78;
    private const double UpcomingStep = 0.17;
    private const double UpcomingFloor = 0.16;


    private readonly AvaloniaDrawContext _draw = new();
    private TrackModel? _track;
    private bool _audioTrack;
    private IReadOnlyList<InstrumentLitNotes.Lit> _lit = Array.Empty<InstrumentLitNotes.Lit>();
    private InstrumentPanelKind _kind;
    private InstrumentPlaybackSnapshot _playing = new();
    private InstrumentPlaybackSnapshot _scratch = new();
    private bool _playbackShown;
    /// <summary>The settings-driven look; key: none; thread: the UI thread. Set by FretboardSettingsBinding.</summary>
    private FretboardSettingsBinding.Look _look = FretboardSettingsBinding.Look.From(new TabForge.Services.AppSettings());
    private double _playbackNowMs;
    /// <summary>The string and fret under the pointer on the fretboard, or null off the board. Key: the pointer position; thread: the UI thread.</summary>
    private (int String, int Fret)? _hover;


    /// <summary>A click on a fret, key or drum piece.</summary>
    public event Action<InstrumentNotePick>? NotePicked;

    /// <summary>The string and fret under the pointer on the fretboard (the ghost dot's cell), or null off the board.</summary>
    internal (int String, int Fret)? Hovered => _hover;

    public InstrumentPanelKind Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    /// <summary>The settings-driven look (frets, keys, preview, sizes). Redraws and re-lays out only when the value changes.</summary>
    public FretboardSettingsBinding.Look Look
    {
        get => _look;
        set { if (_look == value) return; _look = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    /// <summary>True while an audio track is the selected one: the panel shows its no-instrument note instead of an instrument.</summary>
    public bool AudioTrack
    {
        get => _audioTrack;
        set { if (_audioTrack == value) return; _audioTrack = value; InvalidateVisual(); }
    }

    /// <summary>The track whose tuning, capo and fret count the fretboard uses, and the notes lit now. Null: no track is selected, and the panel shows
    /// the placeholder (see <see cref="AudioTrack"/> for the audio case).</summary>
    public void Show(TrackModel? track, IReadOnlyList<InstrumentLitNotes.Lit> lit)
    {
        _track = track;
        _lit = lit;
        InvalidateVisual();
    }

    private IReadOnlyList<int> Tuning => _track is { StringTunings.Count: > 0 } t ? t.StringTunings : StandardTuning;

    /// <summary>The layout input of the board for the current track and look: the band card sizes its pane by it too.</summary>
    internal FretboardGeometryRules.Input FretInput => new(Tuning.Count, _look.Frets, _track?.NumberOfFrets ?? 24, _look.StringSpacing, _look.LeftHanded, _lit.Count > 0 ? _lit[0].Fret : 0, _track?.Kind == TrackKind.Bass);

    private double _contentScale = 1;

    /// <summary>The score's text scale (its zoom times its spacing; see <see cref="ScoreInstrumentScale"/>): the fitted drawing scale is multiplied by it.</summary>
    public double ContentScale
    {
        get => _contentScale;
        set
        {
            if (!double.IsFinite(value) || Math.Abs(value - _contentScale) < 0.001) return;
            _contentScale = value;
            InvalidateVisual();
        }
    }

    /// <summary>The smallest drawing scale of the fretboard in this pane: the Core minimum, lower for a Band card whose fretboard may be small.</summary>
    public double MinDrawingScale
    {
        get => _minDrawingScale;
        set
        {
            if (!double.IsFinite(value) || Math.Abs(value - _minDrawingScale) < 0.001) return;
            _minDrawingScale = value;
            InvalidateVisual();
        }
    }

    private double _minDrawingScale = FretboardScaleRules.MinScale;

    /// <summary>True: the pane paints no backdrop of its own, so what lies under it (a Band card's fill) shows through.</summary>
    public bool TransparentBackdrop
    {
        get => _transparentBackdrop;
        set
        {
            if (_transparentBackdrop == value) return;
            _transparentBackdrop = value;
            InvalidateVisual();
        }
    }

    private bool _transparentBackdrop;

    /// <summary>The fretboard's drawing scale for this pane (see <see cref="FretboardScaleRules"/>): it is drawn in a virtual space this many times smaller.</summary>
    internal double FretboardScale()
    {
        var natural = FretboardScaleRules.NaturalHeight(Tuning.Count, 1.0);
        // A locked pane keeps the drawing at its natural size, whatever the pane's height.
        var fit = FretboardScaleRules.Scale(Bounds.Width, _look.LockInstrumentSize ? natural : Bounds.Height, natural, _minDrawingScale);
        return ScoreInstrumentScale.DrawingScale(fit, _contentScale);
    }

    /// <summary>The virtual area the fretboard may use: the panel at the drawing scale, narrowed when the board would run under the legend column.</summary>
    private UiRect FretboardArea()
    {
        var s = FretboardScale();
        var pane = new UiRect(0, 0, (Bounds.Width - CloseButtonReserve) / s, Bounds.Height / s);
        return FretboardGeometryRules.ContentArea(FretInput, pane, _look.FretboardPosition, BoardLimits);
    }

    /// <summary>The board's layout in the panel's virtual space: the Core rules place it (Left, Centre or Right) beside the legend column.</summary>
    internal FretboardGeometryRules.Layout FretboardLayout() =>
        FretboardGeometryRules.Compute(FretInput, FretboardArea(), _look.FretboardPosition, horizontalOffset: _dragOffset / FretboardScale(), limits: BoardLimits);

    /// <summary>The pane height at which the board of this track stops growing at <paramref name="paneWidth"/> (see <see cref="FretboardSizeRules.WidthLimitedPaneHeight"/>).</summary>
    internal double WidthLimitedPaneHeight(double paneWidth) => FretboardSizeRules.WidthLimitedPaneHeight(FretInput, paneWidth, CloseButtonReserve, BoardLimits);

    /// <summary>The white keys of the keyboard size this look shows.</summary>
    internal int WhiteKeyCount => KeyboardPaneSizeRules.WhiteKeysFor(_look.KeyboardKeys);

    /// <summary>The keys' rectangle in the panel: the keyboard fills the pane between its insets, at most one white key width per 24 DIPs.</summary>
    internal UiRect KeysArea()
    {
        var (lowest, highest) = KeyRange();
        var width = Math.Min(Bounds.Width, KeyboardGeometryRules.WhiteKeysIn(lowest, highest) * KeyboardPaneSizeRules.MaxWhiteKeyWidth);
        return new UiRect((Bounds.Width - width) / 2, KeyboardPaneSizeRules.KeyInset, width, Math.Max(0, Bounds.Height - 2 * KeyboardPaneSizeRules.KeyInset));
    }

    private (int Lowest, int Highest) KeyRange()
    {
        var low = int.MaxValue; var high = int.MinValue; var any = false;
        foreach (var midi in LitMidis()) { any = true; low = Math.Min(low, midi); high = Math.Max(high, midi); }
        return KeyboardGeometryRules.RangeShownFor(_look.KeyboardKeys, low, high, any);
    }

    public override void Render(DrawingContext context)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        Draw(_draw.Use(context), light);
    }

    /// <summary>Draws the pane into <paramref name="d"/> in the light or dark palette; Render calls it, tests call it with a recording context.</summary>
    internal void Draw(IDrawContext d, bool light)
    {
        var ink = AvaloniaPalette.InkOf(light);
        if (!_transparentBackdrop) d.Rect(ink("InstrumentBackground"), null, 0, 0, Bounds.Width, Bounds.Height);
        if (_track is null) { DrawNoTrack(d, ink); return; }
        switch (_kind)
        {
            case InstrumentPanelKind.Fretboard: DrawScaledFretboard(d, ink); break;
            case InstrumentPanelKind.Keyboard: DrawKeyboard(d, ink); break;
            default: DrawDrums(d, ink); break;
        }
        DrawFocusRing(d, ink);
    }

    /// <summary>The placeholder that stands in for the instrument when no track or an audio track is selected, centred.</summary>
    private void DrawNoTrack(IDrawContext d, Func<string, UiColor> ink)
    {
        var text = d.Text(_audioTrack ? AudioTrackPlaceholderText : NoTrackPlaceholderText, Font, NoTrackFontSize, ink("InstrumentMuted"), d.PixelsPerDip);
        d.DrawText(text, Bounds.Width / 2 - text.Width / 2, Bounds.Height / 2 - NoTrackLift - text.Height / 2);
    }


    protected override Size MeasureOverride(Size available)
    {
        // The panel takes the width its parent gives it.
        var width = double.IsFinite(available.Width) ? available.Width : 900;
        return new Size(0, _kind == InstrumentPanelKind.Drums ? Math.Max(PanelHeight, PercussionKeyMapRules.RequiredHeight(width)) : PanelHeight);
    }

    private bool IsLit(int midi)
    {
        foreach (var n in _lit) if (!n.Dead && n.Midi == midi) return true;
        return false;
    }

    /// <summary>The fill of a key or pad that is lit, or null: the playing notes while playback shows them, else the score cursor's notes.</summary>
    private UiColor? LitFill(int midi, Func<string, UiColor> ink)
    {
        if (!_playbackShown) return IsLit(midi) ? ink("InstrumentCurrent") : null;
        if (AnyMidi(_playing.Now, midi) || AnyMidi(_playing.Released, midi)) return ink("InstrumentCurrent");
        if (AnyMidi(_playing.Next, midi)) return ink("InstrumentNext");
        for (var i = 0; i < _playing.Upcoming.Count; i++)
            if (_playing.Upcoming[i].Midi == midi) return ink("InstrumentAccent").WithOpacity(UpcomingBrightness(_playing.UpcomingDepth[i]));
        foreach (var n in _playing.Recent)
            if (n.Midi == midi) return ink("InstrumentPast").WithOpacity(RecentBrightness(n));
        return null;
    }

    private static bool AnyMidi(List<NoteEvent> notes, int midi)
    {
        foreach (var n in notes) if (n.Midi == midi) return true;
        return false;
    }

    /// <summary>The note at a point in the panel, or null off the instrument; the same mapping a click uses.</summary>
    public InstrumentNotePick? Pick(UiPoint point)
    {
        if (_track is null) return null;   // the placeholder has nothing to pick
        switch (_kind)
        {
            case InstrumentPanelKind.Fretboard:
                var scale = FretboardScale();
                if (!FretboardGeometryRules.HitTest(FretInput, FretboardArea(), new UiPoint(point.X / scale, point.Y / scale), out var s, out var fret, _look.FretboardPosition, limits: BoardLimits)) return null;
                return new InstrumentNotePick(_track?.PitchOf(s, fret) ?? Tuning[s] + fret, s, fret);
            case InstrumentPanelKind.Keyboard:
                var (lowest, highest) = KeyRange();
                return KeyboardGeometryRules.KeyAt(KeysArea(), lowest, highest, point) is { } midi ? new InstrumentNotePick(midi, null, null) : null;
            default:
                return PercussionKeyMapRules.NoteAt(Bounds.Width, Bounds.Height, point) is { } note ? new InstrumentNotePick(note, null, null) : null;
        }
    }

    /// <summary>The centre of a fret on a string, in panel coordinates (where a click picks it).</summary>
    public UiPoint FretCentre(int stringIndex, int fret)
    {
        var p = FretboardGeometryRules.PositionOf(FretboardLayout(), stringIndex, fret);
        var s = FretboardScale();
        return new UiPoint(p.X * s, p.Y * s);
    }
}
