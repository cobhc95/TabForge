using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Services;

namespace TabForge;

/// <summary>No tooltip, menu or status text types a bindable key by hand: every key shown comes from the live bindings.</summary>
public static partial class SelfTest
{
    // A key combination typed into a string: Ctrl+, Alt+ or Shift+ followed by a word, or a bracketed function / navigation key.
    private static readonly Regex HandTypedKey = new(
        @"\x22[^\x22\r\n]*?(?<combo>\b(?:Ctrl|Alt|Shift)\s?\+\s?(?<next>[A-Za-z0-9\[\]/+-]*)|\((?<single>F\d{1,2}|Space|Del|Insert)[\s)+;][^\x22\r\n]*)",
        RegexOptions.Compiled);

    private static void TestNoHardWiredKeyText()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("no tooltip, menu or status text carries a hand-typed key", "no source checkout found", "source-hygiene"); return; }
        var offenders = new List<string>();
        foreach (var file in EnumerateHygieneFiles(root).Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("SelfTest", StringComparison.Ordinal) || name == "HotkeyCatalog.cs" || name.StartsWith("TutorialWindow", StringComparison.Ordinal)) continue;   // the catalogue defines keys; the guide window's own keys are fixed
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                var trimmed = line.TrimStart();
                if (line.Contains("Hotkeys[", StringComparison.Ordinal)) continue;   // the one-time rewrite of keys saved by an older version compares stored keys, it shows nothing
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal) || trimmed.StartsWith("<!--", StringComparison.Ordinal)) continue;
                foreach (Match m in HandTypedKey.Matches(line))
                {
                    // Mouse gestures (Ctrl+click, Ctrl+drag, Ctrl+wheel) are not keys of a command.
                    var next = m.Groups["next"].Value.ToLowerInvariant();
                    if (m.Groups["combo"].Success && (next.StartsWith("click", StringComparison.Ordinal) || next.StartsWith("drag", StringComparison.Ordinal) ||
                        next.StartsWith("mouse", StringComparison.Ordinal) || next.StartsWith("wheel", StringComparison.Ordinal) || next.StartsWith("scroll", StringComparison.Ordinal))) continue;
                    offenders.Add($"{name}:{lineNumber}");
                }
            }
        }
        Check("no tooltip, menu or status text carries a hand-typed key combination (they read the live bindings)", offenders.Count == 0, string.Join(", ", offenders.Take(12)));
    }

    private static void TestKeyTextFollowsBindings()
    {
        var keys = new HotkeySettings();
        Eq("Add track shows its default Hotkey 1 and Hotkey 2", "Ctrl+Alt+T, Ctrl+Shift+Insert", HotkeyCatalog.DisplayAll(keys, "Track.Add"));
        Eq("a tooltip shows both keys", "Add track (Ctrl+Alt+T, Ctrl+Shift+Insert)", HotkeyCatalog.TooltipWithKey(keys, "Add track", "Track.Add"));
        keys.Disable("Track.Add", 2);
        Eq("a cleared Hotkey 2 is not shown", "Add track (Ctrl+Alt+T)", HotkeyCatalog.TooltipWithKey(keys, "Add track", "Track.Add"));
        keys.Set("Track.Add", 1, "Ctrl+Alt+K");
        Eq("a rebound Hotkey 1 is shown at once", "Add track (Ctrl+Alt+K)", HotkeyCatalog.TooltipWithKey(keys, "Add track", "Track.Add"));
        keys.Disable("Track.Add");
        Eq("an unbound command shows no key", "Add track", HotkeyCatalog.TooltipWithKey(keys, "Add track", "Track.Add"));
        var classic = new HotkeySettings();
        HotkeyPresets.Apply(classic, HotkeyPresets.GuitarPro5);
        Eq("the Classic preset keeps the familiar Add track key as Hotkey 1", "Ctrl+Shift+Insert, Ctrl+Alt+T", HotkeyCatalog.DisplayAll(classic, "Track.Add"));

        var missing = ShortcutHelp.Ids.Where(id => HotkeyCatalog.ById(id) is null).ToList();
        Check("every command in the shortcuts help exists in the catalogue", missing.Count == 0, string.Join(", ", missing));
        Check("the shortcuts help shows the live keys", ShortcutHelp.Build(new HotkeySettings()).Contains("Add track Ctrl+Alt+T, Ctrl+Shift+Insert", StringComparison.Ordinal));
    }

    private static void TestAddTrackKeys()
    {
        var map = HotkeyCatalog.BuildMap(new HotkeySettings());
        Check("Ctrl+Alt+T runs Add track", map.TryGetValue("Ctrl+Alt+T", out var a) && a == "Track.Add");
        Check("Ctrl+Shift+Insert still runs Add track", map.TryGetValue("Ctrl+Shift+Insert", out var b) && b == "Track.Add");
        // AltGr arrives as Ctrl+Alt: a text box lets only AllowsInTextBox commands through, so a typed AltGr character is never taken.
        Check("Add track does not run while a text box has focus (AltGr typing is safe)", !HotkeyCatalog.AllowsInTextBox("Track.Add"));
        Check("Ctrl+Alt+T is the Add track key only (no other command uses it)", HotkeyCatalog.All.Count(x => HotkeyCatalog.Canonical(HotkeyCatalog.DefaultFor(x, 1)) == "Ctrl+Alt+T" || HotkeyCatalog.Canonical(HotkeyCatalog.DefaultFor(x, 2)) == "Ctrl+Alt+T") == 1);

        // An old file that binds Ctrl+Alt+T to another command keeps it; Add track does not take it.
        var keys = new HotkeySettings();
        keys.Bindings["View.ZoomIn"] = "Ctrl+Alt+T";
        SettingsMigration.KeepUserKeysOverNewDefaults(keys, HotkeyCatalog.All);
        Check("an old binding of Ctrl+Alt+T is kept, Add track's Hotkey 1 stands aside", keys.IsDisabled("Track.Add", 1) && HotkeyCatalog.BuildMap(keys)["Ctrl+Alt+T"] == "View.ZoomIn");
        Check("Add track keeps Ctrl+Shift+Insert as Hotkey 2", HotkeyCatalog.GestureFor(keys, "Track.Add", 2) == "Ctrl+Shift+Insert");

        // An old file that moved Add track off Ctrl+Shift+Insert keeps its own key and gets no default Hotkey 2.
        var moved = new HotkeySettings();
        moved.Bindings["Track.Add"] = "Ctrl+Alt+K";
        SettingsMigration.DisableSecondSlotWhereFirstWasChanged(moved, HotkeyCatalog.All);
        Eq("a customised Add track key is kept", "Ctrl+Alt+K", HotkeyCatalog.GestureFor(moved, "Track.Add"));
        Eq("a customised Add track gets no default Hotkey 2", "", HotkeyCatalog.GestureFor(moved, "Track.Add", 2));
    }
}
