using System.IO;
using System.Windows.Controls;
using TabForge.Plugins;
using TabForge.Views.Presets;

namespace TabForge;

/// <summary>The preset manage panel over a fake store, and the four preset stores over real files in a scratch profile.</summary>
public static partial class SelfTest
{
    private sealed class FakePresetStore : IPresetStore
    {
        public readonly List<string> Items = new();
        public bool Fail;
        public event Action? Changed;
        public IReadOnlyList<string> Names() => Items.ToList();
        private int At(string name) => Items.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        private void Done() { if (Fail) throw new IOException("disk says no"); Changed?.Invoke(); }
        public void Rename(string from, string to) { if (!Fail) { var other = At(to); if (other >= 0 && other != At(from)) Items.RemoveAt(other); Items[At(from)] = to; } Done(); }
        public void Duplicate(string from, string to) { if (!Fail) Items.Insert(At(from) + 1, to); Done(); }
        public void Delete(string name) { if (!Fail) Items.RemoveAt(At(name)); Done(); }
        public void Move(string name, int index) { if (!Fail) { var i = At(name); var n = Items[i]; Items.RemoveAt(i); Items.Insert(Math.Clamp(index, 0, Items.Count), n); } Done(); }
    }

    private static void TestPresetManagePanel()
    {
        var store = new FakePresetStore();
        var answer = true; var asked = 0; var errors = new List<string>();
        var panel = new PresetManagePanel(new Border(), store, "Presets");
        panel.Confirm = _ => { asked++; return answer; };
        panel.AskDelete = _ => { asked++; return (answer, false); };
        panel.ShowError = errors.Add;
        Check("an empty panel says there are no saved presets", panel.IsEmptyShown && panel.Rows.Count == 0);

        store.Items.AddRange(new[] { "Bright", "Clean", "Lead" });
        panel.BeginRename("Clean");
        Check("the rows are the store's presets in its order", panel.Rows.SequenceEqual(new[] { "Bright", "Clean", "Lead" }) && !panel.IsEmptyShown, string.Join(",", panel.Rows));
        Check("an empty name is refused with a message", !panel.Rename("Clean", "   ") && panel.Message.Length > 0 && store.Items[1] == "Clean", panel.Message);
        Check("a name with a file-name character is refused with a message", !panel.Rename("Clean", "a/b") && !panel.Rename("Clean", "..\\x") && panel.Message.Length > 0 && store.Items[1] == "Clean", panel.Message);
        Check("no question is asked for a refused name", asked == 0, $"{asked}");
        answer = false;
        Check("renaming onto an existing preset asks, and No keeps both", !panel.Rename("Clean", "lead") && asked == 1 && store.Items.Count == 3, string.Join(",", store.Items));
        answer = true;
        Check("Yes replaces the other preset and keeps the row's place", panel.Rename("Clean", "Lead") && store.Items.SequenceEqual(new[] { "Bright", "Lead" }) && panel.Selected == "Lead", string.Join(",", store.Items));
        asked = 0;
        Check("a free name renames without a question", panel.Rename("Lead", " Solo ") && asked == 0 && store.Items[1] == "Solo" && panel.Message.Length == 0, string.Join(",", store.Items));

        panel.Duplicate("Solo"); panel.Duplicate("Solo");
        Check("duplicates are named \"copy\", then \"copy 2\", after the original", store.Items.SequenceEqual(new[] { "Bright", "Solo", "Solo copy 2", "Solo copy" }), string.Join(",", store.Items));
        Check("a long name still gets a unique copy name within the limit", PresetRules.CopyName(new string('x', 64), new[] { new string('x', 59) + " copy" }) is { Length: <= 64 } copy && copy.EndsWith(" copy 2", StringComparison.Ordinal));

        panel.Move("Bright", 1);
        Check("moving a row down reorders the store and the rows", store.Items[1] == "Bright" && panel.Rows[1] == "Bright" && panel.Selected == "Bright", string.Join(",", panel.Rows));
        panel.Move("Solo", -1);
        Check("moving the first row up does nothing", store.Items[0] == "Solo");

        answer = false; panel.Delete("Bright");
        Check("Delete asks once and No keeps the preset", store.Items.Contains("Bright"));
        answer = true; asked = 0; panel.Delete("Bright");
        Check("Delete with Yes removes it", !store.Items.Contains("Bright") && asked == 1 && panel.Rows.Count == 3, string.Join(",", panel.Rows));

        store.Fail = true;
        panel.Duplicate("Solo");
        Check("a file failure is shown and the list stays as it is", errors.Count == 1 && panel.Rows.Count == 3 && store.Items.Count == 3, string.Join(" | ", errors));
        PresetDropDownRows();
    }

    /// <summary>The organisable rows inside a preset drop-down (PresetRows), and the built-in preset of the note map's preset bar.</summary>
    private static void PresetDropDownRows()
    {
        var store = new FakePresetStore();
        store.Items.AddRange(new[] { "Bright", "Clean" });
        var combo = new ComboBox();
        var rows = PresetRows.Attach(combo, PresetKind.MidiChain, () => store, o => o is ComboBoxItem { Tag: string name } ? name : null);
        var asked = 0; rows.Confirm = _ => { asked++; return true; };
        rows.AskDelete = _ => { asked++; return (true, false); };
        rows.Reserved = () => new[] { "Factory" };
        var views = new List<System.Windows.FrameworkElement>();
        void Fill()
        {
            combo.Items.Clear(); views.Clear();
            combo.Items.Add(new ComboBoxItem { Content = "Factory" });
            foreach (var name in store.Items) combo.Items.Add(rows.Item("★ " + name, name));
            rows.AddManage(combo.Items);
            foreach (var item in combo.Items.OfType<ComboBoxItem>()) views.Add(rows.View(item, (string)item.Content));
            combo.SelectedIndex = 0;
        }
        store.Changed += Fill;
        Fill();
        var picked = 0; combo.SelectionChanged += (_, e) => { if (e.AddedItems.Count > 0 && e.AddedItems[0] is ComboBoxItem { Tag: string }) picked++; };
        static List<Button> Buttons(System.Windows.FrameworkElement view) => view is Border { Child: Grid grid } ? grid.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>()).ToList() : new();
        static void Click(Button button) => button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check("the list ends with a line and Manage presets…, which is no preset", combo.Items[^2] is Separator && combo.Items[^1] is ComboBoxItem { Tag: null, Content: "Manage presets…" } && rows.View(combo.Items[^1], "Manage presets…") is TextBlock);
        Check("every preset kind has a store without a window", Enum.GetValues<PresetKind>().All(k => PresetManager.StoreOf(k, "x") is not null) && PresetManager.StoreOf(PresetKind.PluginPreset, null) is null);
        Check("a built-in row of a drop-down is plain text, a user row has rename and delete only (no duplicate)", views[0] is TextBlock && Buttons(views[0]).Count == 0 && Buttons(views[1]).Count == 2 && Buttons(views[2]).Count == 2);
        Check("each row button has a tooltip and a spoken name", Buttons(views[1]).All(b => b.ToolTip is not null && System.Windows.Automation.AutomationProperties.GetName(b).EndsWith("Bright", StringComparison.Ordinal)));
        Check("a user row keeps its text as the item's content (closed box, typing to jump)", (string)((ComboBoxItem)combo.Items[1]).Content == "★ Bright");
        Click(Buttons(views[1])[0]);
        Check("the rename button edits the name in the row and picks nothing", rows.IsEditing && picked == 0 && combo.SelectedIndex == 0);
        Check("a refused name stays in the editor with its reason", !rows.Rename("Bright", "a/b") && rows.Message.Length > 0 && rows.IsEditing && !rows.Rename("Bright", "factory") && rows.Message == PresetRows.ReservedText && store.Items[0] == "Bright", rows.Message);
        Check("a free name renames in place and the list is filled again", rows.Rename("Bright", "Dark") && store.Items.SequenceEqual(new[] { "Dark", "Clean" }) && !rows.IsEditing && (string)((ComboBoxItem)combo.Items[1]).Content == "★ Dark");
        rows.Duplicate("Clean");
        rows.Move("Dark", 1);
        Check("duplicate (the manager's action) and move work without picking", store.Items.SequenceEqual(new[] { "Clean", "Dark", "Clean copy" }) && picked == 0, string.Join(",", store.Items));
        Click(Buttons(views[1])[1]);
        Check("delete asks once", asked == 1 && store.Items.SequenceEqual(new[] { "Dark", "Clean copy" }) && picked == 0, string.Join(",", store.Items));

        string? applied = null;
        var bar = new MidiProcessorPresetBar(null!, () => null, (_, json) => applied = json);
        var map = MidiProcessorCatalog.Create(MidiProcessorCatalog.Find("noteMap")!);
        bar.Show(map);
        Check("the note map's presets start with the built-in \"Tabs to SD3\"", bar.Listed.Count > 0 && bar.Listed[0] == ("Tabs to SD3", true) && bar.Shown != "Tabs to SD3", bar.Shown);
        bar.PickRow("Tabs to SD3");
        Check("loading it sets From and To", applied is not null && applied.Contains("\"From\":\"General MIDI\"", StringComparison.Ordinal) && applied.Contains("\"To\":\"Tabs to SD3\"", StringComparison.Ordinal), applied ?? "");
        map.Params = MidiProcessorCatalog.BuiltInPresets.First(p => p.Name.EndsWith("humanize", StringComparison.Ordinal)).Build()[0].Params;
        bar.Show(map);
        Check("a note map with a preset's values shows that preset's name", bar.Shown == "Tabs to SD3", bar.Shown);
    }

    private static void TestFxListWidthLimits()
    {
        Check("the list keeps a chosen width that fits", Views.Fx.FxListSplitter.Limit(350, 980) == 350);
        Check("the list is never narrower than 180", Views.Fx.FxListSplitter.Limit(20, 980) == 180);
        Check("the list takes at most 45% of the window", Views.Fx.FxListSplitter.Limit(900, 980) == 441, $"{Views.Fx.FxListSplitter.Limit(900, 980)}");
        Check("a narrow window keeps the plug-in pane usable", Views.Fx.FxListSplitter.Limit(900, 640) == 210, $"{Views.Fx.FxListSplitter.Limit(900, 640)}");
        Check("a broken width falls back to the default", Views.Fx.FxListSplitter.Limit(double.NaN, 980) == 290);
        var settings = new Services.AppSettings();
        settings.Plugins.FxListWidth = double.NaN;
        Services.SettingsValidator.Normalize(settings);
        Check("a broken saved width is repaired on load", settings.Plugins.FxListWidth == 290, $"{settings.Plugins.FxListWidth}");
    }

    private static void TestPresetStoresOnDisk()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"tf-presets-{Guid.NewGuid():N}");
        var previous = PluginLibrary.RootOverride;
        PluginLibrary.RootOverride = scratch;
        try
        {
            var slot = new PluginSlot { Name = "SelfTest Synth", Path = @"C:\selftest\synth.dll" };
            var chain = new List<PluginSlot> { slot };
            var velocity = MidiProcessorCatalog.Create(MidiProcessorCatalog.Find("velocity")!);
            var processors = PresetFolderStore.MidiProcessor("velocity");
            Directory.CreateDirectory(PluginLibrary.ChainsFolder);
            foreach (var name in new[] { "Clean", "Bright", "Lead" })
            {
                PluginLibrary.SavePreset(slot, name, "AAAA");
                PluginLibrary.SaveChain(Path.Combine(PluginLibrary.ChainsFolder, name + ".tfchain"), chain);
                MidiProcessorPresetBar.Write(processors, name, velocity);
                Services.FilePathPolicy.WriteAtomically(PresetFolderStore.MidiChains().PathOf(name), s => s.Write("[]"u8), createDirectory: true);
            }

            var stores = new (string Kind, Func<PresetFolderStore> Open)[]
            {
                ("plug-in presets", () => PresetFolderStore.Plugin(slot)), ("FX chains", PresetFolderStore.FxChains),
                ("MIDI chain presets", PresetFolderStore.MidiChains), ("MIDI processor presets", () => PresetFolderStore.MidiProcessor("velocity")),
            };
            foreach (var (kind, open) in stores)
            {
                var store = open();
                var folder = Path.GetDirectoryName(store.PathOf("x"))!;
                var changes = 0; store.Changed += () => changes++;
                Check($"{kind}: a folder with no order lists alphabetically", store.Names().SequenceEqual(new[] { "Bright", "Clean", "Lead" }), string.Join(",", store.Names()));
                store.Rename("Clean", "Crunch");
                Check($"{kind}: rename moves the file and writes no order file", store.Names().SequenceEqual(new[] { "Bright", "Crunch", "Lead" }) && !File.Exists(store.PathOf("Clean")) && Directory.GetFiles(folder, "*.txt").Length == 0, string.Join(",", store.Names()));
                store.Move("Lead", 0);
                Check($"{kind}: the new order is read back by a fresh store", open().Names().SequenceEqual(new[] { "Lead", "Bright", "Crunch" }), string.Join(",", open().Names()));
                store.Rename("Bright", "Glass");
                store.Duplicate("Lead", "Lead copy");
                Check($"{kind}: a rename keeps its place and a duplicate follows its original", open().Names().SequenceEqual(new[] { "Lead", "Lead copy", "Glass", "Crunch" }), string.Join(",", open().Names()));
                Check($"{kind}: the duplicate is a full copy", File.ReadAllBytes(store.PathOf("Lead")).SequenceEqual(File.ReadAllBytes(store.PathOf("Lead copy"))));
                store.Delete("Glass");
                Check($"{kind}: delete removes the file and the row", open().Names().SequenceEqual(new[] { "Lead", "Lead copy", "Crunch" }) && !File.Exists(store.PathOf("Glass")) && changes == 5, $"{string.Join(",", open().Names())} / {changes}");
                var refused = 0;
                foreach (var bad in new[] { @"..\escape", "a/b", "", "c:x" }) try { store.PathOf(bad); } catch (InvalidDataException) { refused++; }
                Check($"{kind}: a name that is no plain file name is refused", refused == 4, $"{refused}");
                Services.FilePathPolicy.FaultInjection = stage => { if (stage.StartsWith("commit:", StringComparison.Ordinal)) throw new IOException("injected"); };
                var failed = false;
                try { store.Duplicate("Lead", "Broken"); } catch (IOException) { failed = true; } finally { Services.FilePathPolicy.FaultInjection = null; }
                Check($"{kind}: a failed duplicate leaves the presets as they were", failed && open().Names().SequenceEqual(new[] { "Lead", "Lead copy", "Crunch" }) && File.Exists(store.PathOf("Lead")), string.Join(",", open().Names()));
            }
            Check("the plug-in preset drop-down lists in the saved order", PluginLibrary.PresetNames(slot).SequenceEqual(new[] { "Lead", "Lead copy", "Crunch" }), string.Join(",", PluginLibrary.PresetNames(slot)));
            Check("a renamed plug-in preset still loads", PluginLibrary.LoadPreset(slot, "Crunch") is { State: "AAAA" });
            Check("a renamed chain still loads", PluginLibrary.LoadChain(PresetFolderStore.FxChains().PathOf("Crunch")) is { Count: 1 });

            var info = MidiProcessorCatalog.Find("velocity")!;
            var number = info.Params.First(p => p.Kind == MidiParamKind.Number);
            Services.FilePathPolicy.WriteAtomically(processors.PathOf("Wild"), s => s.Write(System.Text.Encoding.UTF8.GetBytes($"{{\"{number.Key}\":1e9,\"Bogus\":1}}")));
            var read = MidiProcessorPresetBar.Read(processors, "velocity", "Wild");
            var values = read is null ? null : System.Text.Json.Nodes.JsonNode.Parse(read) as System.Text.Json.Nodes.JsonObject;
            Check("a processor preset keeps only that processor's parameters, inside their range", values is not null && !values.ContainsKey("Bogus") && values.Count == info.Params.Count && values[number.Key]!.GetValue<double>() == number.Max, read);
            Services.FilePathPolicy.WriteAtomically(processors.PathOf("Junk"), s => s.Write("not json"u8));
            Check("an unreadable processor preset gives nothing", MidiProcessorPresetBar.Read(processors, "velocity", "Junk") is null);
        }
        finally
        {
            Services.FilePathPolicy.FaultInjection = null;
            PluginLibrary.RootOverride = previous;
            try { Directory.Delete(scratch, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
