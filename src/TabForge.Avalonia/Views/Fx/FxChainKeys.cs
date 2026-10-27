using System;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Avalonia.Views.Fx;

// Owns: how the FX chain window routes its commands: a key press to a command (the FX chain scope of the live bindings), a menu item to a
//   command, and the gesture text of those menu items.
// Does not own: what a command does (FxChainWindow.RunFxCommand), the catalogue rows (HotkeyCatalog.Features.cs), Escape and the list keys.
// Tests: TabForge.Avalonia.Tests.FxChainKeysTests.
public sealed class FxChainKeys
{
    private readonly Func<string, bool> _run;
    private readonly Func<HotkeySettings> _bindings;

    /// <param name="run">Runs a command by its id; true when it was used.</param>
    /// <param name="bindings">The live key bindings (read at each press and each menu opening, so a changed shortcut works at once).</param>
    public FxChainKeys(Func<string, bool> run, Func<HotkeySettings> bindings)
    {
        _run = run;
        _bindings = bindings;
    }

    /// <summary>The FX chain command a gesture is bound to, or null.</summary>
    public static string? IdFor(HotkeySettings settings, string gesture) =>
        HotkeyCatalog.BuildMap(settings, fxChainContext: true).TryGetValue(HotkeyCatalog.Canonical(gesture), out var id) ? id : null;

    /// <summary>Runs the FX chain command bound to the key press. True when a command used the key; a command not safe in text is left to a focused text box,
    /// and a command on the plug-in list (Delete, Ctrl+C ...) is left alone unless <paramref name="listFocused"/>.</summary>
    public bool Handle(Key key, KeyModifiers modifiers, object? focused, bool listFocused = true)
    {
        if (MainWindowKeyboard.GestureOf(key, modifiers) is not { } gesture || IdFor(_bindings(), gesture) is not { } id) return false;
        if (focused is TextBox && !HotkeyCatalog.AllowsInTextBox(id)) return false;
        if (!listFocused && FxChainIds.NeedsListFocus(id)) return false;
        return _run(id);
    }

    /// <summary>A menu item that runs command <paramref name="id"/>; its gesture text comes from the live bindings (see <see cref="Apply"/>).</summary>
    public MenuItem Item(string header, string id)
    {
        var item = new MenuItem { Header = header, Tag = id };
        item.Click += (_, _) => _run(id);
        ShowGesture(item, id);
        return item;
    }

    private void ShowGesture(MenuItem menu, string id)
    {
        var text = HotkeyCatalog.DisplayAll(_bindings(), id);
        menu.InputGesture = MainWindowMenu.GestureOfText(text);
        AvaloniaMenuTemplates.SetGestureText(menu, text);
    }

    /// <summary>Sets the gesture text of every command item under <paramref name="root"/> from the live bindings.</summary>
    public void Apply(ItemsControl root)
    {
        foreach (var item in root.Items)
        {
            if (item is not MenuItem menu) continue;
            if (menu.Tag is string id) ShowGesture(menu, id);
            Apply(menu);
        }
    }
}
