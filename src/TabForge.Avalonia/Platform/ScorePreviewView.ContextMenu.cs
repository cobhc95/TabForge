using System.Collections.Generic;
using System.Linq;
using Avalonia;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Presenters;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

// Owns: the score's right-click rows: the note menu over a note or inside the selection, the beat menu (Paste and Paste special while the
//   clipboard can paste) over a beat, and the empty-area menu elsewhere. A right-click on a beat outside the selection moves the cursor there
//   first, as the WPF score does, so the menu acts on that beat.
// Does not own: the rows (Core ScoreMenus, ScoreNoteMenus), the hit rule (Core ScorePreviewEditor.ContextHitAt), the menu items (ContextMenuBuilder).
// Tests: TabForge.Avalonia.Tests.ScoreContextMenuTests.
public sealed partial class ScorePreviewView
{
    /// <summary>The right-click rows at <paramref name="at"/> (the view's coordinates); the selection the menu acts on is set first.</summary>
    internal IEnumerable<MenuSpec> ContextRowsAt(Point at)
    {
        var page = PagePoint(at);
        var hit = _editor.ContextHitAt(page.X, page.Y);
        var rows = RowsFor(hit);
        // Inside the tinted loop region: Clear loop on top (the ruler menu's command, undoable).
        if (_probe.LoopTint is { } t && hit.Measure >= 0 && ScoreEditorGeometry.IsInRange(hit.Measure, hit.Cell, t.m1, t.c1, t.m2, t.c2))
            return new[] { new MenuSpec { Id = "Transport.ClearLoopArea", Header = "Clear loop", Shortcut = "Esc twice" }, MenuSpec.Separator() }.Concat(rows);
        return rows;
    }

    private IEnumerable<MenuSpec> RowsFor(ScoreContextHit hit)
    {
        if (hit.OnNote || hit.InsideSelection)
        {
            if (!hit.InsideSelection) SelectBeatForMenu(hit.Measure, hit.Cell, hit.String);
            return ContextMenuRows.ScoreNoteAt(ContextMenus, NoteMenuState());
        }
        if (hit.OverBeat) SelectBeatForMenu(hit.Measure, hit.Cell, hit.String);
        return ContextMenuRows.ScoreAt(ContextMenus, ScoreState(overBeat: hit.OverBeat));
    }

    /// <summary>Moves the cursor to the beat a menu opened on, dropping any range, and repaints the cursor and the tools that show it.</summary>
    private void SelectBeatForMenu(int measure, int cell, int stringIndex)
    {
        _editor.SelectForEdit(measure, cell, stringIndex);
        InvalidateVisual();
        CursorChanged?.Invoke();
    }

    /// <summary>The note menu's reads of the editor: the palette tools' lit and enabled states and the duration rule. CanPaste is set by the host.</summary>
    private ScoreNoteMenuState NoteMenuState() => new(CanPaste: false, ToolState: _editor.ToolState, ToolEnabled: _editor.ToolEnabled,
        CanSetDuration: _editor.Effects.CanSetDurationForTool, Key: ContextMenuRows.Shortcuts());
}
