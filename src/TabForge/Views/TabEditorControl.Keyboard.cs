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

// Owns: the editor's keyboard: OnKeyDown, the key map (HandleKey), notation cycling, the Delete guard (BeforeDelete), and the
//   preview of an entered note.
// Does not own: caret movement (TabEditorControl.Navigation.cs) and mouse input (Views/Score/EditorInputController.cs).
// Tests: TestTabEditorInputScript, TestKeyMoveScrollsCursorIntoView.

public sealed partial class TabEditorControl
{
    // ---------- keyboard ----------

    /// <summary>Asked before the Delete key clears beats; true when the host handled the key itself (deleting whole empty bars).</summary>
    public Func<bool>? BeforeDelete { get; set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (TryHandleKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    /// <summary>Raised after a keyboard key moved the cursor (arrows, Home/End, Page Up/Down), so the host can bring it into view.</summary>
    public event EventHandler? CursorMovedByKey;

    public bool TryHandleKey(Key key, ModifierKeys mods)
    {
        if (!HandleKey(key, mods)) return false;
        if (key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            CursorMovedByKey?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool HandleKey(Key key, ModifierKeys mods)
    {
        var track = Track;
        if (track is null) return false;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var alt = mods.HasFlag(ModifierKeys.Alt);

        if (key == Key.Space && !ctrl && !alt) { PlayRequested?.Invoke(this, EventArgs.Empty); return true; }

        // The keyboard way to the context menu (accessibility): Shift+F10 or the Menu key.
        if ((key == Key.Apps && !ctrl && !alt && !shift) || (key == Key.F10 && shift && !ctrl && !alt))
            return RequestContextMenuAtCaret();

        // Duration: + / - (and the numpad +/-). Shift+- and Shift+Up/Down are separate bindings below,
        // so the plain handlers must not swallow shifted keys (they used to shadow Tenuto and Fade out).
        // The main-keyboard = and - keys are the catalogued commands Note.Shorter / Note.Longer (rebindable); the numpad
        // + / - and the typed "+" (Shift+=) stay here as fixed aliases of the same pair.
        if (!ctrl && !alt && (key == Key.Add || (shift && key == Key.OemPlus))) { PlusDuration(); return true; }
        if (!ctrl && !alt && key == Key.Subtract) { MinusDuration(); return true; }

        // Arrows
        if (key == Key.Left && alt && !ctrl) { MoveToEnteredNote(-1); return true; }
        if (key == Key.Right && alt && !ctrl) { MoveToEnteredNote(1); return true; }
        if (key == Key.Left && ctrl && !alt) { MoveBar(-1); return true; }
        // Ctrl+Right on the last bar goes on to a new bar, like Right at the song end.
        if (key == Key.Right && ctrl && !alt && AppendBarAtEnd is not null && SelectedMeasure >= track.Measures.Count - 1) { AppendBarAtEnd(); return true; }
        if (key == Key.Right && ctrl && !alt) { MoveBar(1); return true; }
        if (key == Key.Left && shift && !alt) { ExtendSelection(-1); MoveBeat(-1, keepSelection: true); return true; }
        if (key == Key.Right && shift && !alt) { ExtendSelection(1); SetPosition(_sel.EndMeasure, _sel.EndCell, SelectedString); return true; }   // the cursor stays on the selection's end (GP5)
        if (key == Key.Left && !alt) { MoveBeat(-1); return true; }
        if (key == Key.Right && !alt && !ctrl && AppendBarAtEnd is not null && AtSongEnd()) { AppendBarAtEnd(); return true; }
        if (key == Key.Right && !alt) { MoveBeat(1); return true; }
        // Up/down move between strings.
        if (key == Key.Up && ctrl && !shift && !alt) { MoveLine(-1); return true; }
        if (key == Key.Down && ctrl && !shift && !alt) { MoveLine(1); return true; }
        if (key == Key.Up && !ctrl && !shift && !alt) { MoveString(-1); return true; }
        if (key == Key.Down && !ctrl && !shift && !alt) { MoveString(1); return true; }

        if (key == Key.Home && ctrl) { MoveToFirstBar(); return true; }
        if (key == Key.End && ctrl) { MoveToLastBar(); return true; }
        if (key == Key.Home) { MoveToBarStart(); return true; }
        if (key == Key.End) { MoveToBarEnd(); return true; }
        if (key == Key.PageUp) { MoveLine(-1); return true; }
        if (key == Key.PageDown) { MoveLine(1); return true; }

        if (key == Key.Tab) { CycleNotation(shift ? -1 : 1); return true; }

        // Numeric keypad with NumLock OFF: 0/1/3/5/9 arrive as Insert/End/Next/Clear/Prior,
        // which never collide with the arrow keys, so they can still write frets.
        // Numpad 2/4/6/8 arrive as Down/Left/Right/Up and must stay navigation.
        if (!ctrl && !alt && !shift && !Keyboard.IsKeyToggled(Key.NumLock) && TryNumpadNavAsDigit(key, out var navDigit))
        {
            ClearSelection();   // typing a fret ends the selection (GP5)
            if (Notation == NotationMode.StaffOnly) _edits.EnterStringOnStaff(navDigit);
            else _edits.EnterFret(navDigit);
            return true;
        }

        // Insert (Insert beat) is the catalogued command Edit.InsertBeat; Insert / Delete with a modifier belong to the bar,
        // section and track commands, so the plain handlers below only take the bare key.
        if (key == Key.Back && !ctrl && !alt && !shift) { _edits.DeleteNote(); return true; }
        if (key == Key.Delete && !ctrl && !alt && !shift) { if (BeforeDelete?.Invoke() != true) _edits.DeleteBeat(); return true; }

        // Digits (Shift+1..9 are reserved for the effect shortcuts below).
        if (!ctrl && !alt && !shift && TryDigit(key, out var digit))
        {
            ClearSelection();   // typing a fret ends the selection (GP5)
            if (Notation == NotationMode.StaffOnly) _edits.EnterStringOnStaff(digit);
            else _edits.EnterFret(digit);
            return true;
        }

        // Single-letter note/effect keys now live in the hotkey catalogue (rebindable, preset-aware);
        // the window runs them after this method declines the key.
        switch (key)
        {
            case Key.A when !ctrl: return false;         // handled by the window (chord dialog)
            case Key.T when !ctrl: return false;         // handled by the window (text dialog)
            case Key.D when !ctrl: return false;         // directions dialog
            case Key.K when !ctrl: return false;         // clef
            case Key.Divide when !ctrl: _edits.ToggleTriplet(); return true;        // numpad /
        }
        return false;
    }

    public void CycleNotation(int direction)
    {
        var values = new[] { NotationMode.TabAndStaff, NotationMode.TabOnly, NotationMode.StaffOnly };
        var i = Array.IndexOf(values, Notation);
        i = ((i + direction) % values.Length + values.Length) % values.Length;
        Notation = values[i];
        InvalidateMeasure();
        InvalidateVisual();
        StatusMessage?.Invoke(this, "Notation: " + Notation switch
        {
            NotationMode.TabAndStaff => "tablature + standard",
            NotationMode.TabOnly => "tablature only",
            _ => "standard notation only"
        });
    }

    private void PreviewNote(TabNote note)
    {
        var track = Track;
        if (track is null || !Services.EditorGuard.CanEdit(track)) return;   // no track, or an audio track (no notation)
        var midi = note.MidiValue > 0 ? note.MidiValue : Score.ScoreEditCommands.MidiOf(track, note.StringIndex, note.Fret);
        var cell = _edits.CurrentCell();
        var ms = cell is not null && Project is { } project ? MusicTime.NoteLengthMs(project, SelectedMeasure, SelectedCell, cell) : 0;
        NotePreview?.Invoke(this, new NotePreviewEventArgs(midi, track.MidiOutputDeviceId, track.MidiChannel, track.MidiProgram, ms));
    }
}
