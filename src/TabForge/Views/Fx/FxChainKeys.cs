using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TabForge.Services;

namespace TabForge.Views.Fx;

// Owns: how the FX chain window routes its commands: a key press to a command (the FX chain scope of the live bindings), a menu
//   item to a command, and the gesture text of those menu items.
// Does not own: what a command does (FxChainWindow.RunFxCommand), the catalogue rows (FxChainFeatureModule), Escape and the list keys.
// Tests: TestFxChainCommandScope.
internal sealed class FxChainKeys
{
    private readonly Func<string, bool> _run;

    public FxChainKeys(Func<string, bool> run) => _run = run;

    /// <summary>The live bindings the tooltip layer reads (the same settings every window shows).</summary>
    private static HotkeySettings Bindings => TooltipShortcuts.Hotkeys;

    /// <summary>The FX chain command a gesture is bound to, or null.</summary>
    public static string? IdFor(HotkeySettings settings, string gesture) =>
        HotkeyCatalog.BuildMap(settings, fxChainContext: true).TryGetValue(HotkeyCatalog.Canonical(gesture), out var id) ? id : null;

    /// <summary>Runs the FX chain command bound to the key press. True when a command used the key.</summary>
    public bool Handle(KeyEventArgs e)
    {
        var id = IdFor(Bindings, WpfHotkeyGestureAdapter.Format(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers));
        if (id is null) return false;
        if (Keyboard.FocusedElement is TextBoxBase && !HotkeyCatalog.AllowsInTextBox(id)) return false;
        return _run(id);
    }

    /// <summary>A menu item that runs command <paramref name="id"/>; its gesture text comes from the live bindings (see <see cref="Apply"/>).</summary>
    public MenuItem Item(string header, string id)
    {
        var item = new MenuItem { Header = header };
        MenuHotkey.SetId(item, id);
        item.Click += (_, _) => _run(id);
        return item;
    }

    /// <summary>Sets the gesture text of every command item under <paramref name="root"/> from the live bindings.</summary>
    public static void Apply(ItemsControl root) => MenuHotkey.Apply(root, id => HotkeyCatalog.DisplayAll(Bindings, id));
}
