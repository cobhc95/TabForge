using System;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: that every track's panel, staff type and kind match its instrument (piano, synth, pad: keyboard and no TAB; guitar, bass: fretboard and TAB; drums: drum map),
/// in the demo song, after a Guitar Pro round trip, and after the load repair of a keyboard stored as a guitar.
/// Tests: TrackInstrumentRules, InstrumentViewRules, FullDemoSongFactory, GuitarProImporter.
/// </summary>
public class TrackInstrumentRulesTests
{
    [Fact]
    public void PianoTrackIsKeyboardWithoutTab()
    {
        var piano = new TrackModel { Kind = TrackKind.Keys, InstrumentName = "Acoustic Grand Piano", MidiProgram = 0, StringTunings = new() { 96, 84, 72, 60, 48, 36, 24 } };
        Assert.Equal(InstrumentViewKind.Keyboard, InstrumentViewRules.For(piano));
    }

    [Fact]
    public void FrettedAndDrumTracksKeepTheirNotation()
    {
        var sub = new TrackModel { Kind = TrackKind.Bass, InstrumentName = "Synth Bass 1", MidiProgram = 38, StringTunings = new() { 50, 45, 40, 33 } };
        Assert.Equal(InstrumentViewKind.Fretboard, InstrumentViewRules.For(sub));
        Assert.True(TrackInstrumentRules.HasTab(sub));
        
        var drums = new TrackModel { Kind = TrackKind.Drums, MidiChannel = 9 };
        Assert.Equal(InstrumentViewKind.DrumMap, InstrumentViewRules.For(drums));
        Assert.Equal(NotationMode.TabOnly, TrackInstrumentRules.Effective(NotationMode.TabOnly, drums));
    }

    [Fact]
    public void DemoSongTracksMatchTheirInstruments()
    {
        var song = FullDemoSongFactory.Create();
        foreach (var t in song.Tracks)
        {
            var expected = t.Kind switch
            {
                TrackKind.Drums => InstrumentViewKind.DrumMap,
                TrackKind.Keys => InstrumentViewKind.Keyboard,
                _ => InstrumentViewKind.Fretboard,
            };
            Assert.Equal(expected, InstrumentViewRules.For(t));
        }
        Assert.Equal(TrackKind.Keys, song.Tracks.Single(t => t.Name == "Piano").Kind);
        Assert.Equal(TrackKind.Bass, song.Tracks.Single(t => t.Name.StartsWith("Sub", StringComparison.Ordinal)).Kind);
    }

    [Fact]
    public void GuitarProRoundTripKeepsKeysAsKeys()
    {
        var back = GuitarProImporter.ImportBytes(GuitarProExporter.ToBytes(FullDemoSongFactory.Create(), embedProject: false), "demo.gp");
        foreach (var t in back.Tracks.Where(t => t.MidiProgram is 0 or 89 && t.Kind != TrackKind.Drums && t.Name is "Piano" or "Pad"))
            Assert.Equal(InstrumentViewKind.Keyboard, InstrumentViewRules.For(t));
    }

    [Fact]
    public void LadderTunedGuitarKindShowsKeyboardButKeepsKindAndTab()
    {
        var piano = new TrackModel { Name = "Piano", Kind = TrackKind.Guitar, InstrumentName = "Acoustic Grand Piano (7 strings)", MidiProgram = 0, StringTunings = new() { 96, 84, 72, 60, 48, 36, 24 } };
        var lead = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiProgram = 29, StringTunings = new() { 64, 59, 55, 50, 45, 40, 35 } };
        Assert.Equal(InstrumentViewKind.Keyboard, InstrumentViewRules.For(piano));
        Assert.Equal(TrackKind.Guitar, piano.Kind);
        Assert.Equal(InstrumentViewKind.Fretboard, InstrumentViewRules.For(lead));
    }
}
