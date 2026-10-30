using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's hover overlays and their state: the Write tool's ghost note (what a click would write: the snapped cell at the pointer's row, the
//       Length choice, the chord stack, the scale lock's pitch; translucent track colour, dashed accent edge, the row tinted), the Erase tool's
//       "will be deleted" look and count label, and the dashed cut marker showing where S cuts (one function, CutBeat, decides it for the key and the
//       preview). Everything is derived from the pointer and the selection: nothing here changes the song, and a move repaints only when a cell changes.
// Does not own: the gestures (PianoRollSurface.Input), the split edit (PianoRollEdits.SplitGlue) or the notes' own drawing (PianoRollSurface.Drawing).
// Tests: TabForge.Avalonia.Tests.PianoRollOverlayTests.
public sealed partial class PianoRollSurface
{
    private const double GhostOpacity = 0.4, GhostRowTint = 0.08, EraseTint = 0.6, CutHairlineOpacity = 0.35;
    private const double CutWidth = 2.5, CutDash = 4, CutGap = 3, CutOverhang = 3, EraseLabelPad = 3, EraseLabelGap = 6;

    private bool _hoverAlt;      // Alt was held at the last pointer move (bypasses the snap for the ghost and the cut)
    private bool _pointerIn;     // the pointer is over the surface (the cut marker hides when it leaves the window)
    private bool _ghostOn;       // the pointer is over empty grid (not a note)
    private double _ghostBeat;
    private int _ghostPitch;
    private Rect _overlayBounds;

    /// <summary>The bounds the hover overlays covered before and after the last pointer move (the only area that move changed), for a test to read.</summary>
    internal (Rect Old, Rect New) LastOverlayDirty { get; private set; }

    /// <summary>How many times a pointer move asked for a repaint because an overlay changed.</summary>
    internal int OverlayRepaints { get; private set; }

    /// <summary>A pitch put on the locked scale (the Scale lock): written notes and the ghost use it, so the preview is what a click writes.</summary>
    internal int LockedPitch(int pitch) => LockScale is { Count: > 0 } scale ? PianoRollScaleLock.Snap(pitch, scale) : pitch;

    /// <summary>True while the Write tool's ghost shows.</summary>
    internal bool GhostVisible => Presenter.Tool == PianoRollTool.Select && _gesture == Gesture.None && _ghostOn;

    /// <summary>The ghost's snapped start and pitch (a test reads it); null when it is hidden.</summary>
    internal (double Beat, int Pitch)? Ghost => GhostVisible ? (_ghostBeat, _ghostPitch) : null;

    private void UpdateOverlays(Point p, bool overNote)
    {
        _pointerIn = true;
        var before = _overlayBounds;
        _ghostOn = InGrid(p) && !overNote;
        if (_ghostOn)
        {
            _ghostBeat = PianoRollSnap.Floor(Presenter.Map, Snap(), BeatAt(p.X), _hoverAlt);
            _ghostPitch = LockedPitch(PitchAt(p.Y));
        }
        RepaintIfOverlayMoved(before);
    }

    private void RepaintIfOverlayMoved(Rect before)
    {
        _overlayBounds = OverlayBounds();
        if (before == _overlayBounds) return;
        LastOverlayDirty = (before, _overlayBounds);
        OverlayRepaints++;
        InvalidateVisual();
    }

    private Rect OverlayBounds()
    {
        var bounds = default(Rect);
        if (GhostVisible)
        {
            var high = _ghostPitch;
            if (MusicTheoryService.Chords.TryGetValue(Chord, out var tones))
                foreach (var tone in tones) high = Math.Max(high, PianoRollEdits.ClampPitch(_ghostPitch + tone));
            bounds = new Rect(GridLeft, YOf(high), ViewportWidth, YOf(_ghostPitch) + RowPx - YOf(high));
        }
        if (CutMarkerBeat() is { } at)
            bounds = bounds.Union(new Rect(XOf(at) - CutWidth, GridTop, 2 * CutWidth, ViewportHeight));
        return bounds;
    }

    // ---- The cut marker (S) ----

    /// <summary>Where S cuts: the pointer's grid position snapped (Alt bypasses the snap), or the playhead when the pointer is off the grid. The key and the marker both use it.</summary>
    internal double CutBeat() => SnapBeat(double.IsNaN(_hoverBeat) ? PlayheadBeat() ?? 0 : _hoverBeat, Array.Empty<int>(), _hoverAlt);

    private bool CutMarkerShown => _pointerIn && _gesture == Gesture.None && Presenter.Selected.Count > 0
        && Presenter.Tool is PianoRollTool.Pointer or PianoRollTool.Select;

    /// <summary>The cut position when the marker shows (a tool that shows it, a selected note it crosses, the pointer in the window), else null.</summary>
    internal double? CutMarkerBeat()
    {
        if (!CutMarkerShown) return null;
        var at = CutBeat();
        foreach (var i in Presenter.Selected) if (i < Presenter.Notes.Count && PianoRollEdits.IsCrossedBy(Presenter.Notes[i], at)) return at;
        return null;
    }

    private void DrawCutMarker(IDrawContext d)
    {
        if (CutMarkerBeat() is not { } at) return;
        var x = XOf(at);
        var danger = Ink("Danger");
        d.Line(new UiStroke(danger.WithOpacity(CutHairlineOpacity), Line), x, GridTop, x, GridBottom);
        var dashed = new UiStroke(danger, CutWidth, Dash: CutDash, Gap: CutGap);
        foreach (var i in Presenter.Selected)
        {
            if (i >= Presenter.Notes.Count || !PianoRollEdits.IsCrossedBy(Presenter.Notes[i], at)) continue;
            var y = YOf(Presenter.Notes[i].Pitch);
            d.Line(dashed, x, y - CutOverhang, x, y + RowPx + CutOverhang);
        }
    }

    // ---- The Write tool's ghost ----

    private void DrawGhost(IDrawContext d, UiColor track)
    {
        if (!GhostVisible) return;
        var width = Math.Max(2, NewLength(_ghostBeat) * PixelsPerBeat);
        var accent = Colours.Get("AccentBrush");
        var edge = new UiStroke(accent, Line, Dash: MutedDash, Gap: MutedGap);
        d.Rect(accent.WithOpacity(GhostRowTint), null, GridLeft, YOf(_ghostPitch), ViewportWidth, RowPx);
        d.Rect(track.WithOpacity(GhostOpacity), edge, XOf(_ghostBeat), YOf(_ghostPitch), width, Math.Max(1, RowPx - 1), NoteRadius);
        if (!MusicTheoryService.Chords.TryGetValue(Chord, out var tones)) return;
        foreach (var tone in tones)
        {
            if (tone == 0) continue;
            d.Rect(track.WithOpacity(GhostOpacity), edge, XOf(_ghostBeat), YOf(PianoRollEdits.ClampPitch(_ghostPitch + tone)), width, Math.Max(1, RowPx - 1), NoteRadius);
        }
    }

    // ---- The Erase tool's "will be deleted" look ----

    /// <summary>The notes the Erase box holds now: they stay drawn (tinted) and are deleted only when the button goes up.</summary>
    internal IReadOnlyCollection<int> PendingErase => _erased;

    private readonly Dictionary<int, string> _eraseLabels = new();   // one text per count, made once. Ceiling: one per count seen. Thread: UI only.

    /// <summary>The label beside the Erase box ("3 notes will be deleted"), or null when the box holds no note.</summary>
    internal string? EraseLabel
    {
        get
        {
            if (_gesture != Gesture.Erase || !_moved || _erased.Count == 0) return null;
            if (!_eraseLabels.TryGetValue(_erased.Count, out var text))
                _eraseLabels[_erased.Count] = text = string.Create(CultureInfo.InvariantCulture, $"{_erased.Count} {(_erased.Count == 1 ? "note" : "notes")} will be deleted");
            return text;
        }
    }

    private UiColor EraseFill(PianoRollNote note, UiColor track, UiColor row, int velocity) =>
        UiTokens.Blend(FillOf(note, track, row, velocity), Ink("Danger"), EraseTint);

    private void DrawErased(IDrawContext d, PianoRollNote note, UiColor track, UiColor row, int velocity)
    {
        var danger = new UiStroke(Ink("Danger"), SelectedOutline);   // drawn selected-style, so the plain edge is never used here
        DrawNote(d, note, true, EraseFill(note, track, row, velocity), danger, danger);
    }

    private void DrawEraseLabel(IDrawContext d)
    {
        if (EraseLabel is not { } label) return;
        var box = MarqueeRect();
        var text = d.Text(label, Font, LabelSize, Ink("Danger"), d.PixelsPerDip);
        var x = Math.Clamp(box.Right + EraseLabelGap, GridLeft, Math.Max(GridLeft, GridLeft + ViewportWidth - text.Width - 2 * EraseLabelPad));
        var y = Math.Clamp(box.Bottom + EraseLabelGap, GridTop, Math.Max(GridTop, GridBottom - text.Height - 2 * EraseLabelPad));
        d.Rect(Colours.Get("Panel2Brush"), null, x, y, text.Width + 2 * EraseLabelPad, text.Height + 2 * EraseLabelPad, NoteRadius);
        d.DrawText(text, x + EraseLabelPad, y + EraseLabelPad);
    }
}
