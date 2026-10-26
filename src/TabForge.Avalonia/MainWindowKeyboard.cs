using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the window-level key table (HotkeyCatalog.BuildMap over the bindings), the clip, range, highlight and track row key tables (the commands one
//   gesture is bound to in each context), and turning a key press into a command id that MainWindowCommands runs. For the edit verbs
//   (copy, cut, paste, delete, duplicate) the choice among those commands is EditRouting.Pick by the focus target, never a precedence here. A key with no handled command, or a command not safe in text while a text box has focus, is left to
//   the focused control (the editor).
// Does not own: the bindings (HotkeySettings), the commands (MainWindowCommands), the editor's own keys.
// Tests: TabForge.Avalonia.Tests.MainWindowLifetimeTests, TabForge.Avalonia.Tests.MainWindowClipKeysTests, TabForge.Avalonia.Tests.HotkeySweepTests,
//   TabForge.Avalonia.Tests.MainWindowSpaceEscapeRoutingTests.
/// <summary>Window key routing. One per window; the tables are rebuilt by <see cref="Rebuild"/> when the bindings change; UI thread only.</summary>
public sealed class MainWindowKeyboard
{
    // Key -> catalogue key name, read from every enum name so an alias (Avalonia prints Oem1 as OemSemicolon) still finds the
    // catalogue spelling. Built once, read-only, any thread; ceiling: the Key enum.
    private static readonly IReadOnlyDictionary<Key, string> CatalogNameOfKey = BuildCatalogNames();

    private static Dictionary<Key, string> BuildCatalogNames()
    {
        var names = new Dictionary<Key, string>();
        foreach (var enumName in System.Enum.GetNames<Key>())
            if (HotkeyCatalog.TryParse(enumName, out var name, out _)) names.TryAdd(System.Enum.Parse<Key>(enumName), name);
        return names;
    }

    private Dictionary<string, string> _map = new();
    private Dictionary<string, string> _clipMap = new();
    private Dictionary<string, string> _rangeMap = new();
    private Dictionary<string, string> _trackRowMap = new();
    private Dictionary<string, string> _highlightMap = new();
    private readonly MainWindowCommands _commands;
    private readonly Func<bool>? _stopRecording;
    private readonly Func<FocusTarget> _focus;
    private readonly EditRouting _routing;

    /// <param name="stopRecording">Ends a recording in progress when Space is pressed (the window key: Space ends a recording before it
    /// pauses); true when one ended. Null: no recording in this window.</param>
    /// <param name="focus">The focus target the last click set (the window's SelectionModel.Focus); null: nothing is focused, so the window table applies.</param>
    /// <param name="routing">The one edit routing table; null makes a private instance (it keeps no state).</param>
    public MainWindowKeyboard(HotkeySettings bindings, MainWindowCommands commands, Func<bool>? stopRecording = null, Func<FocusTarget>? focus = null, EditRouting? routing = null)
    {
        _commands = commands;
        _stopRecording = stopRecording;
        _focus = focus ?? (() => FocusTarget.None);
        _routing = routing ?? new EditRouting();
        Rebuild(bindings);
    }

    /// <summary>The catalogue spelling of a key press ("Ctrl+Shift+G"), or null for a key the catalogue has no name for. Any window's key table reads presses through this.</summary>
    public static string? GestureOf(Key key, KeyModifiers modifiers)
    {
        if (!CatalogNameOfKey.TryGetValue(key, out var name)) return null;
        var mods = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) mods |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mods |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mods |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) mods |= HotkeyModifiers.Windows;
        return HotkeyCatalog.Format(name, mods);
    }

    /// <summary>Replaces every key table with the gestures of <paramref name="bindings"/> (called when Preferences applies shortcuts).</summary>
    public void Rebuild(HotkeySettings bindings)
    {
        _map = HotkeyCatalog.BuildMap(bindings);
        _clipMap = HotkeyCatalog.BuildMap(bindings, clipContext: true);
        _rangeMap = HotkeyCatalog.BuildMap(bindings, rangeContext: true);
        _trackRowMap = HotkeyCatalog.BuildMap(bindings, trackRowContext: true);
        _highlightMap = HotkeyCatalog.BuildMap(bindings, highlightContext: true);
    }

    /// <summary>The command id a key press maps to, or null. A gesture bound to edit commands (copy, cut, paste, delete, duplicate) in several contexts
    /// runs the one the focus target routes to; the other clip and range commands apply while their guard and focus hold.</summary>
    public string? CommandFor(Key key, KeyModifiers modifiers)
    {
        if (GestureOf(key, modifiers) is not { } gesture) return null;
        var focus = _focus();
        if (_routing.Pick(focus, EditCandidates(gesture)) is { } routed) return routed;
        if (Usable(_clipMap, gesture) is { } clipId && _routing.Classify(clipId) is null) return clipId;
        if (focus == FocusTarget.Loop && Usable(_rangeMap, gesture) is { } rangeId && _routing.Classify(rangeId) is null) return rangeId;
        return _map.GetValueOrDefault(gesture);
    }

    // Every handled, applicable command the gesture is bound to in the window, clip, range and track row tables.
    private IEnumerable<string> EditCandidates(string gesture)
    {
        foreach (var table in new[] { _map, _clipMap, _rangeMap, _trackRowMap, _highlightMap })
            if (Usable(table, gesture) is { } id) yield return id;
    }

    private string? Usable(Dictionary<string, string> table, string gesture) =>
        table.TryGetValue(gesture, out var id) && _commands.Handles(id) && _commands.Applies(id) ? id : null;

    /// <summary>Runs the mapped command; true when the key was used.</summary>
    public bool Route(Key key, KeyModifiers modifiers, object? focused)
    {
        var id = CommandFor(key, modifiers) ?? EscapeStopFor(key, modifiers);
        if (id is null || !_commands.Handles(id) || !_commands.Applies(id)) return false;
        if (BlockedByFocus(key, focused, id)) return false;
        _commands.Run(id);
        return true;
    }

    /// <summary>
    /// Runs Space (Play/Pause, or Play from the start with Shift) before the focused control sees it, so a focused button never takes
    /// the key as a click. Space also ends a recording first. true when the key was used.
    /// </summary>
    public bool RouteBeforeControls(Key key, KeyModifiers modifiers, object? focused)
    {
        if (key != Key.Space || modifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return false;
        // A text box keeps its own space; the transport command's EndRecording stays for Stop, Escape and the menu.
        if (!MainWindowEditingFocus.IsTextInput(focused) && _stopRecording?.Invoke() == true) return true;
        return Route(key, modifiers, focused);
    }

    /// <summary>Escape stops an active transport when no other command or control took it; the key is fixed, not a catalogue binding.</summary>
    private static string? EscapeStopFor(Key key, KeyModifiers modifiers) =>
        key == Key.Escape && modifiers == KeyModifiers.None ? MainWindowTransportMenu.EscapeStopCommand : null;

    /// <summary>Space belongs only to a text box (the text-input rule); Escape to any editing control. The other keys follow the text-box rule.</summary>
    private static bool BlockedByFocus(Key key, object? focused, string id) => key switch
    {
        Key.Space => MainWindowEditingFocus.IsTextInput(focused),
        Key.Escape => MainWindowEditingFocus.IsEditing(focused),
        _ => focused is TextBox && !HotkeyCatalog.AllowsInTextBox(id),
    };
}
