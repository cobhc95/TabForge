using System.Windows.Input;
using TabForge.Services;

namespace TabForge.Views;

// Owns: turning a WPF key event, or a key with its modifiers, into the hotkey gesture text the bindings and menus use.
// Does not own: the bindings table and presets (Views/Preferences/HotkeyPage.cs) or the key routing (Controllers/WindowKeyRouter.cs).
// Tests: TestMenuGestureTextFollowsBindings.

internal static class WpfHotkeyGestureAdapter
{
    public static string FromEvent(KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        return Format(key, Keyboard.Modifiers);
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var portable = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) portable |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) portable |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Alt)) portable |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Windows)) portable |= HotkeyModifiers.Windows;
        return HotkeyCatalog.Format(key.ToString(), portable);
    }
}
