using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the hotkey sweep: every window or clip catalogue id with a Hotkey 1 runs its handler exactly once through MainWindowKeyboard.Route,
//   no two ids share a gesture in one context, text boxes keep keys not marked text-safe, and the gesture the Shortcuts page shows
//   (HotkeyCatalog.GestureFor) is the gesture the router dispatches on.
// Does not own: the real handlers (each feature's own tests), the track-row, range and FX-chain contexts (not routed by the window).
// Tests: this class.
public class HotkeySweepTests
{
    private static readonly Dictionary<string, Key> KeyByCatalogName = BuildKeyNames();

    // The clip id whose guard applies during the current press; null routes window keys. Per test instance, UI thread.
    private string? ClipUnderTest;

    private static Dictionary<string, Key> BuildKeyNames()
    {
        var names = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumName in Enum.GetNames<Key>())
            if (HotkeyCatalog.TryParse(enumName, out var name, out _)) names.TryAdd(name, Enum.Parse<Key>(enumName));
        return names;
    }

    private static bool TryKeyOf(string gesture, out Key key, out KeyModifiers modifiers)
    {
        key = Key.None;
        modifiers = KeyModifiers.None;
        if (!HotkeyCatalog.TryParse(gesture, out var name, out var mods) || !KeyByCatalogName.TryGetValue(name, out key)) return false;
        if (mods.HasFlag(HotkeyModifiers.Control)) modifiers |= KeyModifiers.Control;
        if (mods.HasFlag(HotkeyModifiers.Alt)) modifiers |= KeyModifiers.Alt;
        if (mods.HasFlag(HotkeyModifiers.Shift)) modifiers |= KeyModifiers.Shift;
        if (mods.HasFlag(HotkeyModifiers.Windows)) modifiers |= KeyModifiers.Meta;
        return true;
    }

    private (MainWindowKeyboard Keyboard, Dictionary<string, int> Runs, HashSet<string> Counted) BuildCountingRouter(HotkeySettings settings)
    {
        var commands = new MainWindowCommands(new Window(), () => { });
        var runs = new Dictionary<string, int>();
        var counted = new HashSet<string>();
        foreach (var action in HotkeyCatalog.All)
        {
            if (commands.Handles(action.Id) || !counted.Add(action.Id)) continue;
            var id = action.Id;
            Action count = () => runs[id] = runs.GetValueOrDefault(id) + 1;
            // A clip key wins over the window table only while its guard applies (a clip is selected): here, while that id is under test.
            if (HotkeyCatalog.IsClipAction(id)) commands.AddWhen(id, count, () => ClipUnderTest == id);
            else commands.Add(id, count);
        }
        // The clip under test has the focus (the last click was on a clip); window keys are routed with nothing focused.
        return (new MainWindowKeyboard(settings, commands, focus: () => ClipUnderTest is null ? FocusTarget.None : FocusTarget.Clips), runs, counted);
    }

    [AvaloniaFact]
    public void EveryRoutedHotkeyRunsItsHandlerExactlyOnce()
    {
        var settings = new HotkeySettings();
        var (keyboard, runs, counted) = BuildCountingRouter(settings);
        var failures = new List<string>();
        foreach (var action in HotkeyCatalog.All.Where(a => counted.Contains(a.Id) && HotkeyCatalog.ContextOf(a.Id) <= 1))
        {
            var shown = HotkeyCatalog.GestureFor(settings, action.Id, 1);
            if (string.IsNullOrWhiteSpace(shown)) continue;
            if (!TryKeyOf(shown, out var key, out var modifiers)) { failures.Add($"{action.Id}: '{shown}' has no key"); continue; }
            runs.Clear();
            ClipUnderTest = HotkeyCatalog.IsClipAction(action.Id) ? action.Id : null;
            keyboard.Route(key, modifiers, null);
            if (runs.GetValueOrDefault(action.Id) != 1 || runs.Values.Sum() != 1)
                failures.Add($"{action.Id} ({shown}) ran: {string.Join(",", runs.Select(r => r.Key + "x" + r.Value))}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [AvaloniaFact]
    public void NoTwoIdsShareAGestureInOneContext()
    {
        var settings = new HotkeySettings();
        var owners = new Dictionary<(int, string), HashSet<string>>();
        foreach (var action in HotkeyCatalog.All)
            foreach (var slot in new[] { 1, 2 })
            {
                var gesture = HotkeyCatalog.GestureFor(settings, action.Id, slot);
                if (string.IsNullOrWhiteSpace(gesture)) continue;
                var slotKey = (HotkeyCatalog.ContextOf(action.Id), HotkeyCatalog.Canonical(gesture));
                if (!owners.TryGetValue(slotKey, out var ids)) owners[slotKey] = ids = new HashSet<string>();
                ids.Add(action.Id);
            }
        var shared = owners.Where(o => o.Value.Count > 1).Select(o => $"{o.Key.Item2}: {string.Join(", ", o.Value)}").ToList();
        Assert.True(shared.Count == 0, string.Join("\n", shared));
    }

    [AvaloniaFact]
    public void TextBoxKeepsKeysThatAreNotTextSafe()
    {
        var settings = new HotkeySettings();
        var (keyboard, runs, counted) = BuildCountingRouter(settings);
        var unsafeAction = HotkeyCatalog.All.First(a => counted.Contains(a.Id) && HotkeyCatalog.ContextOf(a.Id) == 0
            && !HotkeyCatalog.AllowsInTextBox(a.Id) && TryKeyOf(HotkeyCatalog.GestureFor(settings, a.Id, 1), out _, out _));
        Assert.True(TryKeyOf(HotkeyCatalog.GestureFor(settings, unsafeAction.Id, 1), out var key, out var modifiers));
        Assert.False(keyboard.Route(key, modifiers, new TextBox()));
        Assert.Empty(runs);
        Assert.True(keyboard.Route(key, modifiers, null));
        Assert.Equal(1, runs[unsafeAction.Id]);
    }

    [Fact]
    public void EveryCatalogDefaultBindingParses()
    {
        var unparsable = HotkeyCatalog.All
            .SelectMany(action => new[] { action.DefaultGesture, action.DefaultGesture2 })
            .Where(gesture => gesture.Length > 0 && !HotkeyCatalog.TryParse(gesture, out _, out _))
            .ToList();
        Assert.Empty(unparsable);
    }
}
