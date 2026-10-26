using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's keys while it has focus: 1-5 pick a tool (Edit, Erase, Split, Mute, Glue); Escape cancels a drag; Ctrl+L legato; in step input Right / Left move the step and Backspace takes it back; Left/Right move the selection by the snap step, Up/Down by a semitone and
//       Shift+Up/Down by an octave; Shift+Left/Right shorten or lengthen by the snap step; Alt+Left/Right nudge by a 64th note; Delete; Ctrl+C, X,
//       V (at the playhead), D and A; Q quantizes, M mutes, F zooms to fit; Esc clears the note selection (a second Esc goes on to the window, which
//       clears the loop). A key the piano roll does not use goes on to the window (Undo, Redo, Space...). Also the right-press that a menu acts on.
// Does not own: the edits (PianoRollPresenter) or the quantize options and the menu (PianoRollView).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollSurface
{
    private const double NudgeBeats = 1.0 / 16;   // a 64th note

    /// <summary>Quantize with the view's options (Q and the menu); set by PianoRollView.</summary>
    internal Action? QuantizeRequested { get; set; }

    /// <summary>The note a right-click opened the menu on (-1 for empty grid).</summary>
    internal int MenuNote { get; private set; } = -1;

    /// <summary>True when the last right-click was on the key strip (piano keys or note names).</summary>
    internal bool MenuInKeys { get; private set; }

    /// <summary>True when the last right-click was on the ruler (its own menu opens).</summary>
    internal bool MenuInRuler { get; private set; }

    /// <summary>The beat a right-click opened the menu at.</summary>
    internal double MenuBeat { get; private set; }

    /// <summary>A right press at <paramref name="p"/>: a note outside the selection becomes the selection, so the menu acts on it.</summary>
    internal void PrepareMenu(Point p)
    {
        MenuBeat = BeatAt(p.X);
        MenuInRuler = InRuler(p);
        MenuInKeys = InKeys(p);
        var (index, _) = HitNote(p);
        MenuNote = index;
        if (index >= 0 && !Presenter.Selected.Contains(index))
        {
            Presenter.Select(new[] { index });
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _pending) return;
        if (_gesture != Gesture.None)
        {
            if (e.Key != Key.Escape) return;
            CancelGesture();
            e.Handled = true;
            return;
        }
        e.Handled = HandleKey(e.Key, e.KeyModifiers);
    }

    /// <summary>Escape during a drag: the gesture is dropped with nothing applied (the button's release then only lets go).</summary>
    internal void CancelGesture()
    {
        if (_gesture == Gesture.None || _pending) return;
        _loopPreview = null;
        EndGesture();
        InvalidateVisual();
    }

    /// <summary>Runs the piano roll's meaning of a key; false when it has none (the key goes on to the window).</summary>
    internal bool HandleKey(Key key, KeyModifiers modifiers)
    {
        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        if (!ctrl && !alt && ToolOf(key) is { } tool) { Presenter.Tool = tool; ToolChanged?.Invoke(); InvalidateVisual(); return true; }
        if (StepInput && !ctrl && !alt && StepKey(key)) return true;
        var first = Presenter.Selected.Count == 0 ? 0 : Presenter.Selected.Min(i => Presenter.Notes[i].Start);
        switch (key)
        {
            case Key.Left or Key.Right when !ctrl && Presenter.Selected.Count > 0:
                var sign = key == Key.Right ? 1 : -1;
                if (alt) Run(Presenter.MoveAsync(sign * NudgeBeats, 0, "Nudge notes"));
                else if (shift) Run(Presenter.ResizeAsync(sign * StepAt(first)));
                else Run(Presenter.MoveAsync(sign * StepAt(first), 0));
                return true;
            case Key.Up or Key.Down when !ctrl && !alt && Presenter.Selected.Count > 0:
                var semitones = (key == Key.Up ? 1 : -1) * (shift ? 12 : 1);
                Run(Presenter.TransposeAsync(semitones));
                if (Presenter.Selected.FirstOrDefault(-1) is var i and >= 0) Sound(PianoRollEdits.ClampPitch(Presenter.Notes[i].Pitch + semitones));
                return true;
            case Key.Delete or Key.Back when !ctrl && Presenter.Selected.Count > 0:
                Run(Presenter.DeleteAsync());
                return true;
            case Key.Escape when Presenter.Selected.Count > 0 && modifiers == KeyModifiers.None:
                Presenter.ClearSelection();
                SelectionChanged?.Invoke();
                InvalidateVisual();
                return true;
            case Key.Q when !ctrl && !alt:
                QuantizeRequested?.Invoke();
                return true;
            case Key.M when !ctrl && !alt && Presenter.Selected.Count > 0:
                Run(Presenter.ToggleMuteAsync());
                return true;
            case Key.F when !ctrl && !alt:
                ZoomToFit();
                return true;
        }
        if (!ctrl || alt) return false;
        switch (key)
        {
            case Key.C: Presenter.Copy(); return Presenter.Selected.Count > 0;
            case Key.X when Presenter.Selected.Count > 0: Run(Presenter.CutAsync()); return true;
            case Key.V when Presenter.Clipboard.Count > 0: Run(Presenter.PasteAsync(PlayheadBeat() ?? 0)); return true;
            case Key.D when Presenter.Selected.Count > 0: Run(Presenter.DuplicateAsync()); return true;
            case Key.A: Presenter.SelectAll(); SelectionChanged?.Invoke(); InvalidateVisual(); return true;
            case Key.L when Presenter.Notes.Count > 0: Run(Presenter.LegatoAsync()); return true;
        }
        return false;
    }

    /// <summary>Raised when a key picked another tool (the toolbar follows).</summary>
    internal Action? ToolChanged { get; set; }

    private static PianoRollTool? ToolOf(Key key) => key switch
    {
        // 26 Oct: Select and Draw merged into the Edit tool (key 1); the keys renumbered. Retired (tombstone): 2 Draw, 3 Erase, 4 Split, 5 Mute, 6 Glue.
        Key.D1 or Key.NumPad1 => PianoRollTool.Select,
        Key.D2 or Key.NumPad2 => PianoRollTool.Erase,
        Key.D3 or Key.NumPad3 => PianoRollTool.Split,
        Key.D4 or Key.NumPad4 => PianoRollTool.Mute,
        Key.D5 or Key.NumPad5 => PianoRollTool.Glue,
        // Retired 26 Oct (tombstone): key 7 picked the Velocity tool; Alt-drag a note or drag its velocity bar instead.
        _ => null,
    };
}
