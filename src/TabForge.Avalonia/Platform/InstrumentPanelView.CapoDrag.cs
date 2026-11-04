using System;
using Avalonia.Input;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the fretboard's capo drag: grabbing a drawn full or partial capo, previewing its fret while it moves (no song write)
// and committing once on release through CapoCommitRequested / PartialCapoCommitRequested (one undo step, one engine sync in the owner).
// Does not own: the board geometry (Core FretboardGeometryRules), the capo rules (Core FretboardCapoRules), the capo drawing
// (InstrumentPanelView.FretboardWood.cs) or the undo step (the strip commits through the score editor).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests, PartialCapoWindowTests.
// Threads: UI thread only.
public sealed partial class InstrumentPanelView
{
    /// <summary>The capo fret shown while a drag moves the full capo; null at rest (the drawing reads the track).</summary>
    private int? _capoPreview;

    /// <summary>The partial being dragged (-1 at rest) and the fret its drag shows (null: the track's fret).</summary>
    private int _partialPreviewIndex = -1;
    private int? _partialPreviewFret;

    /// <summary>The drag in progress, or null at rest. Key: the press that grabbed a capo; thread: the UI thread.</summary>
    private FretboardCapoDrag? _capoDrag;

    /// <summary>The capo under the pointer (-1 full, >=0 partial index), or null; the hand cursor follows it. The pointer shape never changes elsewhere.</summary>
    private int? _hoverCapo;

    /// <summary>The hand cursor over a draggable capo, made on first hover (a static would build before Avalonia boots).</summary>
    private Cursor? _capoHandCursor;

    /// <summary>True while the pointer hovers a drawn capo.</summary>
    internal bool CapoHovered => _hoverCapo is not null;

    /// <summary>Raised once when a full-capo drag releases on a changed fret; the owner writes it as one undo step.
    /// Returns whether the song changed.</summary>
    public event Func<int, bool>? CapoCommitRequested;

    /// <summary>Raised once when a partial-capo drag releases on a changed fret (the partial's index in the track's list);
    /// the owner writes it as one undo step. Returns whether the song changed.</summary>
    public event Func<int, int, bool>? PartialCapoCommitRequested;

    /// <summary>The capo fret the board shows now: the drag preview while dragging, else the track's capo.</summary>
    internal int ShownCapo => _capoPreview ?? _track?.Capo ?? 0;

    /// <summary>The fret a partial's drag shows now, or null when it is not being dragged (the drawing reads the track).</summary>
    internal int? PartialPreview(int index) => index == _partialPreviewIndex ? _partialPreviewFret : null;

    /// <summary>True while a capo drag is in progress.</summary>
    internal bool CapoDragging => _capoDrag is not null;

    /// <summary>Arms a capo drag when the press grabs a drawn partial or the full capo; false leaves the gesture to the board drag or the note pick.</summary>
    private bool TryArmCapoDrag(double x, double y)
    {
        if (_kind != InstrumentPanelKind.Fretboard || _track is null || _track.IsDrumTrack) return false;
        var scale = FretboardScale();
        if (!(scale > 0)) return false;
        var layout = FretboardLayout();
        var drawingX = x / scale;
        var drawingY = y / scale;
        if (_track.PartialCapos is { Count: > 0 } partials)
        {
            for (var i = 0; i < partials.Count; i++)
            {
                var partial = partials[i];
                if (partial is null || partial.Fret <= 0) continue;
                if (!FretboardCapoRules.HitTestPartial(layout, drawingX, drawingY, partial.Fret, partial.StringMask)) continue;
                var index = i;
                _partialPreviewIndex = index;
                _partialPreviewFret = partial.Fret;
                _capoDrag = new FretboardCapoDrag(partial.Fret,
                    preview: fret => { _partialPreviewFret = fret; InvalidateVisual(); },
                    commit: fret => PartialCapoCommitRequested?.Invoke(index, fret) == true);
                return true;
            }
        }
        if (!FretboardCapoRules.HitTest(layout, drawingX, drawingY, _track.Capo)) return false;
        _capoPreview = _track.Capo;
        _capoDrag = new FretboardCapoDrag(_track.Capo,
            preview: fret => { _capoPreview = fret; InvalidateVisual(); },
            commit: fret => CapoCommitRequested?.Invoke(fret) == true);
        return true;
    }

    /// <summary>Refreshes the capo hover from the pointer (pane coordinates): the hand cursor follows the bar under it. Redraws only on change.</summary>
    internal void UpdateCapoHover(double x, double y)
    {
        int? hover = null;
        if (_kind == InstrumentPanelKind.Fretboard && _track is { } track && !track.IsDrumTrack && _capoDrag is null)
        {
            var scale = FretboardScale();
            if (scale > 0)
            {
                var layout = FretboardLayout();
                var dx = x / scale;
                var dy = y / scale;
                if (track.PartialCapos is { Count: > 0 } partials)
                {
                    for (var i = 0; i < partials.Count; i++)
                    {
                        var partial = partials[i];
                        if (partial is null || partial.Fret <= 0) continue;
                        if (!FretboardCapoRules.HitTestPartial(layout, dx, dy, PartialPreview(i) ?? partial.Fret, partial.StringMask)) continue;
                        hover = i;
                        break;
                    }
                }
                if (hover is null && FretboardCapoRules.HitTest(layout, dx, dy, ShownCapo)) hover = -1;
            }
        }
        if (hover == _hoverCapo) return;
        _hoverCapo = hover;
        Cursor = hover is not null ? _capoHandCursor ??= new Cursor(StandardCursorType.Hand) : null;
        InvalidateVisual();
    }

    /// <summary>Clears the capo hover (the hand cursor); nothing when already clear.</summary>
    internal void ClearCapoHover()
    {
        if (_hoverCapo is null) return;
        _hoverCapo = null;
        Cursor = null;
        InvalidateVisual();
    }

    /// <summary>Moves the armed capo drag with the pointer: previews the fret under it, writing nothing.</summary>
    private void FollowCapoDrag(double x, double y)
    {
        if (_capoDrag is null) return;
        var scale = FretboardScale();
        if (!(scale > 0)) return;
        _moved = true;
        _capoDrag.Move(FretboardCapoRules.FretAtX(FretboardLayout(), x / scale));
    }

    /// <summary>Ends the armed capo drag: one commit on a changed fret, else nothing. Returns true when a drag ended.</summary>
    private bool EndCapoDrag()
    {
        var drag = _capoDrag;
        if (drag is null) return false;
        _capoDrag = null;
        _capoPreview = null;
        _partialPreviewIndex = -1;
        _partialPreviewFret = null;
        drag.End();
        InvalidateVisual();
        return true;
    }
}
