using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the score note right-click menu's actions, run through the menu's own item Click handlers on a real MainWindow: each changes the song as
//   it says, one undo step takes it back, and every item is routed (its gesture is a catalogued command, the Backspace key reaches the editor).
// Does not own: the menu's order and its texts (TestContextMenuLayouts), the keyboard placement of the menu (TestKeyboardContextMenuPlacement).
// Tests: TestNoteMenuClipboardActions, TestNoteMenuEditActions, TestNoteMenuItemsRouted.
public static partial class SelfTest
{
    /// <summary>A two-bar song with one note in bar 1, beat 1, on string index 2 (fret 5).</summary>
    private static SongProject NmSong()
    {
        var song = SmBlankSong(2);
        var track = song.Tracks[0];
        track.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 2, Fret = 5, MidiValue = track.PitchOf(2, 5) });
        return song;
    }

    /// <summary>Right-click on a beat of bar 1: the note menu is built the way the window builds it, and the capture hook takes it instead of a popup.</summary>
    private static ContextMenu NmOpen(MainWindow w, int cell, int stringIndex = 2)
    {
        ContextMenu? caught = null;
        var previous = MainWindow.ContextMenuCapture;
        MainWindow.ContextMenuCapture = menu => caught = menu;
        try
        {
            SmCall(w, "ShowNoteContextMenu", new Views.ContextMenuEventArgs(new Point(200, 200))
            {
                Measure = 0, Cell = cell, StringIndex = stringIndex, OverBeat = true, OnNote = cell == 0
            });
        }
        finally { MainWindow.ContextMenuCapture = previous; }
        return caught ?? throw new InvalidOperationException("the note menu did not open");
    }

    /// <summary>The item at the path of headers, from the menu's top level down.</summary>
    private static MenuItem NmItem(ItemsControl menu, params string[] path)
    {
        ItemsControl at = menu;
        MenuItem? item = null;
        foreach (var header in path)
        {
            item = at.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header as string == header)
                ?? throw new InvalidOperationException($"no menu item '{header}' in '{string.Join(" > ", path)}'");
            at = item;
        }
        return item!;
    }

    private static bool NmHas(ItemsControl menu, string header) => menu.Items.OfType<MenuItem>().Any(m => m.Header as string == header);

    /// <summary>A click as the user makes it: the item must be enabled, then its Click handler runs.</summary>
    private static void NmClick(MenuItem item)
    {
        if (!item.IsEnabled) throw new InvalidOperationException($"'{item.Header}' is disabled");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    /// <summary>Opens a fresh note menu on a beat, clicks the item at <paramref name="path"/>, and returns the song's fingerprint before and after.</summary>
    private static (string Before, string After) NmRun(MainWindow w, DocumentSession doc, int cell, params string[] path)
    {
        var before = DocumentEdits.Fingerprint(doc);
        NmClick(NmItem(NmOpen(w, cell), path));
        SmSettle();
        return (before, DocumentEdits.Fingerprint(doc));
    }

    private static void TestNoteMenuClipboardActions()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, NmSong());
            SmField<TabEditorControl>(w, "Editor")!.SelectedTrackIndex = 0;

            SmStep("note menu: Paste is listed exactly when the clipboard holds a clip", () =>
            {
                Check("note menu: Paste and Paste special follow the clipboard",
                    NmHas(NmOpen(w, 0), "Paste") == ClipboardService.Shared.CanPaste && NmHas(NmOpen(w, 0), "Paste special…") == ClipboardService.Shared.CanPaste);
            });

            SmStep("note menu copy: Copy changes nothing and puts the beat on the clipboard", () =>
            {
                var undo = doc.Undo.UndoCount;
                var (before, after) = NmRun(w, doc, 0, "Copy");
                Check("note menu Copy: the song is unchanged and no undo step is added", before == after && doc.Undo.UndoCount == undo);
                Check("note menu Copy: the clipboard now holds the beat, so the next menu offers Paste",
                    ClipboardService.Shared.CanPaste && NmHas(NmOpen(w, 4), "Paste"));
            });

            SmStep("note menu paste: Paste into an empty beat adds the copied note, one undo step", () =>
            {
                var (before, after) = SmWithDialogs<(string, string)>(_ => null, () => NmRun(w, doc, 4, "Paste"));
                Check("note menu Paste: the copied note lands on the clicked beat", SmCell(doc, 0, 4).Notes.Count == 1 && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu Paste: one undo removes the pasted note", DocumentEdits.Fingerprint(doc) == before && SmCell(doc, 0, 4).Notes.Count == 0);
            });

            SmStep("note menu cut: Cut takes the note off the beat, one undo step puts it back", () =>
            {
                var (before, after) = NmRun(w, doc, 0, "Cut");
                Check("note menu Cut: the beat loses its note", SmCell(doc, 0, 0).Notes.Count == 0 && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu Cut: one undo restores the note", DocumentEdits.Fingerprint(doc) == before && SmCell(doc, 0, 0).Notes.Count == 1);
            });

            SmStep("note menu delete: Delete leaves the beat a rest, one undo step puts the note back", () =>
            {
                var (before, after) = NmRun(w, doc, 0, "Delete");
                Check("note menu Delete: the beat has no note left", SmCell(doc, 0, 0).Notes.Count == 0 && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu Delete: one undo restores the note", DocumentEdits.Fingerprint(doc) == before && SmCell(doc, 0, 0).Notes.Count == 1);
            });
        }
        finally { SmCloseWindow(w); }
    }

    private static void TestNoteMenuEditActions()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, NmSong());
            SmField<TabEditorControl>(w, "Editor")!.SelectedTrackIndex = 0;

            // Each action is checked on the song, then one Undo must restore the fingerprint taken before it.
            SmStep("note menu duration: 16th note sets the beat's duration, one undo restores the eighth", () =>
            {
                var (before, after) = NmRun(w, doc, 0, "Duration", "16th note");
                Check("note menu duration: the beat is now a 16th", SmCell(doc, 0, 0).DurationDenominator == 16 && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu duration: undo brings back the eighth", SmCell(doc, 0, 0).DurationDenominator == 8 && DocumentEdits.Fingerprint(doc) == before);
            });

            SmStep("note menu dynamics: mp sets the note's velocity, one undo restores it", () =>
            {
                var (before, after) = NmRun(w, doc, 0, "Dynamics", "mp");
                Check("note menu dynamics: the note plays at mp (velocity 64)", SmCell(doc, 0, 0).Notes[0].Velocity == 64 && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu dynamics: undo restores the velocity", DocumentEdits.Fingerprint(doc) == before && SmCell(doc, 0, 0).Notes[0].Velocity != 64);
            });

            SmStep("note menu palm mute: the first click adds it, the second removes it, undo brings it back", () =>
            {
                NmRun(w, doc, 0, "Effects", "Palm mute");
                Check("note menu palm mute: the note carries the technique", SmCell(doc, 0, 0).Notes[0].Techniques.Contains(TechniqueNames.PalmMute));
                NmRun(w, doc, 0, "Effects", "Palm mute");
                Check("note menu palm mute: a second click removes it", !SmCell(doc, 0, 0).Notes[0].Techniques.Contains(TechniqueNames.PalmMute));
                SmClick(w, "Undo_Click");
                Check("note menu palm mute: one undo puts it back", SmCell(doc, 0, 0).Notes[0].Techniques.Contains(TechniqueNames.PalmMute));
            });

            SmStep("note menu hammer-on: adds the technique, one undo removes it", () =>
            {
                var (before, after) = NmRun(w, doc, 0, "Effects", "Hammer-on / pull-off");
                Check("note menu hammer-on: the note carries the technique", SmCell(doc, 0, 0).Notes[0].Techniques.Contains(TechniqueNames.Hopo) && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu hammer-on: undo removes it", DocumentEdits.Fingerprint(doc) == before);
            });

            SmStep("note menu dead note: marks the note dead, one undo clears it", () =>
            {
                var (before, after) = NmRun(w, doc, 0, "Effects", "Dead note");
                Check("note menu dead note: the note is dead", SmCell(doc, 0, 0).Notes[0].Dead && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu dead note: undo clears it", DocumentEdits.Fingerprint(doc) == before && !SmCell(doc, 0, 0).Notes[0].Dead);
            });

            SmStep("note menu tie: ties the note on its string, one undo clears it", () =>
            {
                var (before, after) = NmRun(w, doc, 0, "Duration", "Tie");
                Check("note menu tie: the note is tied", SmCell(doc, 0, 0).Notes[0].Tied && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu tie: undo clears it", DocumentEdits.Fingerprint(doc) == before && !SmCell(doc, 0, 0).Notes[0].Tied);
            });

            SmStep("note menu pitch: Pitch up a semitone raises the note by one, one undo lowers it back", () =>
            {
                var (before, after) = NmRun(w, doc, 0, "Pitch and string", "Pitch up a semitone");
                var note = SmCell(doc, 0, 0).Notes[0];
                Check("note menu pitch up: the note is one semitone higher, one fret up", note.Fret == 6 && before != after, $"fret {note.Fret}");
                SmClick(w, "Undo_Click");
                Check("note menu pitch up: undo restores fret 5", DocumentEdits.Fingerprint(doc) == before && SmCell(doc, 0, 0).Notes[0].Fret == 5);
            });

            SmStep("note menu string: Move note to higher string moves it one string up at the same pitch, one undo moves it back", () =>
            {
                var midi = SmCell(doc, 0, 0).Notes[0].MidiValue;
                var (before, after) = NmRun(w, doc, 0, "Pitch and string", "Move note to higher string");
                var note = SmCell(doc, 0, 0).Notes[0];
                Check("note menu string: the note is on the next string up with the same pitch", note.StringIndex == 1 && note.MidiValue == midi && before != after);
                SmClick(w, "Undo_Click");
                Check("note menu string: undo puts it back on string index 2", DocumentEdits.Fingerprint(doc) == before && SmCell(doc, 0, 0).Notes[0].StringIndex == 2);
            });
        }
        finally { SmCloseWindow(w); }
    }

    private static void TestNoteMenuItemsRouted()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, NmSong());
            var ed = SmField<TabEditorControl>(w, "Editor")!;
            ed.SelectedTrackIndex = 0;
            var keys = ((AppSettings)typeof(MainWindow).GetProperty("_settings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!).Hotkeys;
            // Palette tools whose item opens an editor or a chooser: the sweep leaves them to their own tests.
            var editorTools = new HashSet<string>(StringComparer.Ordinal)
            {
                "effect:bend", "effect:tremolo_bar", "effect:natural_harmonic", "effect:grace_note", "effect:trill",
                "effect:chord", "effect:chord_menu", "effect:text", "duration:tuplet-menu"
            };

            SmStep("note menu routing: the top level is the ContextMenuLayouts order", () =>
            {
                var headers = NmOpen(w, 0).Items.OfType<MenuItem>().Select(m => (string)m.Header).ToList();
                var expected = ContextMenuLayouts.NoteMenu(ClipboardService.Shared.CanPaste).Where(i => i != ContextMenuLayouts.Sep).ToList();
                Check("note menu routing: top-level headers match the layout", headers.SequenceEqual(expected), string.Join(" | ", headers));
            });

            SmStep("note menu routing: each bindable item shows its command's live binding and the command exists", () =>
            {
                var menu = NmOpen(w, 0);
                var bad = new List<string>();
                void Bound(string id, params string[] path)
                {
                    var item = NmItem(menu, path);
                    if (HotkeyCatalog.ById(id) is null) bad.Add($"{id}: not in the command catalogue");
                    else if (item.InputGestureText != HotkeyCatalog.DisplayAll(keys, id)) bad.Add($"{string.Join(">", path)}: shows '{item.InputGestureText}'");
                }
                Bound("Edit.Copy", "Copy");
                Bound("Edit.Cut", "Cut");
                Bound("Edit.Paste", "Paste");
                Bound("Edit.PasteSpecial", "Paste special…");
                Bound("Note.PitchUp", "Pitch and string", "Pitch up a semitone");
                Bound("Note.PitchDown", "Pitch and string", "Pitch down a semitone");
                Bound("Note.MoveStringUp", "Pitch and string", "Move note to higher string");
                Bound("Note.MoveStringDown", "Pitch and string", "Move note to lower string");
                Check("note menu routing: every bindable note item's key text matches its command", bad.Count == 0, string.Join("; ", bad));
                Check("note menu routing: Delete shows the fixed Backspace text (not a catalogued command)",
                    NmItem(menu, "Delete").InputGestureText == ContextMenuLayouts.FixedKeys.DeleteNote);
            });

            SmStep("note menu routing: the Backspace key the Delete item shows deletes the note through the editor", () =>
            {
                NmOpen(w, 0);
                var handled = ed.TryHandleKey(Key.Back, ModifierKeys.None);
                Check("note menu routing: Backspace is handled by the editor and empties the beat", handled && SmCell(doc, 0, 0).Notes.Count == 0);
                SmClick(w, "Undo_Click");
                Check("note menu routing: one undo restores the note", SmCell(doc, 0, 0).Notes.Count == 1);
            });

            SmStep("note menu routing: every supported palette tool in the four sub-menus runs from its item without an exception", () =>
            {
                var groups = new (string Category, string Header)[] { ("Duration", "Duration"), ("Dynamic", "Dynamics"), ("Effects", "Effects"), ("Beat", "Beat") };
                var failures = new List<string>();
                var clicked = 0; var skipped = 0;
                SmWithDialogs<bool>(_ => null, () =>
                {
                    foreach (var (category, header) in groups)
                    {
                        var supported = Views.ToolPaletteController.PaletteTools.Where(t => t.Group == category && t.Supported).ToList();
                        var submenu = NmItem(NmOpen(w, 0), header);
                        Check($"note menu routing: the {header} sub-menu lists every supported {category} tool", submenu.Items.Count == supported.Count,
                            $"{submenu.Items.Count} items, {supported.Count} tools");
                        foreach (var tool in supported)
                        {
                            if (editorTools.Contains(tool.Id)) { skipped++; continue; }
                            try
                            {
                                var item = NmItem(NmOpen(w, 0), header, tool.Label);
                                if (!item.IsEnabled) { skipped++; continue; }
                                NmClick(item);
                                clicked++;
                            }
                            catch (Exception ex) { failures.Add($"{tool.Id}: {ex.GetType().Name} {ex.Message}"); }
                        }
                    }
                    return true;
                });
                Check($"note menu routing: {clicked} palette items ran, {skipped} skipped (editor or disabled)", clicked >= 20);
                Check("note menu routing: no palette item from the menu throws", failures.Count == 0, string.Join("; ", failures));
            });
        }
        finally { SmCloseWindow(w); }
    }
}
