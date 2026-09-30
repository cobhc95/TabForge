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

// TabEditorControl: measure override, selection coercion, edit/selection notifications and digit-key helpers.
public sealed partial class TabEditorControl
{
    // ---------- layout ----------

    protected override Size MeasureOverride(Size availableSize)
    {
        var track = Track;
        var systems = track is null ? 1 : GetScoreLayout(track).SystemCount;
        var width = PageWidth;
        return new Size(width * _zoom, (HeaderHeight + systems * SystemHeight + 48) * _zoom);
    }

    private void CoerceSelection()
    {
        var track = Track;
        if (track is null) { SelectedMeasure = SelectedCell = SelectedString = 0; _selecting = false; return; }
        // A range whose bars were removed (undo, bar delete, shorter track) must not point past the song; the
        // window re-applies the shared selection model afterwards, so this only keeps indices valid.
        if (_selecting)
        {
            var last = track.Measures.Count - 1;
            if (last < 0 || Math.Min(_anchorMeasure, _selectionEndMeasure) > last) { _selecting = false; _anchorMeasure = _selectionEndMeasure = -1; }
            else
            {
                if (_anchorMeasure > last) { _anchorMeasure = last; _anchorCell = Math.Max(0, SlotsFor(last) - 1); }
                if (_selectionEndMeasure > last) { _selectionEndMeasure = last; _selectionEndCell = Math.Max(0, SlotsFor(last) - 1); }
            }
        }
        SelectedMeasure = Math.Clamp(SelectedMeasure, 0, Math.Max(0, track.Measures.Count - 1));
        SelectedCell = Math.Clamp(SelectedCell, 0, SlotsFor(SelectedMeasure) - 1);
        SelectedString = Math.Clamp(SelectedString, 0, Math.Max(0, track.StringTunings.Count - 1));
    }

    private void EditedNow()
    {
        if (_project is not null) { _project.IsDirty = true; _project.MarkTimelineChanged(); }
        Edited?.Invoke(this, EventArgs.Empty);
        CoerceSelection();
        InvalidateScoreLayout();
    }

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
}
