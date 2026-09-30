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

        // Duration: + / - (and the numpad +/-). Shift+- and Shift+Up/Down are separate bindings below,
        // so the plain handlers must not swallow shifted keys (they used to shadow Tenuto and Fade out).
        if (!ctrl && !alt && (key == Key.Add || key == Key.OemPlus)) { PlusDuration(); return true; }
        if (!ctrl && !alt && key == Key.Subtract) { MinusDuration(); return true; }
        if (!ctrl && !alt && !shift && key == Key.OemMinus) { MinusDuration(); return true; }
        if (ctrl && !alt && (key == Key.Add || key == Key.OemPlus)) { InsertBeat(); return true; }
        if (ctrl && !alt && (key == Key.Subtract || key == Key.OemMinus)) { DeleteBeats(); return true; }

        // Arrows
        if (key == Key.Left && alt && !ctrl) { MoveToEnteredNote(-1); return true; }
        if (key == Key.Right && alt && !ctrl) { MoveToEnteredNote(1); return true; }
        if (key == Key.Left && ctrl) { MoveBar(-1); return true; }
        if (key == Key.Right && ctrl) { MoveBar(1); return true; }
        if (key == Key.Left && shift) { ExtendSelection(-1); MoveBeat(-1); return true; }
        if (key == Key.Right && shift) { ExtendSelection(1); MoveBeat(1); return true; }
        if (key == Key.Left) { MoveBeat(-1); return true; }
        if (key == Key.Right) { MoveBeat(1); return true; }
        if (key == Key.Up && shift && !ctrl && !alt) { ShiftPitch(1); return true; }
        if (key == Key.Down && shift && !ctrl && !alt) { ShiftPitch(-1); return true; }
        if (key == Key.Up && alt && !ctrl) { MoveString(-1); return true; }
        if (key == Key.Down && alt && !ctrl) { MoveString(1); return true; }
        if (key == Key.Up && ctrl && alt) { MoveNoteOnStaff(1); return true; }
        if (key == Key.Down && ctrl && alt) { MoveNoteOnStaff(-1); return true; }
        // Up/down move between strings.
        if (key == Key.Up && ctrl) { MoveLine(-1); return true; }
        if (key == Key.Down && ctrl) { MoveLine(1); return true; }
        if (key == Key.Up) { MoveString(-1); return true; }
        if (key == Key.Down) { MoveString(1); return true; }

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

        if (key == Key.Insert) { InsertBeat(); return true; }
        if (key == Key.Back) { DeleteNote(); return true; }
        if (key == Key.Delete) { DeleteBeat(); return true; }

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

    /// <summary>Ctrl+Alt+Up/Down: move the note to the adjacent string keeping the pitch when possible.</summary>
    public void MoveNoteOnStaff(int direction)
    {
        var track = Track; var cell = CurrentCell();
        if (track is null || cell is null) return;
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        if (note is null) { MoveString(direction); return; }
        var target = Math.Clamp(note.StringIndex - direction, 0, track.StringTunings.Count - 1);
        if (target == note.StringIndex) return;
        var absolute = MidiOf(track, note.StringIndex, note.Fret);
        var fret = track.FretOf(target, absolute);
        if (fret < 0 || fret > track.NumberOfFrets) return;
        EditStarting?.Invoke(this, EventArgs.Empty);
        note.StringIndex = target;
        note.Fret = fret;
        note.MidiValue = absolute;
        SelectedString = target;
        EditedNow();
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
