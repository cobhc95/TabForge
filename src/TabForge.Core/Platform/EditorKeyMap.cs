namespace TabForge.Platform;

// Owns: the score editor's fixed key map, from an EditorKey plus modifiers to the command it runs, in precedence order.
// Does not own: what a command does (TabEditorControl.Keyboard.cs) or the rebindable hotkey catalogue.
// Tests: no dedicated test class.

/// <summary>A fixed editor command; None means the editor declines the key and the window's hotkeys get it.</summary>
public enum EditorCommand
{
    None, Play, ContextMenu, LongerDuration, ShorterDuration,
    PreviousEnteredNote, NextEnteredNote, PreviousBar, NextBar, AppendBar,
    ExtendSelectionLeft, ExtendSelectionRight, PreviousBeat, NextBeat,
    LineUp, LineDown, StringUp, StringDown, FirstBar, LastBar, BarStart, BarEnd,
    NextNotation, PreviousNotation, EnterDigit, DeleteNote, DeleteBeat, ToggleTriplet,
}

/// <summary>The editor state a key rule depends on.</summary>
/// <param name="CanAppendAfterLastBar">A new bar can be appended and the cursor is on the last bar.</param>
/// <param name="CanAppendAtSongEnd">A new bar can be appended and the cursor is at the song end.</param>
/// <param name="NumLockOff">NumLock is off, so numpad 0/1/3/5/9 arrive as navigation keys.</param>
/// <param name="PlusKeyLengthensNotes">"Plus key lengthens notes" is on: the fixed numpad and Shift+= aliases follow it.</param>
public readonly record struct EditorKeyState(bool CanAppendAfterLastBar, bool CanAppendAtSongEnd, bool NumLockOff, bool PlusKeyLengthensNotes = false);

public static class EditorKeyMap
{
    /// <summary>The command for a key; digit is the fret digit for EnterDigit, else -1.</summary>
    public static EditorCommand Resolve(EditorKey key, bool ctrl, bool shift, bool alt, EditorKeyState state, out int digit)
    {
        digit = -1;
        var bare = !ctrl && !alt && !shift;
        if (key == EditorKey.Space && !ctrl && !alt) return EditorCommand.Play;
        // The keyboard way to the context menu (accessibility): Shift+F10 or the Menu key.
        if ((key == EditorKey.Apps && bare) || (key == EditorKey.F10 && shift && !ctrl && !alt)) return EditorCommand.ContextMenu;

        // Numpad + / - and the typed "+" (Shift+=) are fixed aliases of the rebindable Note.Shorter / Note.Longer;
        // shifted keys other than "+" stay free for the Tenuto and Fade out bindings. The setting swaps what + and - do.
        if (!ctrl && !alt && (key == EditorKey.Add || (shift && key == EditorKey.OemPlus)))
            return state.PlusKeyLengthensNotes ? EditorCommand.LongerDuration : EditorCommand.ShorterDuration;
        if (!ctrl && !alt && key == EditorKey.Subtract)
            return state.PlusKeyLengthensNotes ? EditorCommand.ShorterDuration : EditorCommand.LongerDuration;

        if (key == EditorKey.Left && alt && !ctrl) return EditorCommand.PreviousEnteredNote;
        if (key == EditorKey.Right && alt && !ctrl) return EditorCommand.NextEnteredNote;
        if (key == EditorKey.Left && ctrl && !alt) return EditorCommand.PreviousBar;
        // Ctrl+Right on the last bar goes on to a new bar, like Right at the song end.
        if (key == EditorKey.Right && ctrl && !alt) return state.CanAppendAfterLastBar ? EditorCommand.AppendBar : EditorCommand.NextBar;
        if (key == EditorKey.Left && shift && !alt) return EditorCommand.ExtendSelectionLeft;
        if (key == EditorKey.Right && shift && !alt) return EditorCommand.ExtendSelectionRight;
        if (key == EditorKey.Left && !alt) return EditorCommand.PreviousBeat;
        if (key == EditorKey.Right && !alt) return !ctrl && state.CanAppendAtSongEnd ? EditorCommand.AppendBar : EditorCommand.NextBeat;
        if (key == EditorKey.Up && ctrl && !shift && !alt) return EditorCommand.LineUp;
        if (key == EditorKey.Down && ctrl && !shift && !alt) return EditorCommand.LineDown;
        if (key == EditorKey.Up && bare) return EditorCommand.StringUp;
        if (key == EditorKey.Down && bare) return EditorCommand.StringDown;

        if (key == EditorKey.Home) return ctrl ? EditorCommand.FirstBar : EditorCommand.BarStart;
        if (key == EditorKey.End) return ctrl ? EditorCommand.LastBar : EditorCommand.BarEnd;
        if (key == EditorKey.PageUp) return EditorCommand.LineUp;
        if (key == EditorKey.PageDown) return EditorCommand.LineDown;
        if (key == EditorKey.Tab) return shift ? EditorCommand.PreviousNotation : EditorCommand.NextNotation;

        if (bare && state.NumLockOff && EditorKeyDigits.TryNumpadNavAsDigit(key, out digit)) return EditorCommand.EnterDigit;
        // Insert and Delete with a modifier belong to the bar, section and track commands.
        if (key == EditorKey.Back && bare) return EditorCommand.DeleteNote;
        if (key == EditorKey.Delete && bare) return EditorCommand.DeleteBeat;
        // Shift+1..9 are reserved for the effect shortcuts.
        if (bare && EditorKeyDigits.TryDigit(key, out digit)) return EditorCommand.EnterDigit;
        if (key == EditorKey.Divide && !ctrl) return EditorCommand.ToggleTriplet;
        digit = -1;
        return EditorCommand.None;
    }
}
