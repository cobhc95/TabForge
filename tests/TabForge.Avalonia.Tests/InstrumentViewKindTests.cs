using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: what "Match the instrument" shows for each kind of instrument (Core InstrumentViewRules), that the panel follows the active track and the
/// instrument of the track, and that the manual choices still win when "Match the instrument" is off.
/// Tests: InstrumentViewRules, InstrumentPanelStrip.Refresh, FretboardSettingsBinding.KindFor.
/// </summary>
public class InstrumentViewKindTests
{
    private static TrackModel Track(TrackKind kind, string instrument, int program, int channel = 0) =>
        new() { Kind = kind, InstrumentName = instrument, MidiProgram = program, MidiChannel = channel, Measures = TemplateFactory.Measures(2) };

    /// <summary>A track as the instrument picker leaves it: the catalogue sound applied to a fresh guitar track.</summary>
    private static TrackModel Picked(string sound)
    {
        var project = new SongProject();
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Guitar);
        Assert.True(AudioToInstrumentConversion.ApplyInstrument(project, track, sound));
        return track;
    }

    [Theory]
    [InlineData("Acoustic Guitar (nylon)", InstrumentViewKind.Fretboard)]
    [InlineData("Distortion Guitar", InstrumentViewKind.Fretboard)]
    [InlineData("Electric Bass (finger)", InstrumentViewKind.Fretboard)]
    [InlineData("Synth Bass 1", InstrumentViewKind.Fretboard)]
    [InlineData("Cello", InstrumentViewKind.Fretboard)]
    [InlineData("Violin", InstrumentViewKind.Fretboard)]
    [InlineData("Banjo", InstrumentViewKind.Fretboard)]
    [InlineData("Sitar", InstrumentViewKind.Fretboard)]
    [InlineData("Acoustic Grand Piano", InstrumentViewKind.Keyboard)]
    [InlineData("Harpsichord", InstrumentViewKind.Keyboard)]
    [InlineData("Church Organ", InstrumentViewKind.Keyboard)]
    [InlineData("Flute", InstrumentViewKind.Keyboard)]
    [InlineData("Tenor Sax", InstrumentViewKind.Keyboard)]
    [InlineData("Orchestral Harp", InstrumentViewKind.Keyboard)]
    [InlineData("Bassoon", InstrumentViewKind.Keyboard)]
    [InlineData("Drum Kit (Standard)", InstrumentViewKind.DrumMap)]
    public void EveryCatalogueSoundHasItsView(string sound, InstrumentViewKind expected) =>
        Assert.Equal(expected, InstrumentViewRules.For(Picked(sound)));

    [Fact]
    public void UkuleleAndStringTracksGetAFretboardAndDrumChannelsADrumMap()
    {
        Assert.Equal(InstrumentViewKind.Fretboard, InstrumentViewRules.For(Track(TrackKind.Other, "Ukulele", 24)));
        Assert.Equal(InstrumentViewKind.Fretboard, InstrumentViewRules.For(Track(TrackKind.Guitar, "Electric Guitar", 29)));
        Assert.Equal(InstrumentViewKind.DrumMap, InstrumentViewRules.For(Track(TrackKind.Drums, "Anything", 5, channel: 9)));
        Assert.Equal(InstrumentViewKind.Keyboard, InstrumentViewRules.For(Track(TrackKind.Keys, "Grand Piano", 0)));
    }

    [Fact]
    public void EveryGmProgramMapsToOneView()
    {
        for (var program = 0; program < 128; program++)
        {
            var expected = program is >= 24 and <= 43 or 104 or 105 or 106 or 110 ? InstrumentViewKind.Fretboard : InstrumentViewKind.Keyboard;
            Assert.Equal(expected, InstrumentViewRules.ForProgram(program));
        }
    }

    [AvaloniaFact]
    public void PanelFollowsTheActiveTrackAndItsInstrument()
    {
        var project = new SongProject();
        project.Tracks.Add(Picked("Distortion Guitar"));
        project.Tracks.Add(Picked("Acoustic Grand Piano"));
        project.Tracks.Add(Picked("Drum Kit (Standard)"));
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = project });
        var strip = new InstrumentPanelStrip(score);
        score.ShowTrack(0); strip.Refresh();
        Assert.Equal(InstrumentPanelKind.Fretboard, strip.Panel.Kind);
        score.ShowTrack(1);
        Assert.Equal(InstrumentPanelKind.Keyboard, strip.Panel.Kind);
        score.ShowTrack(2);
        Assert.Equal(InstrumentPanelKind.Drums, strip.Panel.Kind);

        score.ShowTrack(1);
        Assert.True(AudioToInstrumentConversion.ApplyInstrument(project, project.Tracks[1], "Flute"));
        strip.Refresh();
        Assert.Equal(InstrumentPanelKind.Keyboard, strip.Panel.Kind);
        Assert.True(AudioToInstrumentConversion.ApplyInstrument(project, project.Tracks[1], "Cello"));
        strip.Refresh();
        Assert.Equal(InstrumentPanelKind.Fretboard, strip.Panel.Kind);
    }

    [Fact]
    public void ManualViewsOverrideMatchTheInstrument()
    {
        var piano = Picked("Acoustic Grand Piano");
        Assert.Equal(InstrumentPanelKind.Keyboard, FretboardSettingsBinding.KindFor(piano, InstrumentViews.MatchInstrument));
        Assert.Equal(InstrumentPanelKind.Fretboard, FretboardSettingsBinding.KindFor(piano, InstrumentViews.Fretboard));
        Assert.Equal(InstrumentPanelKind.Keyboard, FretboardSettingsBinding.KindFor(Picked("Distortion Guitar"), InstrumentViews.Keyboard));
        Assert.Equal(InstrumentPanelKind.Drums, FretboardSettingsBinding.KindFor(piano, InstrumentViews.Drums));
    }
}
