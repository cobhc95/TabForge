using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Menu gesture text, keyboard routing and the string / note-value commands (the editing, menu and hotkey review).</summary>
public static partial class SelfTest
{
    /// <summary>The window's key order through the real router: Alt chords (Key.System), Insert / Delete with modifiers, Ctrl+Shift+arrows.</summary>
    private static void TestKeyRoutingOrder()
    {
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Gtr", Measures = Presets.TemplateFactory.Measures(4) });
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        editor.SetPosition(0, 0, 0);
        var map = HotkeyCatalog.BuildMap(new HotkeySettings());
        string? ran = null;
        bool Run(string id) { ran = id; return true; }
        string? Route(Key key, Key system, ModifierKeys mods, IReadOnlyDictionary<string, string>? m = null)
        {
            ran = null;
            var target = Controllers.WindowKeyRouter.Dispatch(key, system, mods, editor, m ?? map, Run);
            return target == Controllers.WindowKeyRouter.Target.Hotkey ? ran : target == Controllers.WindowKeyRouter.Target.Editor ? "<editor>" : null;
        }

        // Alt chords arrive as Key.System with the real key in SystemKey; the bindings must still be found.
        Eq("routing: Alt+Shift+Left (reported as Key.System) runs Previous section", "Section.Previous", Route(Key.System, Key.Left, ModifierKeys.Alt | ModifierKeys.Shift));
        Eq("routing: Alt+Shift+Right (Key.System) runs Next section", "Section.Next", Route(Key.System, Key.Right, ModifierKeys.Alt | ModifierKeys.Shift));
        Eq("routing: Alt+Up (Key.System) runs Move track up", "Track.MoveUp", Route(Key.System, Key.Up, ModifierKeys.Alt));
        Eq("routing: Ctrl+Alt+Up (Key.System) runs Speed up", "Playback.SpeedUp", Route(Key.System, Key.Up, ModifierKeys.Control | ModifierKeys.Alt));
        Eq("routing: Alt+Left (Key.System), unbound by default, reaches the editor's step-to-entered-note", "<editor>", Route(Key.System, Key.Left, ModifierKeys.Alt));
        var tux = new HotkeySettings { Preset = HotkeyPresets.TuxGuitar };
        Eq("routing: Alt+Left runs Previous section in the TuxGuitar preset (the binding wins over the editor)", "Section.Previous",
            Route(Key.System, Key.Left, ModifierKeys.Alt, HotkeyCatalog.BuildMap(tux)));

        // Insert / Delete with modifiers belong to their own commands, never to Insert beat / Delete beat.
        Eq("routing: Ctrl+Insert runs Insert bar", "Bar.Insert", Route(Key.Insert, Key.None, ModifierKeys.Control));
        Eq("routing: Ctrl+Delete runs Delete bar", "Bar.Delete", Route(Key.Delete, Key.None, ModifierKeys.Control));
        Eq("routing: Shift+Insert runs the section editor", "Section.Edit", Route(Key.Insert, Key.None, ModifierKeys.Shift));
        Eq("routing: Ctrl+Shift+Insert runs Add track", "Track.Add", Route(Key.Insert, Key.None, ModifierKeys.Control | ModifierKeys.Shift));
        Eq("routing: Ctrl+Shift+Delete runs Delete track", "Track.Delete", Route(Key.Delete, Key.None, ModifierKeys.Control | ModifierKeys.Shift));
        Eq("routing: the bare Delete key still empties the beat (editor)", "<editor>", Route(Key.Delete, Key.None, ModifierKeys.None));

        // Ctrl+Shift+Up / Down are Previous / Next track, not the editor's line movement; Shift+Up is the pitch command.
        Eq("routing: Ctrl+Shift+Up runs Previous track", "Track.Previous", Route(Key.Up, Key.None, ModifierKeys.Control | ModifierKeys.Shift));
        Eq("routing: Ctrl+Shift+Down runs Next track", "Track.Next", Route(Key.Down, Key.None, ModifierKeys.Control | ModifierKeys.Shift));
        Eq("routing: Shift+Up runs the pitch command, not a string move", "Note.PitchUp", Route(Key.Up, Key.None, ModifierKeys.Shift));
        Check("editor alone: Ctrl+Shift+Up is not the editor's line move", !editor.TryHandleKey(Key.Up, ModifierKeys.Control | ModifierKeys.Shift));
        Check("editor alone: Alt+Up is not a cursor string move", !editor.TryHandleKey(Key.Up, ModifierKeys.Alt));
        Check("editor alone: Ctrl+Delete and Ctrl+Insert are not beat edits", !editor.TryHandleKey(Key.Delete, ModifierKeys.Control) && !editor.TryHandleKey(Key.Insert, ModifierKeys.Control | ModifierKeys.Shift));
    }

    /// <summary>"Move to higher / lower string" moves the note itself (same pitch, new fret), all or nothing, one undo step.</summary>
    private static void TestMoveNoteToAdjacentString()
    {
        var project = Presets.TemplateFactory.Create("Rock Band");
        var track = project.Tracks[0];
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        var edits = 0; string? status = null;
        editor.EditStarting += (_, _) => edits++;
        editor.StatusMessage += (_, text) => status = text;
        var cell = track.Measures[0].Cells[0];
        cell.DurationDenominator = 4;
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote { StringIndex = 2, Fret = 5, MidiValue = track.PitchOf(2, 5) });
        editor.SetPosition(0, 0, 2, false);
        var pitch = track.PitchOf(2, 5);

        Check("string move: lower string moves the note (index + 1) and recalculates the fret", editor.MoveNotesToAdjacentString(1)
            && cell.Notes.Count == 1 && cell.Notes[0].StringIndex == 3 && track.PitchOf(3, cell.Notes[0].Fret) == pitch && cell.Notes[0].MidiValue == pitch,
            $"string={cell.Notes[0].StringIndex} fret={cell.Notes[0].Fret}");
        Eq("string move: exactly one undo step for one move", 1, edits);
        Eq("string move: the cursor follows the note", 3, editor.SelectedString);
        Check("string move: higher string moves it back to the original fret", editor.MoveNotesToAdjacentString(-1) && cell.Notes[0].StringIndex == 2 && cell.Notes[0].Fret == 5);

        // Pitch not playable: open B string to the high E string would need fret -5.
        cell.Notes[0].StringIndex = 1; cell.Notes[0].Fret = 0; cell.Notes[0].MidiValue = track.PitchOf(1, 0);
        editor.SetPosition(0, 0, 1, false);
        edits = 0; status = null;
        Check("string move: a pitch below the open string is refused with a message and no change",
            !editor.MoveNotesToAdjacentString(-1) && cell.Notes[0].StringIndex == 1 && cell.Notes[0].Fret == 0 && edits == 0 && status is { Length: > 0 }, status);
        // Past the last fret.
        cell.Notes[0].StringIndex = 0; cell.Notes[0].Fret = track.NumberOfFrets; cell.Notes[0].MidiValue = track.PitchOf(0, track.NumberOfFrets);
        editor.SetPosition(0, 0, 0, false);
        status = null;
        Check("string move: a fret past the last fret is refused", !editor.MoveNotesToAdjacentString(1) && cell.Notes[0].StringIndex == 0 && cell.Notes[0].Fret == track.NumberOfFrets && edits == 0 && status is { Length: > 0 });
        Check("string move: no string above the highest string", !editor.MoveNotesToAdjacentString(-1) && edits == 0);

        // The target string is taken in that beat.
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote { StringIndex = 2, Fret = 3, MidiValue = track.PitchOf(2, 3) });
        cell.Notes.Add(new TabNote { StringIndex = 3, Fret = 0, MidiValue = track.PitchOf(3, 0) });
        editor.SetPosition(0, 0, 2, false);
        Check("string move: an occupied target string refuses the move", !editor.MoveNotesToAdjacentString(1) && cell.Notes[0].StringIndex == 2 && cell.Notes[1].StringIndex == 3 && edits == 0);

        // A selection moves every note together (a chord on adjacent strings shifts down as one).
        editor.SelectAll();
        Check("string move: a selected chord shifts to the lower strings together, in one undo step",
            editor.MoveNotesToAdjacentString(1) && cell.Notes.Select(n => n.StringIndex).OrderBy(i => i).SequenceEqual(new[] { 3, 4 }) && edits == 1);
        Check("string move: pitches are unchanged after the chord shift", cell.Notes.Select(n => n.MidiValue).OrderBy(m => m).SequenceEqual(new[] { track.PitchOf(2, 3), track.PitchOf(3, 0) }.OrderBy(m => m)));

        // The plain arrow still moves only the cursor.
        editor.ClearSelection();
        editor.SetPosition(0, 0, 3, false);
        edits = 0;
        var before = cell.Notes.Select(n => (n.StringIndex, n.Fret)).ToList();
        editor.MoveString(-1);
        Check("string move: the cursor-only command leaves the notes alone", edits == 0 && editor.SelectedString == 2 && cell.Notes.Select(n => (n.StringIndex, n.Fret)).SequenceEqual(before));

        var map = HotkeyCatalog.BuildMap(new HotkeySettings());
        Check("string move: default keys Alt+Shift+Up / Down are the move-note commands",
            map.GetValueOrDefault("Alt+Shift+Up") == "Note.MoveStringUp" && map.GetValueOrDefault("Alt+Shift+Down") == "Note.MoveStringDown");
    }

    /// <summary>MainWindow.xaml of the source tree the build sits in (or TABFORGE_SOURCE_DIR); null when it is not around.</summary>
    private static string? FindMainWindowXaml()
    {
        var probe = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (probe is not null)
        {
            var candidate = System.IO.Path.Combine(probe.FullName, "src", "TabForge", "MainWindow.xaml");
            if (System.IO.File.Exists(candidate)) return candidate;
            probe = probe.Parent;
        }
        var envDir = Environment.GetEnvironmentVariable("TABFORGE_SOURCE_DIR");
        return !string.IsNullOrEmpty(envDir) && System.IO.File.Exists(System.IO.Path.Combine(envDir, "MainWindow.xaml"))
            ? System.IO.Path.Combine(envDir, "MainWindow.xaml") : null;
    }

    /// <summary>The two "delete section" actions are named for what they remove, and the destructive one confirms and can be undone.</summary>
    private static void TestSectionDeleteWording()
    {
        var items = Views.TimelineMenus.Leaves(Views.TimelineMenus.Section(new Views.SectionMenuState(null, true, false, false), _ => "")).ToList();
        var destructive = items.First(i => i.Command == Views.TimelineCommand.DeleteSection);
        Check("section delete: the timeline menu says it deletes the bars, with an ellipsis (it asks first)", destructive.Header == "Delete section and its bars…", destructive.Header);
        Check("section delete: its tooltip says it asks first and that Undo restores it", destructive.ToolTip is { } tip && tip.Contains("Asks first") && tip.Contains("Undo"));
        Check("section delete: the confirmation is on by default", new AppSettings().General.ConfirmDeleteSection);
        var xamlPath = FindMainWindowXaml();
        if (xamlPath is null) { Check("section delete: MainWindow.xaml not next to the build, panel wording audit skipped", true); return; }
        System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var button = System.Xml.Linq.XDocument.Load(xamlPath).Descendants(wpf + "Button").FirstOrDefault(b => (string?)b.Attribute("Click") == "MarkerDel_Click");
        Check("section delete: the Sections panel button is \"Remove\" (marker only), not \"Delete\"",
            button is not null && (string?)button.Attribute("Content") == "Remove"
            && ((string?)button.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml/presentation}ToolTip") ?? (string?)button.Attribute("ToolTip") ?? "").Contains("bars and notes stay"));
    }

    /// <summary>The Zoom 100% menu item says what it does, and F3 / F8 are described as the panels they open.</summary>
    private static void TestViewMenuWording()
    {
        var xamlPath = FindMainWindowXaml();
        if (xamlPath is null) Check("view wording: MainWindow.xaml not next to the build, menu audit skipped", true);
        else
        {
            System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var zoom = System.Xml.Linq.XDocument.Load(xamlPath).Descendants(wpf + "MenuItem").First(m => (string?)m.Attribute("Click") == "Zoom100_Click");
            Eq("view wording: the zoom item is called Zoom 100% (it sets 100%, it does not fit the width)", "Zoom 100%", (string?)zoom.Attribute("Header"));
        }
        Eq("view wording: Zoom 100% shows 100% in the zoom box", "100%", MainWindow.ShowZoomOn(new System.Windows.Controls.ComboBox { IsEditable = true }, 1.0));
        var f3 = HotkeyCatalog.ById("View.Multitrack")!; var f8 = HotkeyCatalog.ById("View.Global")!;
        Check("view wording: F3 is described as the track list, not as a score view", f3.DefaultGesture == "F3" && f3.Name == "Show track list" && f3.Description.Contains("track list") && !f3.Description.Contains("in the score"));
        Check("view wording: F8 is described as the arrangement overview toggle", f8.DefaultGesture == "F8" && f8.Name.Contains("arrangement overview") && f8.Description.Contains("Arrangement"));
    }

    /// <summary>The main menu's gesture text is the live binding of the command behind each item, blank when unbound, never typed by hand.</summary>
    private static void TestMenuGestureTextFollowsBindings()
    {
        var xamlPath = FindMainWindowXaml();
        if (xamlPath is null) { Check("menu gestures: MainWindow.xaml not next to the build, source audit skipped", true); return; }

        System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = System.Xml.Linq.XDocument.Load(xamlPath);
        var menuElement = doc.Descendants(wpf + "Menu").First(m => (string?)m.Attribute(x + "Name") == "MainMenu");
        Check("menu gestures: no main-menu item types its own key text (InputGestureText)",
            !menuElement.DescendantsAndSelf().Any(e => e.Attribute("InputGestureText") is not null));

        // Build the same tree as real MenuItems (header + command id), then walk it the way the window does.
        var ids = new List<string>();
        MenuItem Build(System.Xml.Linq.XElement e)
        {
            var item = new MenuItem { Header = (string?)e.Attribute("Header") };
            var idAttr = e.Attributes().FirstOrDefault(a => a.Name.LocalName == "MenuHotkey.Id");
            if (idAttr is not null) { MenuHotkey.SetId(item, idAttr.Value); ids.Add(idAttr.Value); }
            foreach (var child in e.Elements(wpf + "MenuItem")) item.Items.Add(Build(child));
            return item;
        }
        var menu = new Menu();
        foreach (var top in menuElement.Elements(wpf + "MenuItem")) menu.Items.Add(Build(top));
        Check("menu gestures: the main menu carries command ids on its key-bearing items", ids.Count > 60, $"{ids.Count} ids");
        var unknown = ids.Where(id => HotkeyCatalog.ById(id) is null).ToList();
        Check("menu gestures: every id names a catalogued command", unknown.Count == 0, string.Join(", ", unknown));

        IEnumerable<MenuItem> All(ItemsControl root) => root.Items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(All(i)));
        string Live(HotkeySettings keys, string id) => HotkeyCatalog.Display(HotkeyCatalog.GestureFor(keys, id));
        void Verify(string scenario, HotkeySettings keys)
        {
            MenuHotkey.Apply(menu, id => Live(keys, id));
            var wrong = All(menu).Where(i => MenuHotkey.GetId(i) is { } id && i.InputGestureText != Live(keys, id))
                .Select(i => $"{i.Header}={i.InputGestureText}").ToList();
            Check($"menu gestures ({scenario}): every item's key text equals the live binding", wrong.Count == 0, string.Join("; ", wrong));
        }
        string Text(string header) => All(menu).First(i => (string?)i.Header == header).InputGestureText;

        var keys = new HotkeySettings();
        Verify("defaults", keys);
        // The labels that used to be wrong.
        Eq("menu gestures: Insert beat shows Insert (Ctrl + zooms)", "Insert", Text("_Insert beat"));
        Eq("menu gestures: Double dotting shows nothing (Ctrl+. is Stop)", "", Text("Double dotting"));
        Eq("menu gestures: Previous section shows Alt+Shift+Left", "Alt+Shift+Left", Text("Previous section"));
        Eq("menu gestures: Next section shows Alt+Shift+Right", "Alt+Shift+Right", Text("Next section"));
        Eq("menu gestures: Triplet feel has no key text", "", Text("Triplet _feel"));
        Eq("menu gestures: Stylesheet has no key text", "", Text("Stylesheet…"));
        Eq("menu gestures: Longer note value shows -", "-", Text("Longer note value"));
        Eq("menu gestures: Shorter note value shows +", "+", Text("Shorter note value"));
        Eq("menu gestures: Accent shows ; (not Oem1)", ";", Text("Accent"));
        Eq("menu gestures: Move to higher string shows Alt+Shift+Up", "Alt+Shift+Up", Text("Move to higher string"));

        keys.Preset = HotkeyPresets.TuxGuitar;
        Verify("TuxGuitar preset", keys);
        Eq("menu gestures: the TuxGuitar preset shows Alt+Left for Previous section", "Alt+Left", Text("Previous section"));

        keys = new HotkeySettings();
        keys["Edit.Undo"] = "Ctrl+Q";
        keys.Disable("File.Save");
        Verify("rebound and unbound", keys);
        Eq("menu gestures: a rebound command shows its new key", "Ctrl+Q", Text("_Undo"));
        Eq("menu gestures: an unbound command shows no key", "", Text("_Save"));

        HotkeyCatalog.ReverseDurationKeys = true;
        try
        {
            keys = new HotkeySettings();
            Verify("reversed + / - keys", keys);
            Eq("menu gestures: reversed, Longer note value shows +", "+", Text("Longer note value"));
        }
        finally { HotkeyCatalog.ReverseDurationKeys = false; }

        // Every id the window can run from a key must exist as a runnable command: the items' ids are all in the default map or unbound.
        var map = HotkeyCatalog.BuildMap(new HotkeySettings());
        var duplicates = ids.Where(id => Live(new HotkeySettings(), id) is { Length: > 0 } g
            && map.TryGetValue(HotkeyCatalog.TryParse(HotkeyCatalog.GestureFor(new HotkeySettings(), id), out var k, out var m) ? HotkeyCatalog.Format(k, m) : "", out var owner) && owner != id).ToList();
        Check("menu gestures: no shown key belongs to a different command", duplicates.Count == 0, string.Join(", ", duplicates));
    }

    private static void TestNewBindableCommands()
    {
        string[] ids = { "File.ExportMidi", "File.ExportAscii", "File.ProjectSettings", "Transport.Metronome", "Transport.CountIn" };
        foreach (var id in ids)
        {
            var action = HotkeyCatalog.ById(id);
            Check($"bindable command {id} is in the catalogue and unbound by default", action is not null && action.DefaultGesture == "");
        }
        // Every effective gesture maps to exactly one command (no silent overwrite in BuildMap).
        var perGesture = HotkeyCatalog.All.Where(a => !HotkeyCatalog.IsClipAction(a.Id))
            .Select(a => HotkeyCatalog.GestureFor(new HotkeySettings(), a.Id)).Where(g => g.Length > 0)
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check("the default key map has no gesture bound to two commands", perGesture.Count == 0, string.Join(", ", perGesture));

        // Every preset, with and without "Reverse + / - duration keys": no canonical gesture on two commands of one context.
        var reverseBefore = HotkeyCatalog.ReverseDurationKeys;
        try
        {
            foreach (var preset in HotkeyPresets.Names)
                foreach (var reverse in new[] { false, true })
                {
                    HotkeyCatalog.ReverseDurationKeys = reverse;
                    var settings = new HotkeySettings { Preset = preset };
                    var dupes = HotkeyCatalog.All
                        .Select(a => (Clip: HotkeyCatalog.IsClipAction(a.Id), Gesture: HotkeyCatalog.GestureFor(settings, a.Id)))
                        .Where(x => x.Gesture.Length > 0)
                        .Select(x => (x.Clip, Gesture: HotkeyCatalog.TryParse(x.Gesture, out var k, out var m) ? HotkeyCatalog.Format(k, m) : x.Gesture))
                        .GroupBy(x => (x.Clip, Gesture: x.Gesture.ToUpperInvariant()))
                        .Where(g => g.Count() > 1).Select(g => g.Key.Gesture).ToList();
                    Check($"preset {preset}{(reverse ? " (reversed +/-)" : "")}: no key on two commands", dupes.Count == 0, string.Join(", ", dupes));
                }
        }
        finally { HotkeyCatalog.ReverseDurationKeys = reverseBefore; }

        // A binding saved before the Ctrl, Alt, Shift order (Shift written before Alt) still loads and still fires.
        var legacy = new AppSettings();
        legacy.Hotkeys.Bindings["Section.Previous"] = "Shift+Alt+F2";
        var loaded = SettingsValidator.Normalize(legacy).Hotkeys;
        Eq("an old Shift-before-Alt binding loads in the canonical order", "Alt+Shift+F2", loaded.Bindings.TryGetValue("Section.Previous", out var kept) ? kept : "");
        var legacyMap = HotkeyCatalog.BuildMap(new HotkeySettings { Bindings = { ["Section.Previous"] = "Ctrl+Shift+Alt+F2" } });
        Check("an old-order binding is found under the gesture the window builds from the key event",
            legacyMap.TryGetValue(WpfHotkeyGestureAdapter.Format(Key.F2, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift), out var legacyId) && legacyId == "Section.Previous");

        // Text input always wins: none of the keys that edit the score is let through while a text box has focus.
        Check("Insert beat, pitch, string, duration and beat-delete commands never run inside a text box",
            new[] { "Edit.InsertBeat", "Edit.DeleteBeats", "Note.PitchUp", "Note.PitchDown", "Note.MoveStringUp", "Note.MoveStringDown", "Note.Longer", "Note.Shorter", "Bar.Insert", "Bar.Delete", "Track.Previous", "Track.Next" }
                .All(id => !HotkeyCatalog.AllowsInTextBox(id)));
    }
}
