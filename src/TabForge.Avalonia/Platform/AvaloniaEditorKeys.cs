using Avalonia.Input;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the Avalonia Key to EditorKey mapping, the twin of the app's WpfEditorKeys.
// Does not own: the digit rules or the key map (TabForge.Core Platform/EditorKey.cs, EditorKeyMap.cs).
// Tests: ScorePreviewEditorTests.

public static class AvaloniaEditorKeys
{
    /// <summary>The editor key for an Avalonia key; None when the editor gives it no meaning.</summary>
    public static EditorKey ToEditorKey(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9) return EditorKey.D0 + (key - Key.D0);
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return EditorKey.NumPad0 + (key - Key.NumPad0);
        return key switch
        {
            Key.Insert => EditorKey.Insert,
            Key.End => EditorKey.End,
            Key.PageDown => EditorKey.PageDown,
            Key.Clear => EditorKey.Clear,
            Key.PageUp => EditorKey.PageUp,
            Key.Space => EditorKey.Space,
            Key.Apps => EditorKey.Apps,
            Key.F10 => EditorKey.F10,
            Key.Add => EditorKey.Add,
            Key.Subtract => EditorKey.Subtract,
            Key.OemPlus => EditorKey.OemPlus,
            Key.Divide => EditorKey.Divide,
            Key.Left => EditorKey.Left,
            Key.Right => EditorKey.Right,
            Key.Up => EditorKey.Up,
            Key.Down => EditorKey.Down,
            Key.Home => EditorKey.Home,
            Key.Tab => EditorKey.Tab,
            Key.Back => EditorKey.Back,
            Key.Delete => EditorKey.Delete,
            _ => EditorKey.None
        };
    }
}