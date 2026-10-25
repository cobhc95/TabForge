namespace TabForge.Platform;

// Owns: the toolkit-neutral keys the score editor reads (EditorKeyMap.cs), and the fret digit each digit key writes.
// Does not own: the toolkit key mapping (Platform/Wpf/WpfEditorKeys.cs) or what a digit does (TabEditorControl.Keyboard.cs).
// Tests: no dedicated test class.

/// <summary>A key the score editor reads; None for every other key.</summary>
public enum EditorKey
{
    None,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Insert, End, PageDown, Clear, PageUp,
    Space, Apps, F10, Add, Subtract, OemPlus, Divide,
    Left, Right, Up, Down, Home, Tab, Back, Delete,
}

public static class EditorKeyDigits
{
    /// <summary>The digit a main-keyboard or numpad digit key writes.</summary>
    public static bool TryDigit(EditorKey key, out int digit)
    {
        digit = -1;
        if (key >= EditorKey.D0 && key <= EditorKey.D9) { digit = key - EditorKey.D0; return true; }
        if (key >= EditorKey.NumPad0 && key <= EditorKey.NumPad9) { digit = key - EditorKey.NumPad0; return true; }
        return false;
    }

    /// <summary>The digit a numpad key writes with NumLock off; only keys that cannot be confused with real navigation keys.</summary>
    public static bool TryNumpadNavAsDigit(EditorKey key, out int digit)
    {
        digit = key switch
        {
            EditorKey.Insert => 0,
            EditorKey.End => 1,
            EditorKey.PageDown => 3,
            EditorKey.Clear => 5,
            EditorKey.PageUp => 9,
            _ => -1
        };
        return digit >= 0;
    }
}
