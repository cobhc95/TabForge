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

// TabEditorControl: keyboard input.
public sealed partial class TabEditorControl
{
    // ---------- keyboard ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (TryHandleKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    public bool TryHandleKey(Key key, ModifierKeys mods)
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
        if (key == Key.Right && ctrl && !alt) { MoveBar(1); return true; }
        if (key == Key.Left && shift && !alt) { ExtendSelection(-1); MoveBeat(-1); return true; }
        if (key == Key.Right && shift && !alt) { ExtendSelection(1); MoveBeat(1); return true; }
        if (key == Key.Left && !alt) { MoveBeat(-1); return true; }
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
            if (Notation == NotationMode.StaffOnly) EnterStringOnStaff(navDigit);
            else EnterFret(navDigit);
            return true;
        }

        // Insert (Insert beat) is the catalogued command Edit.InsertBeat; Insert / Delete with a modifier belong to the bar,
        // section and track commands, so the plain handlers below only take the bare key.
        if (key == Key.Back && !ctrl && !alt && !shift) { DeleteNote(); return true; }
        if (key == Key.Delete && !ctrl && !alt && !shift) { DeleteBeat(); return true; }

        // Digits (Shift+1..9 are reserved for the effect shortcuts below).
        if (!ctrl && !alt && !shift && TryDigit(key, out var digit))
        {
            if (Notation == NotationMode.StaffOnly) EnterStringOnStaff(digit);
            else EnterFret(digit);
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
            case Key.Divide when !ctrl: ToggleTriplet(); return true;        // numpad /
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

    public void ToggleRepeatOpen()
    {
        if (_project is null || CurrentMeasure() is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        Services.EditCommands.ToggleRepeatOpen(_project, SelectedTrackIndex, SelectedMeasure, out _);
        EditedNow();
    }

    /// <summary>Toggles the repeat end; <paramref name="count"/> (from the menu's prompt) sets the repeat count when turning it on.</summary>
    public void ToggleRepeatClose(int? count = null)
    {
        if (_project is null || CurrentMeasure() is null) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        Services.EditCommands.ToggleRepeatClose(_project, SelectedTrackIndex, SelectedMeasure, count, out _);
        EditedNow();
    }

    /// <summary>
    /// Moves the selected note(s) (the note under the cursor, or every note of the selected beats) to the adjacent string
    /// without changing the pitch: the fret is recalculated from the tuning and capo. <paramref name="delta"/> -1 is the higher
    /// string (one up on the tab), +1 the lower. All or nothing: when any note cannot go (no such string, a fret below 0 or past
    /// the last fret, or the string is taken in that beat) nothing changes and the status line says why. One undo step.
    /// </summary>
    public bool MoveNotesToAdjacentString(int delta)
    {
        var track = Track;
        if (track is null) return false;
        void Say(string text) => StatusMessage?.Invoke(this, text);
        if (track.StringTunings.Count == 0 || track.MidiChannel == 9 || track.Kind == TrackKind.Drums)
        { Say("This track has no strings to move notes between"); return false; }
        var side = delta < 0 ? "higher" : "lower";
        var moves = new List<(TabNote Note, int Target, int Fret)>();
        foreach (var cell in ToolCells())
        {
            var movers = HasSelection ? cell.Notes.ToList() : cell.Notes.Where(n => n.StringIndex == SelectedString).ToList();
            foreach (var note in movers)
            {
                var target = note.StringIndex + delta;
                if (target < 0 || target >= track.StringTunings.Count) { Say($"No change: the note is already on the {(delta < 0 ? "highest" : "lowest")} string"); return false; }
                var fret = track.FretOf(target, MidiOf(track, note.StringIndex, note.Fret));
                if (fret < 0) { Say($"No change: that pitch is below the open {side} string"); return false; }
                if (fret > track.NumberOfFrets) { Say($"No change: that pitch is above the last fret of the {side} string"); return false; }
                if (cell.Notes.Any(other => other.StringIndex == target && !movers.Contains(other))) { Say($"No change: the {side} string already has a note in that beat"); return false; }
                moves.Add((note, target, fret));
            }
        }
        if (moves.Count == 0) { Say("No note to move: put the cursor on a note or select some beats"); return false; }
        EditStarting?.Invoke(this, EventArgs.Empty);
        foreach (var (note, target, fret) in moves)
        {
            var pitch = MidiOf(track, note.StringIndex, note.Fret);
            note.StringIndex = target;
            note.Fret = fret;
            note.MidiValue = pitch;
        }
        if (!HasSelection) SelectedString = moves[0].Target;
        EditedNow();
        Say(moves.Count == 1 ? $"Moved the note to the {side} string" : $"Moved {moves.Count} notes to the {side} string");
        return true;
    }

    /// <summary>Move the note up/down in pitch by semitones (Shift+Up / Shift+Down).</summary>
    public void ShiftPitch(int semitones)
    {
        var track = Track; var cell = CurrentCell();
        if (track is null || cell is null) return;
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        if (note is null) return;
        var fret = note.Fret + semitones;
        var stringIndex = note.StringIndex;
        if (fret < 0)
        {
            // Below the open string: play the same pitch on the next lower string that is free (as a guitarist would).
            var target = MidiOf(track, note.StringIndex, note.Fret) + semitones;
            var lower = Enumerable.Range(note.StringIndex + 1, Math.Max(0, track.StringTunings.Count - note.StringIndex - 1))
                .FirstOrDefault(s => MidiOf(track, s, 0) <= target && cell.Notes.All(n => n.StringIndex != s), -1);
            if (lower < 0) return; // already the lowest playable pitch
            stringIndex = lower;
            fret = target - MidiOf(track, lower, 0);
        }
        EditStarting?.Invoke(this, EventArgs.Empty);
        note.StringIndex = stringIndex;
        note.Fret = fret;
        note.MidiValue = MidiOf(track, stringIndex, fret);
        if (stringIndex != SelectedString) SetPosition(SelectedMeasure, SelectedCell, stringIndex);
        EditedNow();
    }

    private void PreviewNote(TabNote note)
    {
        var track = Track;
        if (track is null) return;
        var midi = note.MidiValue > 0 ? note.MidiValue : MidiOf(track, note.StringIndex, note.Fret);
        NotePreview?.Invoke(this, new NotePreviewEventArgs(midi, track.MidiOutputDeviceId, track.MidiChannel, track.MidiProgram));
    }
}
