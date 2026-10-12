using System.IO;
using System.Text.Json.Nodes;
using TabForge.Plugins;

namespace TabForge;

/// <summary>The user's drum maps as the map editor writes them: the note-to-note table, the editor's checks and the drum map store (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>Runs a check against an empty drum maps folder of its own, then puts the real one back.</summary>
    private static void WithScratchDrumMaps(Action<string> run)
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"tf-drummaps-{Guid.NewGuid():N}");
        var previous = PluginLibrary.RootOverride;
        PluginLibrary.RootOverride = scratch;
        DrumMapLibrary.Reload();
        try { run(scratch); }
        finally
        {
            PluginLibrary.RootOverride = previous;
            DrumMapLibrary.Reload();
            try { Directory.Delete(scratch, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void TestDrumMapRemap() => WithScratchDrumMaps(_ =>
    {
        var gm = DrumMapLibrary.Find("General MIDI");
        var noPieces = Array.Empty<(string, string)>();
        var (draft, error) = DrumMapDraft.Build("My kit", noPieces, new[] { (40, "38"), (46, " 26 "), (36, ""), (42, "42") }, gm);
        Check("a remap of 40 to 38 and 46 to 26 is a valid map that holds just those two", draft is { Remap.Count: 2 } && error is null, error ?? "wrong count");
        if (draft is null) return;
        draft.Save();
        var loaded = DrumMapLibrary.Find("My kit");
        Check("the saved map loads as a user map with its note table", loaded is { IsUser: true, Remap.Count: 2 } && loaded.Remap![40] == 38 && loaded.Remap[46] == 26, loaded?.Name ?? "missing");
        var table = DrumMapLibrary.BuildTable("General MIDI", "My kit", "");
        var changed = Enumerable.Range(0, 128).Where(n => table[n] != n).ToArray();
        Check("the processor's table sends 40 as 38 and 46 as 26, and every other note unchanged", table[40] == 38 && table[46] == 26 && changed.SequenceEqual(new[] { 40, 46 }), string.Join(",", changed));
        var noteMap = MidiProcessorCatalog.Create(MidiProcessorCatalog.Find("noteMap")!);
        var values = JsonNode.Parse(noteMap.Params)!.AsObject();
        values["From"] = "General MIDI"; values["To"] = "My kit"; values["Overrides"] = "36>35";
        noteMap.Params = values.ToJsonString();
        var engine = JsonNode.Parse(MidiProcessorCatalog.EngineParams(noteMap))!["Map"]!.AsArray();
        Check("the engine parameters carry the map's table, with overrides on top",
            engine[40]!.GetValue<int>() == 38 && engine[46]!.GetValue<int>() == 26 && engine[36]!.GetValue<int>() == 35 && engine[60]!.GetValue<int>() == 60, $"40->{engine[40]} 46->{engine[46]} 36->{engine[36]}");
        var store = PresetFolderStore.MidiProcessor("noteMap");
        Views.Presets.MidiProcessorPresetBar.Write(store, "Kit", noteMap);
        var preset = JsonNode.Parse(Views.Presets.MidiProcessorPresetBar.Read(store, "noteMap", "Kit") ?? "{}")!;
        Check("a note map processor preset keeps From, To and Overrides",
            (string?)preset["From"] == "General MIDI" && (string?)preset["To"] == "My kit" && (string?)preset["Overrides"] == "36>35", preset.ToJsonString());
        var text = DrumMapLibrary.Describe("General MIDI", "My kit", "");
        Check("the summary names the changed notes", text.Contains("40 Electric Snare → 38") && text.Contains("46 Open Hi-Hat → 26"), text);
        var longText = DrumMapLibrary.Describe("", "", "1>2 3>4 5>6 7>x", max: 2);
        Check("the summary of a map that changes nothing says so, and a long list ends in a count",
            DrumMapLibrary.Describe("General MIDI", "General MIDI", "") == "Every note is sent unchanged." && longText.EndsWith("+ 2 more", StringComparison.Ordinal), longText);
        Check("a map file without a note table loads as before", DrumMapLibrary.Find("Tabs to SD3") is { Remap: null } && DrumMapLibrary.BuildTable("General MIDI", "Tabs to SD3", "")[40] == 38, "Tabs to SD3");
    });

    private static void TestDrumMapEditorStore() => WithScratchDrumMaps(scratch =>
    {
        var gm = DrumMapLibrary.Find("General MIDI");
        var noPieces = Array.Empty<(string, string)>();
        var noSends = Array.Empty<(int, string)>();
        string? Error(string name, (string, string)[] pieces, (int, string)[] sends) => DrumMapDraft.Build(name, pieces, sends, gm).Error;
        Check("a map needs a name that is a file name", Error(" ", noPieces, noSends) is not null && Error("a/b", noPieces, noSends) is not null, "name");
        Check("a map cannot take the name of a map that comes with the app", Error("general midi", noPieces, noSends) is not null, "shipped name");
        Check("a note number outside 0 to 127 is refused",
            Error("K", new[] { ("128", "Snare") }, noSends) is not null && Error("K", new[] { ("d1", "Snare") }, noSends) is not null && Error("K", new[] { ("-1", "Snare") }, noSends) is not null, "range");
        Check("a note listed twice is refused, and so is a note without a piece name",
            Error("K", new[] { ("38", "Snare"), (" 38", "Rim") }, noSends) is not null && Error("K", new[] { ("38", " ") }, noSends) is not null, "duplicate");
        Check("a send-as value that is no note is refused; x means not sent",
            Error("K", noPieces, new[] { (40, "200") }) is not null && DrumMapDraft.Build("K", noPieces, new[] { (40, "x") }, gm).Draft is { } dropped && dropped.Remap[40] == -1, "send as");
        var paired = DrumMapDraft.Build("K", new[] { ("38", "Electric Snare"), ("", "") }, new[] { (40, "38"), (38, "38") }, gm, new Dictionary<int, int> { [99] = 98, [40] = 1 }).Draft!;
        Check("a send that piece names already give is not stored twice, and the table of notes without a row is kept",
            !paired.Remap.ContainsKey(40) && !paired.Remap.ContainsKey(38) && paired.Remap[99] == 98 && paired.Pieces.Count == 1, string.Join(",", paired.Remap));

        DrumMapDraft.Build("Kit A", new[] { ("38", "Snare") }, new[] { (40, "38") }, gm).Draft!.Save();
        var folder = Path.Combine(scratch, "DrumMaps");
        File.WriteAllText(Path.Combine(folder, "Broken.json"), "{ not json");
        var store = new DrumMapStore();
        var changes = 0; store.Changed += () => changes++;
        Check("the store lists the user's map files and names the unreadable one",
            store.Names().SequenceEqual(new[] { "Broken", "Kit A" }) && store.Unreadable().SequenceEqual(new[] { "Broken" }) && DrumMapLibrary.Find("Broken") is null,
            string.Join(",", store.Names()) + " / " + string.Join(",", store.Unreadable()));
        store.Duplicate("Kit A", "Kit B");
        Check("a duplicate is a second map under its own name with the same table", DrumMapLibrary.Find("Kit B") is { IsUser: true, Remap: not null } copy && copy.Remap![40] == 38 && DrumMapLibrary.Find("Kit A") is not null, "Kit B");
        store.Rename("Kit A", "Kit C");
        Check("a rename moves the map to the new name", DrumMapLibrary.Find("Kit C") is not null && DrumMapLibrary.Find("Kit A") is null && File.Exists(Path.Combine(folder, "Kit C.json")), string.Join(",", store.Names()));
        var refused = false;
        try { store.Rename("Kit C", "General MIDI"); } catch (InvalidDataException) { refused = true; }
        Check("a rename onto the name of a map that comes with the app is refused", refused && DrumMapLibrary.Find("Kit C") is not null, "refused");
        store.Delete("Kit B");
        Check("a delete removes the map, and every change was announced", DrumMapLibrary.Find("Kit B") is null && changes == 3, $"changes {changes}");
        Check("the maps that come with the app are listed first and one user map is left", DrumMapLibrary.All()[0] is { IsUser: false } && DrumMapLibrary.All().Count(m => m.IsUser) == 1, DrumMapLibrary.All().Count.ToString());
    });
}
