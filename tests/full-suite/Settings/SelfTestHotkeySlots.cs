using System.IO;
using System.Linq;
using TabForge.Services;

namespace TabForge;

/// <summary>The second hotkey slot per command: precedence, per-slot clearing, normalisation, save and load, and the additive migration.</summary>
public static partial class SelfTest
{
    private static void TestHotkeyTwoSlots()
    {
        var keys = new HotkeySettings();
        Eq("Hotkey 2 is empty by default", "", HotkeyCatalog.GestureFor(keys, "File.ExportMidi", 2));

        keys.Set("File.ExportMidi", 2, "Shift+Alt+F2");
        Eq("a Hotkey 2 binding is returned for slot 2", "Shift+Alt+F2", HotkeyCatalog.GestureFor(keys, "File.ExportMidi", 2));
        Eq("Hotkey 1 is untouched by a Hotkey 2 binding", HotkeyCatalog.GestureFor(new HotkeySettings(), "File.ExportMidi"), HotkeyCatalog.GestureFor(keys, "File.ExportMidi"));
        var map = HotkeyCatalog.BuildMap(keys);
        Check("a Hotkey 2 gesture runs its command", map.TryGetValue("Alt+Shift+F2", out var id2) && id2 == "File.ExportMidi");

        // Slot 1 always wins a shared gesture.
        var save = HotkeyCatalog.GestureFor(new HotkeySettings(), "File.Save");
        keys.Set("File.ExportMidi", 2, save);
        Check("a gesture that is some command's Hotkey 1 keeps that command", HotkeyCatalog.BuildMap(keys).TryGetValue(save, out var owner) && owner == "File.Save");

        // Clearing and resetting act per slot.
        keys.Set("File.ExportMidi", 1, "Ctrl+Alt+F9");
        keys.Set("File.ExportMidi", 2, "Ctrl+Alt+F8");
        keys.Disable("File.ExportMidi", 1);
        Eq("clearing Hotkey 1 leaves Hotkey 2", "Ctrl+Alt+F8", HotkeyCatalog.GestureFor(keys, "File.ExportMidi", 2));
        Eq("a cleared Hotkey 1 is empty", "", HotkeyCatalog.GestureFor(keys, "File.ExportMidi", 1));
        keys.Disable("File.ExportMidi", 2);
        keys.Set("File.ExportMidi", 1, "Ctrl+Alt+F9");
        Eq("setting Hotkey 1 does not bring back a cleared Hotkey 2", "", HotkeyCatalog.GestureFor(keys, "File.ExportMidi", 2));
        keys.Reset("File.ExportMidi", 2);
        Eq("Reset of slot 2 keeps slot 1", "Ctrl+Alt+F9", HotkeyCatalog.GestureFor(keys, "File.ExportMidi", 1));
        keys.Set("File.ExportMidi", 2, "Ctrl+Alt+F8");
        keys.Reset("File.ExportMidi");
        Check("Reset of an action returns both slots to their defaults",
            keys.Bindings.Count == 0 && keys.Bindings2.Count == 0 && keys.DisabledActions.Count == 0 && keys.DisabledActions2.Count == 0);

        // A preset switch drops both slots and the layout reads as Custom once a second key exists.
        keys.Set("File.ExportMidi", 2, "Ctrl+Alt+F8");
        Eq("a Hotkey 2 change makes the layout Custom", HotkeyPresets.Custom, HotkeyPresets.Describe(keys));
        HotkeyPresets.Apply(keys, HotkeyPresets.TuxGuitar);
        Check("applying a preset clears Hotkey 2 too", keys.Bindings2.Count == 0 && keys.DisabledActions2.Count == 0);

        // Normalisation: canonical order, unknown ids dropped, a gesture stored once across both slots (slot 1 first), cleared slot wins.
        var raw = new AppSettings();
        raw.Hotkeys.Bindings["File.ExportMidi"] = "Ctrl+Alt+F9";
        raw.Hotkeys.Bindings2["File.ExportMidi"] = "Ctrl+Alt+F9";            // same key as its own Hotkey 1: dropped
        raw.Hotkeys.Bindings2["Section.Previous"] = "Shift+Alt+F2";      // old modifier order: canonical
        raw.Hotkeys.Bindings2["No.Such.Action"] = "F5";
        raw.Hotkeys.Bindings2["File.Open"] = "Ctrl+Alt+F7";
        raw.Hotkeys.DisabledActions2.Add("File.Open");                    // cleared: the binding is removed
        var normal = SettingsValidator.Normalize(raw).Hotkeys;
        Eq("Hotkey 2 gestures are normalised", "Alt+Shift+F2", normal.Get("Section.Previous", 2));
        Check("a duplicate or unknown Hotkey 2 entry is dropped", !normal.Bindings2.ContainsKey("File.ExportMidi") && !normal.Bindings2.ContainsKey("No.Such.Action"));
        Check("a cleared Hotkey 2 slot has no binding", !normal.Bindings2.ContainsKey("File.Open") && normal.IsDisabled("File.Open", 2));

        // Save and load keep both slots.
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-slots-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var settings = new AppSettings();
            settings.Hotkeys.Set("File.ExportMidi", 2, "Ctrl+Alt+F8");
            settings.Hotkeys.Disable("File.Open", 2);
            SettingsFileService.SaveAtomic(path, settings);
            var back = SettingsFileService.Load(path).Hotkeys;
            Check("Hotkey 2 survives save and load", back.Get("File.ExportMidi", 2) == "Ctrl+Alt+F8" && back.IsDisabled("File.Open", 2));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }

        // Default audit: across every preset, with and without reversed +/-, both slots, no gesture sits on two commands of one
        // context and no command has the same gesture twice.
        foreach (var preset in HotkeyPresets.Names)
            foreach (var reverse in new[] { false, true })
            {
                var s = new HotkeySettings { Preset = preset, ReverseDurationKeys = reverse };
                var all = HotkeyCatalog.All
                    .SelectMany(a => new[] { 1, 2 }.Select(slot => (a.Id, Clip: HotkeyCatalog.ContextOf(a.Id), Gesture: HotkeyCatalog.Canonical(HotkeyCatalog.GestureFor(s, a.Id, slot)))))
                    .Where(x => x.Gesture.Length > 0)
                    .GroupBy(x => (x.Clip, x.Gesture.ToUpperInvariant()))
                    .Where(g => g.Count() > 1).Select(g => g.Key.Item2).ToList();
                Check($"preset {preset}{(reverse ? " (reversed +/-)" : "")}: no key twice across both slots", all.Count == 0, string.Join(", ", all));
            }
    }

    private static void TestHotkeySettingsMigration()
    {
        var withDefault2 = new HotkeyAction("Test.A", "Test", "A", "F2", "", "F3");
        var untouched = new HotkeyAction("Test.B", "Test", "B", "F4", "", "F5");
        var cleared = new HotkeyAction("Test.C", "Test", "C", "F6", "", "F7");
        var noSecond = new HotkeyAction("Test.D", "Test", "D", "F8");
        var actions = new[] { withDefault2, untouched, cleared, noSecond };

        var keys = new HotkeySettings();
        keys.Bindings[withDefault2.Id] = "Ctrl+F2";
        keys.Disable(cleared.Id);
        keys.Bindings[noSecond.Id] = "Ctrl+F8";
        SettingsMigration.DisableSecondSlotWhereFirstWasChanged(keys, actions);
        Check("a customised Hotkey 1 disables that action's default Hotkey 2", keys.IsDisabled(withDefault2.Id, 2));
        Check("a cleared Hotkey 1 disables that action's default Hotkey 2", keys.IsDisabled(cleared.Id, 2));
        Check("an untouched action keeps its default Hotkey 2", !keys.IsDisabled(untouched.Id, 2));
        Check("an action without a default Hotkey 2 gets nothing", !keys.IsDisabled(noSecond.Id, 2));
        Eq("the customised Hotkey 1 is unchanged", "Ctrl+F2", keys[withDefault2.Id]);

        // An old settings file (no second-slot fields) loads as Hotkey 1 only, and a file that has them is not migrated again.
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-slotmig-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Hotkeys\":{\"Preset\":\"TabForge\",\"Bindings\":{\"File.ExportMidi\":\"Ctrl+Alt+F9\"},\"DisabledActions\":[\"File.Open\"]}}");
            var old = SettingsFileService.Load(path).Hotkeys;
            Check("an old file keeps its Hotkey 1 customisations", old.Get("File.ExportMidi", 1) == "Ctrl+Alt+F9" && old.IsDisabled("File.Open"));
            Check("an old file has an empty Hotkey 2", old.Bindings2.Count == 0 && old.Get("File.ExportMidi", 2) == "");

            File.WriteAllText(path, "{\"Hotkeys\":{\"Preset\":\"TabForge\",\"Bindings\":{},\"DisabledActions\":[],\"Bindings2\":{\"File.ExportMidi\":\"Ctrl+Alt+F8\"},\"DisabledActions2\":[]}}");
            var current = SettingsFileService.Load(path).Hotkeys;
            Check("a file with Hotkey 2 loads it", current.Get("File.ExportMidi", 2) == "Ctrl+Alt+F8");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
