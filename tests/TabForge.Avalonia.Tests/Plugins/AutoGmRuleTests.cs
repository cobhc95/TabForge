using System.Collections.Generic;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the automatic GM sound rule: GM off whenever a VST instrument plays, on when none does, and the user's own tick or untick always wins,
//   including a track that opens with GM on and then auto-loads its instrument. Does not own: the FX window's check boxes (FxChainOptionsTests).
// Tests: this class.
public class AutoGmRuleTests
{
    private static TrackModel Track(bool instrument) => new()
    {
        Name = "Drums", SoundSource = instrument ? SoundSources.Plugins : SoundSources.Midi, MidiSound = true,
        Rig = new RigPreset { Plugins = instrument ? new List<PluginSlot> { new() { Name = "Superior Drummer 3", Type = PluginSlotType.Instrument } } : new() },
    };

    [Fact]
    public void AnInstrumentTurnsGmOffEvenWhenTheTrackOpenedWithGmOn()
    {
        var track = Track(instrument: true);   // a song opened with GM ticked (the default), then its auto-load instrument arrived
        Assert.True(MixerGroups.ApplyAutoGm(track, new MixerOptions()));
        Assert.False(track.MidiSound);
    }

    [Fact]
    public void AManualTickKeepsGmBesideTheInstrument()
    {
        var track = Track(instrument: true);
        track.MidiSoundManualOn = true;
        Assert.False(MixerGroups.ApplyAutoGm(track, new MixerOptions()));
        Assert.True(track.MidiSound);
    }

    [Fact]
    public void GmComesBackWhenTheInstrumentStopsPlaying()
    {
        var track = Track(instrument: true);
        MixerGroups.ApplyAutoGm(track, new MixerOptions());
        track.Rig.Plugins[0].Enabled = false;
        Assert.True(MixerGroups.ApplyAutoGm(track, new MixerOptions()));
        Assert.True(track.MidiSound);
    }

    [Fact]
    public void AManualUntickKeepsGmOffWithNoInstrument()
    {
        var track = Track(instrument: true);
        track.MidiSound = false; track.MidiSoundManualOff = true;
        track.Rig.Plugins[0].Enabled = false;
        Assert.False(MixerGroups.ApplyAutoGm(track, new MixerOptions()));
        Assert.False(track.MidiSound);
    }

    [Fact]
    public void AutoLoadDoesNotBringBackTheGmTickSavedWithTheChain()
    {
        var settings = new PluginSettings();
        settings.AutoChains.Add(new AutoChain { Key = "drums", MidiSound = true, Plugins = new List<PluginSlot> { new() { Name = "SD3", Type = PluginSlotType.Instrument } } });
        var track = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, MidiSound = true };
        Assert.True(AutoChains.Apply(settings, track));
        Assert.False(track.MidiSound);
        Assert.False(MixerGroups.ApplyAutoGm(track, new MixerOptions()));
        Assert.False(track.MidiSound);
    }

    [Fact]
    public void AutoLoadKeepsAManualGmChoiceSavedWithTheChain()
    {
        var settings = new PluginSettings();
        settings.AutoChains.Add(new AutoChain { Key = "drums", MidiSound = true, MidiSoundManualOn = true, Plugins = new List<PluginSlot> { new() { Name = "SD3", Type = PluginSlotType.Instrument } } });
        var track = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9 };
        AutoChains.Apply(settings, track);
        MixerGroups.ApplyAutoGm(track, new MixerOptions());
        Assert.True(track.MidiSound);
    }
}
