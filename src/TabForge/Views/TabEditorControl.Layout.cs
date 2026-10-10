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
using TabForge.Views.Score;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

// Owns: measuring the control (MeasureOverride), the selection coercion after an edit, the edit runner (RunEdit, FinishEdit)
//   through the host or standalone, the digit-entry helpers, and the host adapters the layout, render and input code read
//   (IScoreLayoutHost, IScoreAppearanceHost, IScorePageHost, IScoreRenderHost, IEditorInputHost).
// Does not own: the page maths (TabEditorControl.Geometry.cs), drawing (TabEditorControl.Rendering.cs) and the undo stack (the
//   host's Run).
// Tests: no named test.

public sealed partial class TabEditorControl
{
    // ---------- layout ----------

    protected override Size MeasureOverride(Size availableSize)
    {
        var track = Track;
        var systems = track is null || track.IsAudio ? 1 : Layout.GetLayout(track).SystemCount;   // an audio track has no notation: one short page with its message
        var width = PageWidth;
        return new Size(width * _zoom, (HeaderHeight + systems * SystemHeight + 48) * _zoom);
    }

    private void CoerceSelection()
    {
        var track = Track;
        if (track is null) { SelectedMeasure = SelectedCell = SelectedString = 0; _sel.Clear(); return; }
        // A range whose bars were removed (undo, bar delete, shorter track) must not point past the song; the
        // window re-applies the shared selection model afterwards, so this only keeps indices valid.
        if (_sel.Selecting)
        {
            var last = track.Measures.Count - 1;
            _sel.Coerce(track.Measures.Count, last >= 0 ? Math.Max(0, SlotsFor(last) - 1) : 0);
        }
        SelectedMeasure = Math.Clamp(SelectedMeasure, 0, Math.Max(0, track.Measures.Count - 1));
        SelectedCell = CoerceCell(SelectedMeasure, SelectedCell);
        SelectedString = Math.Clamp(SelectedString, 0, Math.Max(0, track.StringTunings.Count - 1));
    }

    /// <summary>The host the edits run through: the one set, else the window that shows the editor.</summary>
    private IScoreEditHost? EditHostNow => EditHost ?? Window.GetWindow(this) as IScoreEditHost;

    /// <summary>
    /// Runs one editor command: <paramref name="mutate"/> changes the song through the host (<see cref="IScoreEditHost.Run"/>: one undo step,
    /// the dirty flag, the timeline invalidation) and returns whether it changed anything; the selection is coerced, then <see cref="Edited"/> fires.
    /// An editor with no host (a standalone control) reports the start of the edit through <see cref="EditStarting"/> and marks the timeline itself.
    /// </summary>
    private bool RunEdit(Func<bool> mutate, bool markTimeline = true, bool continuesLastStep = false)
    {
        if (_project is null) return false;
        if (!ScoreEditPreparation.TryPrepare(this, mutate, out mutate)) { StatusMessage?.Invoke(this, Services.EditorGuard.Hint); return false; }
        _layout.CaptureMeasureRange(AffectedMeasureRange);
        bool changed;
        if (EditHostNow is { } host) changed = host.Run(_ => ScoreEditPreparation.RunMutation(mutate, _layout), markTimeline, continuesLastStep);
        else
        {
            EditStarting?.Invoke(this, EventArgs.Empty);
            changed = ScoreEditPreparation.RunMutation(mutate, _layout);
            if (changed) MarkSongChanged(markTimeline);
        }
        if (changed) FinishEdit(knownRange: true);
        else _layout.InvalidatePendingMeasures(changed: false);
        return changed;
    }

    /// <summary>The song changed outside a host's edit (a standalone control with no document): flagged changed, playback timing caches rebuilt once unless the change already did that.</summary>
    private void MarkSongChanged(bool markTimeline)
    {
        if (_project is null) return;
        _project.IsDirty = true;
        if (markTimeline) _project.MarkTimelineChanged();
    }

    /// <summary>After the song changed: indices valid again, then the host hears <see cref="Edited"/>, then the score redraws.</summary>
    private void FinishEdit(bool knownRange = false)
    {
        CoerceSelection();
        Edited?.Invoke(this, EventArgs.Empty);
        _layout.InvalidatePendingMeasures(useCapturedRange: knownRange);
    }

    /// <summary>The song already changed through <see cref="DocumentEdits.Run"/> (a paste): the host hears <see cref="Edited"/> and the score redraws.</summary>
    private void EditedNow() => FinishEdit();

    private void SelectionChangedNow(bool seekPlayback = true)
    {
        _selectionShouldSeekPlayback = seekPlayback;
        try { SelectionChanged?.Invoke(this, EventArgs.Empty); }
        finally { _selectionShouldSeekPlayback = true; }
        AnnounceCursor();
        InvalidateVisual();
    }

    private static bool TryDigit(Key key, out int digit)
    {
        digit = -1;
        if (key >= Key.D0 && key <= Key.D9) { digit = (int)key - (int)Key.D0; return true; }
        if (key >= Key.NumPad0 && key <= Key.NumPad9) { digit = (int)key - (int)Key.NumPad0; return true; }
        return false;
    }
    private static bool TryNumpadNavAsDigit(Key key, out int digit)
    {
        // Only keys that cannot be confused with real navigation keys.
        digit = key switch
        {
            Key.Insert => 0,
            Key.End => 1,
            Key.Next => 3,
            Key.Clear => 5,
            Key.Prior => 9,
            _ => -1
        };
        return digit >= 0;
    }

    // ---- what the layout engine reads from this editor ----
    NotationMode IScoreLayoutHost.Notation => Notation;
    ScoreAppearance IScoreLayoutHost.Appearance => Appearance;
    ScoreAppearance IScorePageHost.Appearance => Appearance;
    bool IScoreLayoutHost.HorizontalScroll => HorizontalScroll;
    double IScoreLayoutHost.GridLeft => GridLeft;
    double IScoreLayoutHost.GridWidth => GridWidth;
    double IScoreLayoutHost.FretFontSize => FretFontSize;
    SongProject? IScoreLayoutHost.Project => _project;
    TrackModel? IScoreLayoutHost.Track => Track;

    void IScoreAppearanceHost.AppearanceChanged(ScoreAppearanceChange change)
    {
        if (change == ScoreAppearanceChange.Layout) InvalidateScoreLayout();
        else InvalidateVisual();
    }

    // ---- what the score drawing reads from this editor ----
    double IScorePageHost.StaffGap => StaffGap;
    double IScorePageHost.StringGap => StringGap;
    double IScoreRenderHost.FretFontSize => FretFontSize;
    double IScoreRenderHost.GridLeft => GridLeft;
    double IScoreRenderHost.GridWidth => GridWidth;
    double IScoreRenderHost.HeaderCentreX => HeaderCentreX;
    double IScorePageHost.StaffTop(int system) => StaffTop(system);
    double IScorePageHost.TabTop(int system) => TabTop(system);
    int IScorePageHost.SlotsFor(int measure) => SlotsFor(measure);
    int IScorePageHost.SelectedMeasure => SelectedMeasure;
    int IScoreRenderHost.SelectedCell => SelectedCell;
    int IScoreRenderHost.SelectedString => SelectedString;
    bool IScorePageHost.HideCursor => HideCursor;
    bool IScoreRenderHost.HasSelection => HasSelection;
    (int m1, int c1, int m2, int c2) IScoreRenderHost.SelectionRange() => SelectionRange();
    int IScoreRenderHost.HoverMeasure => _sel.HoverMeasure;
    int IScoreRenderHost.HoverCell => _sel.HoverCell;
    int IScoreRenderHost.ActiveVoiceIndex => _activeVoiceIndex;
    bool IScoreRenderHost.PlaybackActive => _playback.Active;
    int IScoreRenderHost.PlaybackMeasure => _playback.Measure;
    int IScoreRenderHost.PlaybackCell => _playback.Cell;
    double IScoreRenderHost.PlaybackFraction => _playback.Fraction;
    HashSet<(int bar, int cell, int s)> IScoreRenderHost.SoundingNotes => _playback.Sounding;
    HashSet<(int bar, int cell, int s)> IScoreRenderHost.StruckNotes => _playback.Struck;
    (Rect Rect, Brush Brush)? IScoreRenderHost.PlayingBarBand(TrackModel track, ScoreSystemPosition system, ScoreMeasurePosition position) => _playback.PlayingBarBand(track, system, position);
    bool IScoreRenderHost.InHorizontalBand(ScoreMeasurePosition measure) => InHorizontalBand(measure);

    // ---- what the playback overlay reads from this editor ----
    ScoreLayoutEngine IScorePageHost.Layout => _layout;

    // ---- what the mouse input reads from this editor ----
    double IEditorInputHost.HeaderHeight => HeaderHeight;
    double IEditorInputHost.SystemHeight => SystemHeight;
    List<TabCell> IEditorInputHost.CellsFor(MeasureModel measure, bool create) => CellsFor(measure, create);
    void IEditorInputHost.SetCursor(int measure, int cell, int stringIndex) { SelectedMeasure = measure; SelectedCell = cell; SelectedString = stringIndex; }
    void IEditorInputHost.SelectionChangedNow(bool seekPlayback) => SelectionChangedNow(seekPlayback);
    void IEditorInputHost.RaiseContextMenuRequested(ContextMenuEventArgs args) => ContextMenuRequested?.Invoke(this, args);
}
