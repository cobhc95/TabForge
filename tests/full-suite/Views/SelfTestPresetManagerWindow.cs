using System.IO;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.PresetManagement;
using TabForge.Views.Presets;

namespace TabForge;

/// <summary>The preset manager window over a scratch profile that holds presets of every kind.</summary>
public static partial class SelfTest
{
    private static void TestPresetManagerWindow()
    {
        // The delete question's setting lives in a scratch settings store, never in the owner's settings.
        using var settingsStore = AppSettingsStore.Open(Path.Combine(Path.GetTempPath(), $"tf-presetmanager-settings-{Guid.NewGuid():N}.json"), TimeSpan.FromMinutes(30));
        using var settingsScope = AppSettingsStore.OverrideSharedForTest(settingsStore);
        var scratch = Path.Combine(Path.GetTempPath(), $"tf-presetmanager-{Guid.NewGuid():N}");
        var previous = PluginLibrary.RootOverride;
        PluginLibrary.RootOverride = scratch;
        DrumMapLibrary.Reload();
        try
        {
            void Seed(PresetFolderStore store, params string[] names)
            {
                foreach (var name in names) Services.FilePathPolicy.WriteAtomically(store.PathOf(name), s => s.Write("{\"Name\":\"x\",\"Notes\":{\"36\":\"Kick\"}}"u8), createDirectory: true);
            }
            Seed(PresetFolderStore.Plugin("Synth A"), "Clean", "Bright", "Lead");
            Seed(PresetFolderStore.Plugin("Amp B"), "Crunch");
            Seed(PresetFolderStore.FxChains(), "Lead chain", "Rhythm chain");
            Seed(PresetFolderStore.Containers(), "Drive stack");
            Seed(PresetFolderStore.MidiProcessor("velocity"), "Soft", "Hard");
            Seed(new PresetFolderStore(DrumMapLibrary.UserFolder, ".json"), "My kit");
            DrumMapLibrary.Reload();
            var shipped = DrumMapLibrary.All().Count(m => !m.IsUser);
            var builtInChains = MidiProcessorCatalog.BuiltInPresets.Count;

            var window = new PresetManagerWindow(null, PresetKind.MidiProcessor, "velocity");
            var asked = 0; var answer = true; var tick = false; var errors = new List<string>(); var lastAsk = "";
            window.Confirm = _ => true;   // the overwrite question is not part of these checks
            window.AskDelete = text => { asked++; lastAsk = text; return (answer, tick); };
            window.ShowError = errors.Add;
            int Count(PresetKind kind, string? key = null) => window.TreeRows.First(r => r.Node.Kind == kind && r.Node.Key == key).Count;

            Check("the window opens on the requested node, its group expanded", window.SelectedNode is { Kind: PresetKind.MidiProcessor, Key: "velocity", Title: "Velocity" }
                && window.TreeRows.Any(r => r.Node.Key == "velocity") && window.Listed.Select(e => e.Name).SequenceEqual(new[] { "Hard", "Soft" }), string.Join(",", window.Listed.Select(e => e.Name)));
            Check("the tree counts every kind; an empty kind stays with 0", Count(PresetKind.PluginPreset) == 4 && Count(PresetKind.FxChain) == 2 && Count(PresetKind.Container) == 1
                && Count(PresetKind.MidiChain) == builtInChains && Count(PresetKind.MidiProcessor) == 3 && Count(PresetKind.MidiProcessor, "velocity") == 2 && Count(PresetKind.DrumMap) == shipped + 1,
                string.Join(" ", window.TreeRows.Select(r => $"{r.Node.Title}={r.Count}")));
            Check("a processor type with only a built-in preset is listed under MIDI processors", window.TreeRows.Any(r => r.Node.Key == "noteMap" && r.Count == 1));

            // The actions, on a plug-in's presets.
            window.Show(PresetKind.PluginPreset, "Synth A");
            Check("jumping to a plug-in lists its presets", window.Listed.Select(e => e.Name).SequenceEqual(new[] { "Bright", "Clean", "Lead" }) && Count(PresetKind.PluginPreset, "Synth A") == 3);
            window.Select("Clean");
            Check("a name with a file-name character is refused with the drop-down's message", !window.RenameSelected("a/b") && window.Message == PresetRules.Error("a/b"), window.Message);
            Check("rename changes the store and keeps the selection", window.RenameSelected(" Glass ") && PresetFolderStore.Plugin("Synth A").Names().Contains("Glass") && window.SelectedEntry?.Name == "Glass" && window.Message.Length == 0);
            window.DuplicateSelected();
            Check("duplicate adds a copy after the original, selected, and the count follows", PresetFolderStore.Plugin("Synth A").Names().SequenceEqual(new[] { "Bright", "Glass", "Glass copy", "Lead" })
                && window.SelectedEntry?.Name == "Glass copy" && Count(PresetKind.PluginPreset, "Synth A") == 4 && Count(PresetKind.PluginPreset) == 5, string.Join(",", PresetFolderStore.Plugin("Synth A").Names()));
            window.MoveSelected(-1);
            Check("move up reorders the store", PresetFolderStore.Plugin("Synth A").Names().SequenceEqual(new[] { "Bright", "Glass copy", "Glass", "Lead" }) && window.SelectedEntry?.Name == "Glass copy");
            answer = false; window.DeleteSelected();
            Check("delete asks once and No keeps the preset", asked == 1 && PresetFolderStore.Plugin("Synth A").Names().Count == 4);
            answer = true; window.DeleteSelected();
            Check("delete with Yes removes the file and lowers the count", !File.Exists(PresetFolderStore.Plugin("Synth A").PathOf("Glass copy")) && Count(PresetKind.PluginPreset, "Synth A") == 3 && window.SelectedEntry is not null);

            // Built-in entries.
            window.Show(PresetKind.MidiChain, null);
            Check("built-in MIDI chains are listed, marked, and protected", window.Listed.Count == builtInChains && window.Listed.All(e => e.BuiltIn) && window.SelectedEntry is { BuiltIn: true }
                && !window.CanRename && !window.CanDelete && !window.CanMove && !window.CanDuplicate && window.RenameTip == PresetManagerWindow.BuiltInTip, window.RenameTip);
            window.DeleteSelected(); window.MoveSelected(1);
            Check("the commands leave a built-in alone", !window.RenameSelected("Mine") && asked == 2 && window.Listed.Count == builtInChains);
            window.Show(PresetKind.MidiProcessor, "noteMap");
            Check("the note map's built-in preset is listed and protected", window.Listed.Count == 1 && window.Listed[0] is { Name: "Tabs to SD3", BuiltIn: true } && !window.CanRename);
            window.Show(PresetKind.DrumMap, null);
            Check("shipped drum maps come first and the user's map can be changed", window.Listed.Count == shipped + 1 && window.Listed.Take(shipped).All(e => e.BuiltIn) && window.Select("My kit") && window.CanRename && window.CanDelete);
            Check("a user's map cannot take a shipped map's name", shipped == 0 || (!window.RenameSelected(window.Listed[0].Name) && window.Message == PresetRows.ReservedText), window.Message);
            if (shipped > 0)
            {
                window.Select(window.Listed[0].Name);
                var name = window.SelectedEntry!.Name;
                Check("a shipped drum map can be duplicated but not renamed or deleted", window.CanDuplicate && !window.CanRename && !window.CanDelete);
                window.DuplicateSelected();
                Check("the copy of a shipped map is a user's map with its own name inside", new DrumMapStore().Names().Contains(name + " copy") && DrumMapLibrary.Find(name + " copy") is { IsUser: true } && errors.Count == 0, string.Join(";", errors));
            }

            // Several presets at once: one question, the block moves, the built-ins skipped, and the "Don't ask again" tick.
            Seed(PresetFolderStore.Containers(), "Clip A", "Clip B", "Clip C", "Clip D", "Clip E", "Clip F");
            window.Show(PresetKind.Container, null);
            window.SelectNames("Clip B", "Clip D", "Clip E");
            Check("three rows selected: Delete, Duplicate and the moves are on; Rename and Show in tree need one row", window.SelectedEntries.Select(e => e.Name).SequenceEqual(new[] { "Clip B", "Clip D", "Clip E" })
                && !window.CanRename && window.CanDelete && window.CanDuplicate && window.CanMove && !window.CanShowInTree, string.Join(",", window.SelectedEntries.Select(e => e.Name)));
            asked = 0; answer = true; lastAsk = "";
            window.DeleteSelected();
            Check("deleting three presets asks once, naming them, and deletes them all; the row after the first one is selected",
                asked == 1 && lastAsk == "Delete 3 presets?\n\nClip B\nClip D\nClip E" && PresetFolderStore.Containers().Names().SequenceEqual(new[] { "Clip A", "Clip C", "Clip F", "Drive stack" })
                && Count(PresetKind.Container) == 4 && window.SelectedEntries.Select(e => e.Name).SequenceEqual(new[] { "Clip C" }), lastAsk);
            window.SelectNames("Clip A", "Clip C"); answer = false; window.DeleteSelected();
            Check("No to the question keeps every selected preset", asked == 2 && PresetFolderStore.Containers().Names().Count == 4);
            window.SelectNames("Clip C", "Clip F");
            window.MoveSelected(-1);
            Check("move up moves the block as one, keeping its order, and it stays selected", PresetFolderStore.Containers().Names().SequenceEqual(new[] { "Clip C", "Clip F", "Clip A", "Drive stack" })
                && window.SelectedEntries.Select(e => e.Name).SequenceEqual(new[] { "Clip C", "Clip F" }), string.Join(",", PresetFolderStore.Containers().Names()));
            window.MoveSelected(-1);
            Check("a block at the edge does not move, and its Move up is off", PresetFolderStore.Containers().Names().SequenceEqual(new[] { "Clip C", "Clip F", "Clip A", "Drive stack" }) && !window.CanMoveUp);
            window.MoveSelected(1);
            Check("move down moves the block back", PresetFolderStore.Containers().Names().SequenceEqual(new[] { "Clip A", "Clip C", "Clip F", "Drive stack" }));
            Check("the question names up to five presets, then how many more", PresetDeleteQuestion.TextFor(new[] { "A", "B", "C", "D", "E", "F", "G" }) == "Delete 7 presets?\n\nA\nB\nC\nD\nE\nand 2 more"
                && PresetDeleteQuestion.TextFor(new[] { "X" }) == "Delete the preset \"X\"?");

            Seed(PresetFolderStore.MidiChains(), "Mine one");
            window.Show(PresetKind.MidiChain, null);
            var builtIn = window.Listed.Where(e => e.BuiltIn).Select(e => e.Name).Take(2).ToArray();
            window.SelectNames(builtIn);
            Check("only built-ins selected: every action stays off", !window.CanDelete && !window.CanRename && !window.CanMove && !window.CanDuplicate);
            window.SelectNames(builtIn.Append("Mine one").ToArray());
            asked = 0; answer = true; lastAsk = "";
            window.DeleteSelected();
            Check("built-ins in a selection are skipped: only the user's preset is asked about and deleted", asked == 1 && lastAsk == "Delete the preset \"Mine one\"?"
                && PresetFolderStore.MidiChains().Names().Count == 0 && window.Listed.Count == builtInChains && window.Listed.All(e => e.BuiltIn), lastAsk);

            // "Don't ask again": ticked and Yes turns the question off; No stores nothing; the setting brings it back.
            window.Show(PresetKind.Container, null);
            window.Select("Clip F");
            asked = 0; answer = false; tick = true;
            window.DeleteSelected();
            Check("No with the tick stores nothing: the question was asked, nothing was deleted, the setting stays on", asked == 1 && AppSettingsStore.Shared.Settings.General.ConfirmDeletePresets
                && PresetFolderStore.Containers().Names().Contains("Clip F"));
            answer = true;
            window.DeleteSelected();
            Check("Yes with the tick deletes and turns the question off in the settings", asked == 2 && !AppSettingsStore.Shared.Settings.General.ConfirmDeletePresets && !PresetFolderStore.Containers().Names().Contains("Clip F"));
            tick = false; window.Select("Clip A"); window.DeleteSelected();
            Check("with the question off, a delete happens without asking", asked == 2 && !PresetFolderStore.Containers().Names().Contains("Clip A"));
            AppSettingsStore.Shared.Settings.General.ConfirmDeletePresets = true;   // the Preferences row, turned back on
            window.Select("Clip C"); answer = false; window.DeleteSelected();
            Check("with the setting back on, the question asks again", asked == 3 && PresetFolderStore.Containers().Names().Contains("Clip C"));

            // Search across every kind, and Show in tree.
            window.SearchText = "lead";
            Check("search finds hits of several kinds, each with its place", window.Listed.Select(e => e.Node.Where).OrderBy(w => w).SequenceEqual(new[] { "FX chains", "Plug-in presets › Synth A" }), string.Join("|", window.Listed.Select(e => e.Node.Where)));
            window.SearchText = "chain rhythm";
            Check("every word typed must be in the name", window.Listed.Count == 1 && window.Listed[0].Name == "Rhythm chain");
            window.SearchText = "lead";
            window.Select("Lead chain"); window.RenameSelected("Solo chain");
            Check("an action works on a selected hit", PresetFolderStore.FxChains().Names().Contains("Solo chain") && window.Listed.All(e => e.Name != "Lead chain"));
            window.SearchText = "soft";   // also finds a built-in MIDI chain
            window.Select("Soft");
            window.ShowInTree();
            Check("Show in tree leaves the search and selects the preset in its node", window.SearchText.Length == 0 && window.SelectedNode is { Kind: PresetKind.MidiProcessor, Key: "velocity" } && window.SelectedEntry?.Name == "Soft"
                && window.Listed.Count == 2);
            Check("no file error was shown", errors.Count == 0, string.Join(";", errors));

            var today = new DateTime(2026, 10, 12);
            Check("dates read \"12 Oct\", with the year when it is not this year", PresetCatalogue.DateText(new DateTime(2026, 3, 5), today) == new DateTime(2026, 3, 5).ToString("d MMM")
                && PresetCatalogue.DateText(new DateTime(2024, 3, 5), today).EndsWith("2024", StringComparison.Ordinal) && PresetCatalogue.DateText(null, today).Length == 0);
            Check("the preset manager is a catalogue command without a default key, in the Tools menu", Services.HotkeyCatalog.All.Any(a => a.Id == PresetsFeatureModule.CommandId && a.DefaultGesture.Length == 0)
                && new PresetsFeatureModule().MenuRows.Single() is { Menu: "_Tools", Header: "Preset manager…" });
            window.Close();
        }
        finally
        {
            PluginLibrary.RootOverride = previous;
            DrumMapLibrary.Reload();
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
