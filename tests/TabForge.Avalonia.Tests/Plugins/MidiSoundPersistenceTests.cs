using System.Linq;
using System.Text.Json;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the GM sound flags surviving a save and reopen, so a VST instrument that plays still unticks an automatic GM sound after a reload,
/// and a manual choice holds.
/// Does not own: the save format itself (ProjectService), the live switching rule (MixerGroups.ApplyAutoGm) or the FX window.
/// Tests: this class.
/// </summary>
public class MidiSoundPersistenceTests
{
    private static TrackModel DrumTrack(bool midiSound, bool auto, bool manualOff)
    {
        var track = new TrackModel
        {
            Name = "Drums",
            SoundSource = SoundSources.Plugins,
            MidiSound = midiSound,
            MidiSoundAuto = auto,
            MidiSoundManualOff = manualOff,
        };
        track.Rig.Plugins.Add(new PluginSlot { Name = "Superior Drummer 3", Type = PluginSlotType.Instrument, Enabled = true });
        return track;
    }

    private static TrackModel Reopen(TrackModel track) => JsonSerializer.Deserialize<TrackModel>(JsonSerializer.Serialize(track))!;

    [Fact]
    public void AnAutoTickedGmSoundIsUntickedWhenTheInstrumentPlaysAfterReopen()
    {
        var reopened = Reopen(DrumTrack(midiSound: true, auto: true, manualOff: false));

        Assert.True(reopened.MidiSoundAuto);
        Assert.True(MixerGroups.ApplyAutoGm(reopened, new MixerOptions()));
        Assert.False(reopened.MidiSound);
    }

    [Fact]
    public void AManualUntickStillHoldsAfterReopen()
    {
        var reopened = Reopen(DrumTrack(midiSound: false, auto: false, manualOff: true));

        Assert.False(MixerGroups.ApplyAutoGm(reopened, new MixerOptions { AutoGmSound = true }));
        Assert.False(reopened.MidiSound);
    }

    [Fact]
    public void AManualGmSoundNextToAPlayingInstrumentIsKeptAfterReopen()
    {
        var reopened = Reopen(DrumTrack(midiSound: true, auto: false, manualOff: false));

        Assert.False(MixerGroups.ApplyAutoGm(reopened, new MixerOptions()));
        Assert.True(reopened.MidiSound);
        Assert.True(reopened.Rig.Plugins.Single().Enabled);
    }
}
