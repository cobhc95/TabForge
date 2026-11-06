using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Menus;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's keys while it has focus. Bindable on the Shortcuts page (defaults in brackets): the tools Edit, Erase, Split, Mute and Glue
//       (1-5), Select tool (V), Split (S), Glue (Ctrl+Shift+G), Snap (Alt+S), Quantize (Q), Mute (M), Transpose dialog (T), Zoom to fit (F), Legato
//       (Ctrl+L), Duplicate (Ctrl+D) and Invert selection (Ctrl+I). Fixed: Escape cancels a drag; in step input Right / Left move the step and Backspace
//       takes it back; Left/Right move the selection by the snap step, Up/Down by a semitone and Shift+Up/Down by an octave; Shift+Left/Right shorten or
//       lengthen by the snap step; Alt+Left/Right nudge by a 64th note; Delete; Ctrl+C, X, V (at the playhead) and A; Esc clears the note selection (a
//       second Esc goes on to the window, which clears the loop). A key the piano roll does not use goes on to the window (Undo, Redo, Space...). Also the right-press that a menu acts on.
// Does not own: the edits (PianoRollPresenter) or the quantize options and the menu (PianoRollView).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollSurface
{
    private const double NudgeBeats = 1.0 / 16;   // a 64th note

    /// <summary>Quantize with the view's options (Q and the menu); set by PianoRollView.</summary>
    internal Action? QuantizeRequested { get; set; }

    /// <summary>Transpose with the view's dialog (T with notes selected); set by PianoRollView.</summary>
    internal Action? TransposeRequested { get; set; }

    /// <summary>Turn snap on or off (S); set by PianoRollView to the magnet button's handler.</summary>
    internal Action? SnapToggleRequested { get; set; }

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

    /// <summary>The live hotkey bindings (the Shortcuts page); defaults when the host gives none.</summary>
    internal Func<TabForge.Services.HotkeySettings>? Hotkeys { get; set; }

    /// <summary>True when the key press is the command's gesture (Hotkey 1 or 2) in the current bindings.</summary>
    internal bool IsBound(string commandId, Key key, KeyModifiers modifiers)
    {
        if (MainWindowKeyboard.GestureOf(key, modifiers) is not { } pressed) return false;
        var settings = Hotkeys?.Invoke() ?? new TabForge.Services.HotkeySettings();
        foreach (var slot in new[] { 1, 2 })
        {
            var bound = TabForge.Services.HotkeyCatalog.GestureFor(settings, commandId, slot);
            if (bound.Length > 0 && string.Equals(TabForge.Services.HotkeyCatalog.Canonical(bound), pressed, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
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
        FlushLaneWheel();   // a key ends a Shift+wheel burst (undo and the rest see the committed values)
        var ctrl = modifiers.HasPrimary();
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        if (IsBound("PianoRoll.Snap", key, modifiers) && SnapToggleRequested is { } toggleSnap) { toggleSnap(); return true; }   // the magnet button's action (focus-gated, rebindable)
        if (IsBound("PianoRoll.SelectTool", key, modifiers)) { Presenter.Tool = PianoRollTool.Pointer; ToolChanged?.Invoke(); InvalidateVisual(); return true; }
        if (IsBound("PianoRoll.Split", key, modifiers)) { SplitSelection(); return true; }
        if (IsBound("PianoRoll.Glue", key, modifiers)) { GlueSelection(); return true; }
        foreach (var (id, tool) in ToolKeys)
            if (IsBound(id, key, modifiers)) { Presenter.Tool = tool; ToolChanged?.Invoke(); InvalidateVisual(); return true; }
        if (StepInput && !ctrl && !alt && StepKey(key)) return true;
        var selected = Presenter.Selected.Count > 0;
        // The piano roll's own commands, through the Shortcuts page's bindings (the defaults are the keys these always had).
        if (IsBound(PianoRollMenus.Quantize, key, modifiers)) { QuantizeRequested?.Invoke(); return true; }
        if (selected && IsBound(PianoRollMenus.Mute, key, modifiers)) { Run(Presenter.ToggleMuteAsync()); return true; }
        if (selected && IsBound(PianoRollMenus.TransposeDialog, key, modifiers)) { TransposeRequested?.Invoke(); return true; }
        if (IsBound(PianoRollMenus.ZoomToFit, key, modifiers)) { ZoomToFit(); return true; }
        if (Presenter.Notes.Count > 0 && IsBound(PianoRollMenus.Legato, key, modifiers)) { Run(Presenter.LegatoAsync()); return true; }
        if (selected && IsBound(PianoRollMenus.Duplicate, key, modifiers)) { Run(Presenter.DuplicateAsync()); return true; }
        if (IsBound(PianoRollMenus.InvertSelection, key, modifiers)) { Presenter.InvertSelection(); SelectionChanged?.Invoke(); InvalidateVisual(); return true; }
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
        }
        if (!ctrl || alt) return false;
        switch (key)
        {
            case Key.C: Presenter.Copy(); return Presenter.Selected.Count > 0;
            case Key.X when Presenter.Selected.Count > 0: Run(Presenter.CutAsync()); return true;
            case Key.V when Presenter.Clipboard.Count > 0: Run(Presenter.PasteAsync(PlayheadBeat() ?? 0)); return true;
            case Key.D when Presenter.Selected.Count > 0: Run(Presenter.DuplicateAsync()); return true;
            case Key.A: Presenter.SelectAll(); SelectionChanged?.Invoke(); InvalidateVisual(); return true;
        }
        return false;
    }

    /// <summary>Raised when a key picked another tool (the toolbar follows).</summary>
    internal Action? ToolChanged { get; set; }

    // Select and Draw merged into the Edit tool (key 1); Select is the Edit tool's enum value.
    private static readonly (string Id, PianoRollTool Tool)[] ToolKeys =
    {
        (PianoRollMenus.EditTool, PianoRollTool.Select), (PianoRollMenus.EraseTool, PianoRollTool.Erase), (PianoRollMenus.SplitTool, PianoRollTool.Split),
        (PianoRollMenus.MuteTool, PianoRollTool.Mute), (PianoRollMenus.GlueTool, PianoRollTool.Glue),
    };
}
