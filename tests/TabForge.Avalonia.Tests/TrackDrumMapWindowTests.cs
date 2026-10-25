using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track custom drum map window (rows, reset from a preset) and its hook in Track properties (OK writes the map as Custom).
/// Does not own: the MIDI drum map file editor (DrumMapEditorWindowTests).
/// Tests: this class.
/// </summary>
public class TrackDrumMapWindowTests
{
    [AvaloniaFact]
    public void ShowsOneRowPerPercussionSoundFromTheTrack()
    {
        var track = new TrackModel { Kind = TrackKind.Drums, MidiChannel = 9, DrumMapPreset = DrumMaps.GuitarPro5 };
        var window = new TrackDrumMapWindow(track);
        window.Show();
        Assert.Equal("Custom drum map", window.Title);
        Assert.Equal(PercussionKeyMapRules.Names.Count, window.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "DrumMapRows").Children.Count);
        Assert.Equal(DrumMaps.For(track, 36).TabLine, window.Entries.Single(e => e.Midi == 36).TabLine);
    }

    [AvaloniaFact]
    public void EditedMapIsWrittenAsCustomOnOk()
    {
        var song = TabForge.Presets.DemoSongFactory.Create();
        var track = song.Tracks.First(t => t.Kind == TrackKind.Drums);
        var window = new TrackPropertiesWindow(track);
        window.Show();
        var map = CustomDrumMapRows.FromTrack(window.DrumMapProbe());
        map.Single(e => e.Midi == 36).Label = "KK";
        window.TakeCustomMap(map);
        var box = window.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "DrumMapBox");
        Assert.Equal(DrumMaps.Custom, (box.SelectedItem as ComboBoxItem)?.Tag);
        DialogTestKit.Click(window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "OkButton"));
        DialogTestKit.RunQueuedUiWork();   // OK writes the edit after the frame that hides the window
        Assert.Equal(DrumMaps.Custom, track.DrumMapPreset);
        Assert.Equal("KK", DrumMaps.For(track, 36).Label);
    }
}
