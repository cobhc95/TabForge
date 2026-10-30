using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Articulations;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the track-level articulation map (a track with no instrument), the Export MIDI rows for built-in versus plug-in tracks, and an export
//   that picks a map for a built-in track from the dialog.
// Does not own: the compile (ScoreToMidiCompiler) or the window's layout (MidiExportWindowLayoutTests).
// Tests: this class. Headless where a window is used; files live under Path.GetTempPath().
[Collection("PluginLibraryRoot")]
public sealed class MidiArticulationTrackMapTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-track-map-{Guid.NewGuid():N}");

    public MidiArticulationTrackMapTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } }

    private static SongProject BuildSong(bool plugIn)
    {
        var song = new SongProject { Title = "Track map", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var track = new TrackModel
        {
            Name = plugIn ? "Guitar I" : "Built-in lead", Kind = TrackKind.Guitar, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1),
        };
        if (plugIn)
        {
            track.SoundSource = SoundSources.Plugins;
            track.MidiSound = false;
            track.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Guitar", Type = TabForge.Plugins.PluginSlotType.Instrument });
        }
        song.Tracks.Add(track);
        return song;
    }

    private static void PutPalmMute(TrackModel track)
    {
        var beat = track.Measures[0].Cells[0];
        beat.DurationDenominator = 4;
        var note = new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5), Velocity = 90 };
        note.Techniques.Add(TechniqueNames.PalmMute);
        beat.Notes.Add(note);
    }

    private static ArticulationMap PalmMap() =>
        new() { Rules = { [TechniqueNames.PalmMute] = new ArticulationRule(ArticulationAction.Keyswitch, 26, 0, ArticulationMode.Latched) } };

    [Fact]
    public void TrackLevelMapIsSavedWithTheSongAndReloaded()
    {
        var song = BuildSong(plugIn: false);
        var track = song.Tracks[0];
        ArticulationMapStore.Store(track, null, PalmMap());
        var path = Path.Combine(_folder, "map.tforge");
        ProjectService.Save(path, song);
        var loaded = ProjectService.Load(path).Tracks[0];

        Assert.NotNull(loaded.ArticulationBindings);
        Assert.True(ArticulationMapStore.MapOf(loaded, null).Rules.ContainsKey(TechniqueNames.PalmMute));
        Assert.Null(loaded.Rig.Plugins.FirstOrDefault(p => p.Type == TabForge.Plugins.PluginSlotType.Instrument));
    }

    [Fact]
    public void TrackMapBecomesTheInstrumentsMapWhenAnInstrumentIsAddedLater()
    {
        var song = BuildSong(plugIn: false);
        var track = song.Tracks[0];
        ArticulationMapStore.Store(track, null, PalmMap());
        track.SoundSource = SoundSources.Plugins;
        track.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Guitar", Type = TabForge.Plugins.PluginSlotType.Instrument });

        Assert.True(ArticulationExportOptions.DefaultMapOf(track)?.Rules.ContainsKey(TechniqueNames.PalmMute) == true);
    }

    [AvaloniaFact]
    public void ExportDialogOffersAMapOnABuiltInTrackWhileItsDefaultStaysNone()
    {
        var song = BuildSong(plugIn: false);
        song.Tracks.Add(BuildSong(plugIn: true).Tracks[0]);
        var settings = new MidiExportSettings { UseArticulationMap = true };
        var window = new MidiExportWindow(song, settings, hasSelection: false, snip: null, selection: null,
            saveFile: null, quietSave: () => { }, report: _ => { }, openArticulations: _ => { });
        window.Show();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        try
        {
            var selectors = window.GetVisualDescendants().OfType<ArticulationPresetSelector>().ToList();
            var links = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "TechniqueEdit").ToList();
            Assert.Equal(2, selectors.Count);
            Assert.Equal(2, links.Count);

            Assert.True(selectors[0].IsEnabled);
            Assert.Equal("None (keep the TabForge sound)", selectors[0].SelectedEntry!.Text);
            Assert.Equal("Track map (attached)", selectors[1].SelectedEntry!.Text);
            Assert.All(links, link => Assert.True(link.IsEnabled));

            var status = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text is { } text && text.StartsWith("Built-in sound", StringComparison.Ordinal));
            Assert.Equal("Built-in sound: no map by default. Pick a map to write its keyswitches for another program.", status.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BuiltInTrackLinkOpensTheMapForThatTrack()
    {
        var song = BuildSong(plugIn: false);
        TrackModel? opened = null;
        var settings = new MidiExportSettings { UseArticulationMap = true };
        var window = new MidiExportWindow(song, settings, hasSelection: false, snip: null, selection: null,
            saveFile: null, quietSave: () => { }, report: _ => { }, openArticulations: track => opened = track);
        window.Show();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        try
        {
            var link = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "TechniqueEdit");
            link.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Same(song.Tracks[0], opened);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task PickingAPresetForABuiltInTrackExportsItsKeyswitch()
    {
        var song = BuildSong(plugIn: false);
        PutPalmMute(song.Tracks[0]);
        var path = Path.Combine(_folder, "picked.mid");
        var settings = new MidiExportSettings { UseArticulationMap = true };
        var window = new MidiExportWindow(song, settings, hasSelection: false, snip: null, selection: null,
            saveFile: (_, _) => path, quietSave: () => { }, report: _ => { }, openArticulations: _ => { });
        window.Show();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        try
        {
            var selector = window.GetVisualDescendants().OfType<ArticulationPresetSelector>().First();
            var preset = selector.Items.OfType<ArticulationPresetSelector.Entry>()
                .First(e => e.Name == ArticulationPresets.AmpleName);
            selector.SelectedItem = preset;
            DialogTestKit.RunQueuedUiWork();
            await window.ExportPressed();
        }
        finally { window.Close(); }

        Assert.True(File.Exists(path));
        var midi = MidiArticulationVerifyTests.ParseMidi(File.ReadAllBytes(path));
        Assert.Contains(midi.Notes, n => n.Pitch == 26 && n.Velocity == 100);
    }
}
