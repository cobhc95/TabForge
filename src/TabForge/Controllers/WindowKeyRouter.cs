using System.Windows.Input;
using TabForge.Views;

namespace TabForge.Controllers;

/// <summary>
/// Where a key press goes once the window has dealt with its own special cases (Space, zoom, Esc, text boxes, clips):
/// the editor's fixed navigation / entry keys, or a catalogued command. Pulled out of MainWindow so the order is tested
/// without a window.
/// </summary>
internal static class WindowKeyRouter
{
    public enum Target { None, Hotkey, Editor }

    /// <summary>
    /// WPF reports an Alt chord as <see cref="Key.System"/> with the real key in <paramref name="systemKey"/>; the editor and the
    /// key map both need the real key.
    /// </summary>
    public static Key RealKey(Key key, Key systemKey) => key == Key.System ? systemKey : key;

    /// <summary>
    /// Order: a bound Ctrl / Alt chord wins first (so the editor's own arrow handling can never swallow Ctrl+Shift+Up / Down,
    /// Ctrl+Insert, Alt+Shift+Left... that a command is bound to); then the editor's fixed keys; then every other binding.
    /// </summary>
    public static Target Dispatch(Key key, Key systemKey, ModifierKeys mods, TabEditorControl editor,
        IReadOnlyDictionary<string, string> map, Func<string, bool> runHotkey)
    {
        var real = RealKey(key, systemKey);
        var gesture = WpfHotkeyGestureAdapter.Format(real, mods);
        var chord = (mods & (ModifierKeys.Control | ModifierKeys.Alt)) != 0;
        if (chord && map.TryGetValue(gesture, out var chordId) && runHotkey(chordId)) return Target.Hotkey;
        if (editor.TryHandleKey(real, mods)) return Target.Editor;
        if (!chord && map.TryGetValue(gesture, out var id) && runHotkey(id)) return Target.Hotkey;
        // A chord whose command declined (no handler) was already tried; do not run it twice.
        return Target.None;
    }
}
