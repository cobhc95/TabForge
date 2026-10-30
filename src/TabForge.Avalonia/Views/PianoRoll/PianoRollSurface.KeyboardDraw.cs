using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the key strip's paint in theme tokens: each white key its own key (own fill, thin gap line, slightly
//   rounded corners), black keys on top at 60% width, octave labels on C, the held key in the accent, the hovered
//   key brightened one step, the keys of selected pitches in a softer accent tint, and the modifier previews (a
//   dashed accent outline on the hovered key with Ctrl, on the anchor..hover range with Shift). In Names mode the
//   pads take the same lit, tinted and preview states with a legible label. Only the visible rows are drawn;
//   labels and the accent blends come from caches and token blends, never per-frame allocation.
// Does not own: the key state and gestures (PianoRollSurface.Keyboard), the grid paint (PianoRollSurface.Drawing).
// Tests: TabForge.Avalonia.Tests.PianoRollKeyboardTests.
public sealed partial class PianoRollSurface
{
    private void DrawKeys(IDrawContext d)
    {
        var edge = new UiStroke(Ink("PianoRollLineBeat"), Line);
        d.PushClip(0, GridTop, GridLeft, ViewportHeight);
        if (NoteNames)
        {
            DrawNameRows(d, edge);
            d.Pop();
            return;
        }
        var lit = KeyLit();
        d.Rect(Ink("PianoRollKeyWhite"), null, 0, GridTop, GridLeft, ViewportHeight);
        var (first, last) = VisibleRows();
        for (var r = first; r <= last; r++)   // white keys first, each its own key with a thin gap line
        {
            var pitch = Rows[r];
            if (IsBlack(pitch)) continue;
            DrawWhiteKey(d, pitch, r, edge, lit);
        }
        for (var r = first; r <= last; r++)   // black keys on top, shorter (60% of the key width)
        {
            var pitch = Rows[r];
            if (!IsBlack(pitch)) continue;
            DrawBlackKey(d, pitch, r, edge, lit);
        }
        d.Line(edge, GridLeft - Line / 2, GridTop, GridLeft - Line / 2, GridBottom);
        d.Pop();
    }

    private void DrawWhiteKey(IDrawContext d, int pitch, int row, UiStroke edge, UiColor lit)
    {
        var y = GridTop + row * RowPx - ScrollY;
        var fill = KeyFill(pitch, Ink("PianoRollKeyWhite"), lit);
        d.Rect(fill, null, 0, y, GridLeft, RowPx, NoteRadius);
        d.Line(edge, 0, y + RowPx, GridLeft, y + RowPx);   // the thin gap line under each white key
        if (_previewPitches.Contains(pitch)) PreviewOutline(d, 0, y, GridLeft, RowPx, lit);
        DrawKeyLabel(d, pitch, y, RowPx, fill);
    }

    private void DrawBlackKey(IDrawContext d, int pitch, int row, UiStroke edge, UiColor lit)
    {
        var y = GridTop + row * RowPx - ScrollY;
        var width = GridLeft * BlackShare;
        var fill = KeyFill(pitch, Ink("PianoRollKeyBlack"), lit);
        d.Rect(fill, edge, 0, y, width, RowPx, NoteRadius);   // own edge, slightly rounded
        if (_previewPitches.Contains(pitch)) PreviewOutline(d, 0, y, width, RowPx, lit);
        DrawKeyLabel(d, pitch, y, RowPx, fill);
    }

    /// <summary>A key's fill: the accent while held, the softer accent tint while its pitch is selected, brightened one step under the pointer.</summary>
    private UiColor KeyFill(int pitch, UiColor fill, UiColor lit)
    {
        if (pitch == _keyDown) return lit;
        if (_selectedPitches.Contains(pitch)) fill = KeySelectedTint(lit, fill);
        if (pitch == _keyHover) fill = UiTokens.Blend(fill, UiTokens.MeasureInk, HoverStep);
        return fill;
    }

    /// <summary>The dashed accent outline of a modifier preview (this click would add or remove the pitch).</summary>
    private void PreviewOutline(IDrawContext d, double x, double y, double width, double height, UiColor lit) =>
        d.Rect(null, new UiStroke(lit, Line, Dash: MutedDash, Gap: MutedGap), x + Line, y + Line,
            System.Math.Max(1, width - 2 * Line), System.Math.Max(1, height - 2 * Line), NoteRadius);

    private void DrawKeyLabel(IDrawContext d, int pitch, double y, double height, UiColor fill)
    {
        if ((pitch % 12 != 0 && !FoldedLabels) || RowPx < LabelSize - 2) return;   // octave labels stay on C
        var ink = pitch == _keyDown || _selectedPitches.Contains(pitch) ? UiTokens.InkOn(fill) : Ink("PianoRollKeyLabel");
        var text = d.Text(KeyLabel(pitch), Font, LabelSize, ink, d.PixelsPerDip);
        d.DrawText(text, GridLeft - text.Width - LabelGap, y + (height - text.Height) / 2);
    }

    /// <summary>The key strip as note names: a pad per pitch with the same lit, selected-tint and dashed-preview states as piano keys.</summary>
    private void DrawNameRows(IDrawContext d, UiStroke edge)
    {
        var (row, alt, blackRow) = (Ink("PianoRollNameRow"), Ink("PianoRollNameRowAlt"), Ink("PianoRollNameRowBlack"));
        var lit = KeyLit();
        var (first, last) = VisibleRows();
        for (var r = first; r <= last; r++)
        {
            var pitch = Rows[r];
            var y = GridTop + r * RowPx - ScrollY;
            var fill = KeyFill(pitch, IsBlack(pitch) ? blackRow : r % 2 == 0 ? row : alt, lit);
            d.Rect(fill, null, 0, y, GridLeft, RowPx, NoteRadius);
            d.Line(edge, 0, y + RowPx, GridLeft, y + RowPx);
            if (_previewPitches.Contains(pitch)) PreviewOutline(d, 0, y, GridLeft, RowPx, lit);
            if (RowPx < LabelSize - 2) continue;
            var ink = pitch == _keyDown || _selectedPitches.Contains(pitch) || _previewPitches.Contains(pitch)
                ? UiTokens.InkOn(fill)
                : Ink("PianoRollNameText");
            var text = d.Text(NameLabel(pitch), Font, System.Math.Min(LabelSize, RowPx - 1), ink, d.PixelsPerDip);
            d.DrawText(text, GridLeft - text.Width - LabelGap, y + (RowPx - text.Height) / 2);
        }
        d.Line(edge, GridLeft - Line / 2, GridTop, GridLeft - Line / 2, GridBottom);
    }

    /// <summary>A note's drawn fill: the accent when selected (others their neutral colour), a lighter accent tint on previewed pitches.</summary>
    private UiColor NoteDrawFill(PianoRollNote note, bool selected, UiColor track, UiColor row, int velocity)
    {
        var fill = FillOf(note, track, row, velocity);
        if (selected) return KeyLit();
        if (_previewPitches.Contains(note.Pitch)) return UiTokens.Blend(fill, KeyLit(), PreviewTint);
        return fill;
    }
}
