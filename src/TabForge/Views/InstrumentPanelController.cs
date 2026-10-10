using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>The playback state an instrument redraw reads (a value: no allocation per redraw).</summary>
internal readonly record struct InstrumentFrame(ScoreTimeline? Timeline, double PlayheadMs, bool Playing, bool Paused, VisualOptions Options);

/// <summary>What the instrument panel controller needs from its window.</summary>
internal interface IInstrumentPanelHost : IPaneHost
{
    InstrumentPanel Instrument { get; }
    FrameworkElement ScaleFinderButton { get; }
    FrameworkElement InstrumentOverlay { get; }
    /// <summary>The loop area's first and last bar, or null without one.</summary>
    (int Start, int End)? LoopBars { get; }
    bool IsInitialized { get; }
    /// <summary>Redraws the fretboard / keyboard / drums from the song, the playhead and the options here.</summary>
    void RefreshInstrument();
    /// <summary>What the playback shows now (the timeline, the playhead, the transport state and the visual options).</summary>
    InstrumentFrame Frame { get; }
}

// Owns: the practice display options (note names, left-handed, look-ahead, scale highlight, fret count), the view choice
//   (fretboard / keyboard / drums, per track and for all tracks), the scale finder, the instrument appearance choices,
//   and the fretboard's click / drag gesture (note entry, repositioning).
// Does not own: drawing (InstrumentPanel, InstrumentVisualizer), the right-click menu wiring.
// Tests: TestFretboardGeometry, TestAudioInstrumentPanel, TestTimelineAndInstrumentContextMenuByKeyboard.
internal sealed class InstrumentPanelController
{
    private readonly IInstrumentPanelHost _host;
    /// <summary>Right-click "Show all tracks as" choice for this session; null follows Settings.</summary>
    private string? _instrumentViewOverride;
    /// <summary>Right-click "Show this track as" choices for this session (per track).</summary>
    private readonly Dictionary<TrackModel, string> _trackInstrumentViews = new(ReferenceEqualityComparer.Instance);
    private string? _appliedInstrumentViewSetting;
    private bool _dragArmed;
    private bool _dragging;
    private bool _gestureMoved;
    private Point _dragStart;
    private Point? _scaleButtonAnchor;

    public InstrumentPanelController(IInstrumentPanelHost host) => _host = host;

    public bool LeftHanded { get; set; }
    public bool ShowNoteNames { get; set; }
    public int PreviewHorizon { get; set; } = 4;
    public string? ScaleHighlight { get; set; }
    public int FretboardFrets { get; set; } = 24;

    private AppSettings Settings => _host.Settings;
    private InstrumentPanel Instrument => _host.Instrument;

    public string EffectiveInstrumentView =>
        _host.SelectedTrack is { } track && _trackInstrumentViews.TryGetValue(track, out var own) ? own
        : _instrumentViewOverride ?? Settings.Editing.InstrumentView;

    /// <summary>The view chosen for this track (or all tracks), else the Settings default.</summary>
    public string ViewFor(TrackModel track) =>
        _trackInstrumentViews.TryGetValue(track, out var own) ? own : _instrumentViewOverride ?? Settings.Editing.InstrumentView;

    // ---------- drawing ----------

    /// <summary>Redraws the fretboard / keyboard / drums from the song, the playhead and the options.</summary>
    public void ShowInstrument()
    {
        var audioSelected = _host.SelectedTrack is { IsAudio: true };
        var track = audioSelected ? null : _host.SelectedTrack;   // an audio track has no instrument: the panel shows its no-track state
        Instrument.DrumLabel = track is { Kind: TrackKind.Drums } drums ? midi => DrumMaps.For(drums, midi).Label : null;
        var frame = _host.Frame;
        var state = InstrumentVisualizer.Build(
            _host.Project, track, frame.Timeline, frame.PlayheadMs, frame.Playing, frame.Paused,
            PreviewHorizon, LeftHanded, ShowNoteNames, ScaleHighlight, FretboardFrets, options: frame.Options);
        var editingSelection = InstrumentVisualizer.BuildEditingSelection(
            track, _host.Editor.Effects.CurrentCell(), LeftHanded, ShowNoteNames, ScaleHighlight, FretboardFrets, options: frame.Options);
        Instrument.Title = track?.Name ?? "Instrument"; Instrument.AudioTrack = audioSelected;
        ApplyInstrumentView(state);
        ApplyInstrumentView(editingSelection);
        Instrument.SetState(state);
        Instrument.SetEditingSelection(editingSelection);
    }

    // ---------- scale finder and highlight ----------

    /// <summary>Opens the scale finder on the current selection (score or timeline), else the whole song.</summary>
    public void OpenScaleFinder()
    {
        var chosen = ScaleFinderWindow.Show(_host.Window, _host.Project, _host.SelectedTrack, CurrentScaleRange(), ScaleHighlight,
            Settings.Editing.ScaleHighlightStyle, Settings.Editing.ScaleHighlightColour,
            (style, colour) => SetInstrumentAppearance(scaleStyle: style, scaleColour: colour));
        if (chosen is null) return;
        SetScaleHighlight(chosen == "Off" ? null : chosen);
        _host.SetStatus(chosen == "Off" ? "Scale highlight cleared" : $"Scale highlight: {chosen}");
    }

    private ScaleFinderWindow.Range? CurrentScaleRange()
    {
        var editor = _host.Editor;
        if (editor.HasSelection)
        {
            var (m1, c1, m2, c2) = editor.SelectionCellRange;
            return new ScaleFinderWindow.Range(m1, c1, m2, c2, m1 == m2 ? $"bar {m1 + 1}" : $"bars {m1 + 1}–{m2 + 1}");
        }
        if (_host.LoopBars is var (start, end))
            return new ScaleFinderWindow.Range(start, 0, end, int.MaxValue - 1,
                start == end ? $"bar {start + 1}" : $"bars {start + 1}–{end + 1}");
        return null;
    }

    /// <summary>Highlights a scale ("E Natural Minor") on the fretboard, or clears it with null.</summary>
    public void SetScaleHighlight(string? scale)
    {
        if (Equals(ScaleHighlight, scale)) return;
        ScaleHighlight = scale;
        _host.RefreshInstrument();
        _host.SaveSettings();
    }

    /// <summary>Hotkey / menu: clears the scale highlight.</summary>
    public void ClearScaleHighlight()
    {
        SetScaleHighlight(null);
        _host.SetStatus("Scale highlight cleared");
    }

    /// <summary>Under the fretboard's colour legend; top-right corner for the keyboard and drum views.</summary>
    public void PlaceScaleFinderButton(Point? anchor)
    {
        _scaleButtonAnchor = anchor;
        var button = _host.ScaleFinderButton; var overlay = _host.InstrumentOverlay;
        var width = button.ActualWidth > 0 ? button.ActualWidth : 70;
        // Keep clear of the pane's hide (X) button in the top-right corner.
        var reserve = FretboardGeometry.CornerReserve;
        var left = anchor?.X ?? overlay.ActualWidth - width - 8 - reserve;
        Canvas.SetLeft(button, Math.Max(0, Math.Min(left, overlay.ActualWidth - width - 2 - reserve)));
        Canvas.SetTop(button, anchor?.Y ?? 8);
    }

    public void ReplaceScaleFinderButton() => PlaceScaleFinderButton(_scaleButtonAnchor);

    // ---------- view choice and appearance ----------

    /// <summary>Applies the view choice (or the instrument's natural view) and the keyboard size to a state.</summary>
    public void ApplyInstrumentView(InstrumentVisualState state)
    {
        var natural = InstrumentVisualizer.NaturalKind(_host.SelectedTrack);
        state.Kind = EffectiveInstrumentView switch
        {
            InstrumentViews.Keyboard => InstrumentKind.Keyboard,
            InstrumentViews.Drums => InstrumentKind.Drums,
            InstrumentViews.Fretboard => natural == InstrumentKind.Bass ? InstrumentKind.Bass : InstrumentKind.Guitar,
            _ => natural,
        };
        ApplyAppearance(state, Settings);
    }

    /// <summary>The keyboard size, key colours and fretboard look from the settings (shared by every instrument view, also the Band view's rows).</summary>
    public static void ApplyAppearance(InstrumentVisualState state, AppSettings settings)
    {
        state.KeyboardKeys = settings.Editing.KeyboardKeys;
        state.GreyKeys = settings.Editing.KeyboardKeyColours switch
        {
            KeyboardKeyStyles.Grey => true,
            KeyboardKeyStyles.White => false,
            _ => !VisualTheme.IsLight,
        };
        var ed = settings.Editing;
        state.ScaleStyle = ed.ScaleHighlightStyle;
        state.ScaleColour = ThemeService.ScaleHighlightColour(ed.ScaleHighlightColour, VisualTheme.IsLight);
        state.ScaleStrength = ScaleHighlightStyles.StrengthFactor(ed.ScaleHighlightStrength);
        state.MarkerColour = ThemeService.FretMarkerColour(ed.FretMarkerColour);
        state.MarkerBrightness = FretMarkerLevels.Level(ed.FretMarkerBrightness);
        state.NumberScale = FretNumberSizes.Scale(ed.FretNumberSize);
        state.StringSpacing = FretStringSpacings.Factor(ed.FretStringSpacing);
        state.MarkerScale = MarkerSizing.Scale(ed.FretMarkerSizePercent);
    }

    /// <summary>Appearance choices from the instrument panel's menu or the scale finder; saved for every song and window.</summary>
    public void SetInstrumentAppearance(string? scaleStyle = null, string? scaleColour = null, string? markerColour = null, string? markerBrightness = null, string? numberSize = null, string? stringSpacing = null)
    {
        var ed = Settings.Editing;
        if (stringSpacing is not null) ed.FretStringSpacing = stringSpacing;
        if (numberSize is not null) ed.FretNumberSize = numberSize;
        if (scaleStyle is not null) ed.ScaleHighlightStyle = scaleStyle;
        if (scaleColour is not null) ed.ScaleHighlightColour = scaleColour;
        if (markerColour is not null) ed.FretMarkerColour = markerColour;
        if (markerBrightness is not null) ed.FretMarkerBrightness = markerBrightness;
        _host.RefreshInstrument();
        _host.SaveSettings();
    }

    /// <summary>Sets the view for one track (track given) or for every track (null track).</summary>
    public void SetInstrumentView(string? view, TrackModel? track = null)
    {
        if (track is not null)
        {
            if (view is null) _trackInstrumentViews.Remove(track); else _trackInstrumentViews[track] = view;
        }
        else
        {
            _instrumentViewOverride = view;
            _trackInstrumentViews.Clear();
        }
        _host.RefreshInstrument();
        _host.SetStatus($"Instrument panel: {EffectiveInstrumentView}" + (track is null ? " (all tracks)" : $" ({track.Name})"));
    }

    /// <summary>Hotkey: this track's view, fretboard, keyboard, drums, match the instrument.</summary>
    public void CycleInstrumentView()
    {
        var order = new[] { InstrumentViews.Fretboard, InstrumentViews.Keyboard, InstrumentViews.Drums, InstrumentViews.MatchInstrument };
        var at = Array.IndexOf(order, EffectiveInstrumentView);
        SetInstrumentView(order[(at + 1) % order.Length], _host.SelectedTrack);
    }

    /// <summary>A changed default in Settings replaces any right-click choice made this session.</summary>
    public void SyncInstrumentViewSetting()
    {
        var setting = Settings.Editing.InstrumentView;
        if (_appliedInstrumentViewSetting is not null && _appliedInstrumentViewSetting != setting)
        {
            _instrumentViewOverride = null;
            _trackInstrumentViews.Clear();
            if (_host.IsInitialized) _host.RefreshInstrument();
        }
        _appliedInstrumentViewSetting = setting;
    }

    // ---------- fretboard click / drag ----------

    /// <summary>The TAB line a clicked drum pad writes to: the track's drum map (preset or custom), kept inside the track's lines.</summary>
    internal static int PercussionPadLine(TrackModel drumTrack, int percussion) =>
        Math.Clamp(DrumMaps.For(drumTrack, percussion).TabLine, 0, Math.Max(0, drumTrack.StringTunings.Count - 1));

    /// <summary>Arms a fretboard click/drag gesture; note entry waits until mouse-up so horizontal drags stay edits-free.</summary>
    public void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var editor = _host.Editor;
        // Drum tracks: clicking a sound in the percussion map writes it at the cursor (percussion key map).
        if (_host.SelectedTrack is { } drumTrack && (drumTrack.Kind == TrackKind.Drums || drumTrack.MidiChannel == 9) &&
            Instrument.TryHitPercussion(e.GetPosition(Instrument), out var percussion))
        {
            var line = PercussionPadLine(drumTrack, percussion);
            if (editor.Effects.ToggleFretAtPosition(line, percussion)) editor.Focus();
            e.Handled = true;
            return;
        }
        if (!Instrument.CanRepositionFretboard) return;
        _dragArmed = true;
        _dragging = false;
        _gestureMoved = false;
        _dragStart = e.GetPosition(Instrument);
        Instrument.CaptureMouse();
        e.Handled = true;
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var pointer = e.GetPosition(Instrument);
        var offsetX = pointer.X - _dragStart.X;
        var offsetY = pointer.Y - _dragStart.Y;
        if (Math.Abs(offsetX) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(offsetY) >= SystemParameters.MinimumVerticalDragDistance)
            _gestureMoved = true;

        if (!_dragging)
        {
            if (Math.Abs(offsetX) < SystemParameters.MinimumHorizontalDragDistance || Math.Abs(offsetX) <= Math.Abs(offsetY)) return;
            _dragging = true;
            Instrument.Cursor = Cursors.SizeWE;
        }

        var snapPosition = Instrument.NearestPositionForDrag(offsetX);
        Instrument.UpdatePlacementDrag(offsetX, snapPosition);
        e.Handled = true;
    }

    public void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragArmed) return;
        var wasDragging = _dragging;
        var shouldEnterNote = !wasDragging && !_gestureMoved;
        var point = e.GetPosition(Instrument);
        var finalPosition = Instrument.NearestPositionForDrag(point.X - _dragStart.X);
        _dragArmed = false;
        _dragging = false;
        _gestureMoved = false;
        Instrument.Cursor = null;

        if (wasDragging)
        {
            Instrument.CommitPlacementDrag(finalPosition);
            Settings.Appearance.FretboardPosition = finalPosition.ToString();
            _host.SaveSettings();
            e.Handled = true;
        }
        else
        {
            Instrument.CancelPlacementDrag();
            if (shouldEnterNote && TryEnterFretAt(point)) e.Handled = true;
        }

        if (Mouse.Captured == Instrument) Mouse.Capture(null);
    }

    public void OnLostMouseCapture()
    {
        if (!_dragArmed) return;
        _dragArmed = false;
        _dragging = false;
        _gestureMoved = false;
        Instrument.Cursor = null;
        Instrument.CancelPlacementDrag();
    }

    private bool TryEnterFretAt(Point point)
    {
        var track = _host.SelectedTrack;
        if (track is null || track.Kind is TrackKind.Drums or TrackKind.Keys) return false;
        if (!Instrument.TryHitFret(point, out var stringIndex, out var fret)) return false;
        if (track.StringTunings.Count == 0 || track.Measures.Count == 0) return false;
        if (!_host.Editor.Effects.ToggleFretAtPosition(stringIndex, fret)) return false;
        _host.Editor.Focus();
        return true;
    }
}
