using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.NoteMaps;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the shipped drum maps and the General MIDI to kit conversion: the maps load, General MIDI to Superior Drummer 3
///   remaps kick, snare and hi-hat, a new map edits the General MIDI drums, and a saved custom map loads back with
///   its notes and its note table.
/// Does not own: the editor window (DrumMapEditorWindow), the checks (DrumMapDraftPresenter) or the map files (DrumMapLibrary).
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it or reads the scratch folder
public sealed class ShippedDrumMapTests
{
    [Fact]
    public void ShippedMapsLoadWithGeneralMidiKick()
    {
        DrumMapLibrary.Reload();
        Assert.True(DrumMapLibrary.All().Count >= 8);
        var general = DrumMapLibrary.Find("General MIDI");
        Assert.NotNull(general);
        Assert.True(general.Verified);
        Assert.Equal("Bass Drum 1", general.Notes[36]);
    }

    [Fact]
    public void GeneralMidiToTabsToSd3RemapsKickSnareAndHiHat()
    {
        DrumMapLibrary.Reload();
        var table = DrumMapLibrary.BuildTable("General MIDI", "Superior Drummer 3", "");
        Assert.Equal(36, table[36]);
        Assert.Equal(38, table[38]);
        Assert.Equal(61, table[42]);
        Assert.NotEqual("Every note is sent unchanged.", DrumMapLibrary.Describe("General MIDI", "Superior Drummer 3", ""));
    }

    [Fact]
    public void SameMapStaysIdentity()
    {
        DrumMapLibrary.Reload();
        var table = DrumMapLibrary.BuildTable("General MIDI", "General MIDI", "");
        Assert.True(table.Select((sent, note) => sent == note).All(same => same));
        Assert.Equal("Every note is sent unchanged.", DrumMapLibrary.Describe("General MIDI", "General MIDI", ""));
    }

    [AvaloniaFact]
    public void NewMapEditorShowsGeneralMidiRows()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        DrumMapLibrary.Reload();
        var window = new DrumMapEditorWindow(null, null);
        try
        {
            Assert.Equal(DrumMapLibrary.Find("General MIDI")!.Notes.Count, window.SendRowCount);
            Assert.True(window.Check());
        }
        finally { window.Close(); }
    }

    [Fact]
    public void CustomMapSaveLoadRoundTrip()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        DrumMapLibrary.Reload();
        var general = DrumMapLibrary.Find("General MIDI");
        Assert.NotNull(general);
        var pieces = general.Notes.Select(p => (Note: p.Key.ToString(CultureInfo.InvariantCulture), Piece: p.Value));
        var (draft, error) = DrumMapDraft.Build("Round trip kit", pieces, new[] { (Source: 36, SendAs: "38") }, general, null);
        Assert.Null(error);
        Assert.NotNull(draft);
        draft.Save();

        var loaded = DrumMapLibrary.Find("Round trip kit");
        Assert.NotNull(loaded);
        Assert.True(loaded.IsUser);
        Assert.Equal(general.Notes.Count, loaded.Notes.Count);
        Assert.Equal("Bass Drum 1", loaded.Notes[36]);
        var table = DrumMapLibrary.BuildTable("General MIDI", "Round trip kit", "");
        Assert.Equal(38, table[36]);
        Assert.Equal(38, table[38]);
    }

    [Fact]
    public void EngineParamsCarryTheConvertedTable()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        DrumMapLibrary.Reload();
        var processor = new PluginMidiProcessor
        {
            Type = "noteMap",
            Params = "{\"InCh\":0,\"NoteMode\":0,\"NoteSet\":\"\",\"From\":\"General MIDI\",\"To\":\"Superior Drummer 3\",\"Overrides\":\"\"}",
        };
        var engine = JsonNode.Parse(MidiProcessorCatalog.EngineParams(processor))!.AsObject();
        var map = engine["Map"]!.AsArray();
        Assert.Equal(128, map.Count);
        Assert.Equal(61, map[42]!.GetValue<int>());
    }
}
