using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// "Auto-switch to GM sound when no VST instrument plays" (vst.autogm): every way a track's instrument can stop playing (FX button, chain window
/// on/off, bypass, removal, quarantine after a crash, untrusted or missing file, failed load) makes the General MIDI sound take over, it hands
/// back when the instrument plays again, and the user's own tick or untick is respected. Synthetic tracks only, no engine.
/// </summary>
public static partial class SelfTest
{
    private static TrackModel AutoGmTrack(string path = "")
    {
        var t = new TrackModel { Name = "Keys", Kind = TrackKind.Keys, SoundSource = SoundSources.Plugins, MidiSound = false };   // as added with an instrument: GM off
        t.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = path, Type = PluginSlotType.Instrument, Format = "VST3" });
        t.Rig.Plugins.Add(new PluginSlot { Name = "Delay", Path = "", Type = PluginSlotType.Effect, Format = "VST3" });
        return t;
    }

    private static void TestAutoGmEveryPath()
    {
        var before = TabForge.Audio.AudioEngineClient.Instance.Mixer.AutoGmSound;
        TabForge.Audio.AudioEngineClient.Instance.Mixer.AutoGmSound = true;
        try
        {
            // The track-row FX button and the chain window's "Through chain" both set SoundSource, then run the same engine sync (ApplyAutoGm).
            var t = AutoGmTrack();
            MixerGroups.ApplyAutoGm(t, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: the instrument plays, GM stays off and only the instrument sounds", !t.MidiSound && MixerGroups.RouteOf(t, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.Instrument && !MixerGroups.GmSounds(t, TabForge.Audio.AudioEngineClient.Instance.Mixer));
            t.SoundSource = SoundSources.Midi;   // FX button / chain off
            MixerGroups.ApplyAutoGm(t, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: FX button / chain off: GM takes over (the track is not silent)", t.MidiSound && MixerGroups.GmSounds(t, TabForge.Audio.AudioEngineClient.Instance.Mixer) && MixerGroups.RouteOf(t, TabForge.Audio.AudioEngineClient.Instance.Mixer) != TrackRoute.Silent && MixerGroups.MidiInEngine(t, TabForge.Audio.AudioEngineClient.Instance.Mixer));
            t.SoundSource = SoundSources.Plugins;   // switched back on
            MixerGroups.ApplyAutoGm(t, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: chain on again: the automatic GM tick is taken back", !t.MidiSound && !MixerGroups.GmSounds(t, TabForge.Audio.AudioEngineClient.Instance.Mixer));

            t.Rig.Plugins[0].Enabled = false;   // instrument bypassed
            MixerGroups.ApplyAutoGm(t, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: instrument bypassed: GM takes over", t.MidiSound && MixerGroups.GmSounds(t, TabForge.Audio.AudioEngineClient.Instance.Mixer));
            t.Rig.Plugins[0].Enabled = true;
            MixerGroups.ApplyAutoGm(t, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: instrument back on: GM switches off again", !t.MidiSound && !MixerGroups.GmSounds(t, TabForge.Audio.AudioEngineClient.Instance.Mixer));

            // The instrument removed: with effects left, and with the chain empty (chain on, nothing in it).
            var removed = AutoGmTrack();
            removed.Rig.Plugins.RemoveAt(0);
            MixerGroups.ApplyAutoGm(removed, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: instrument removed (effects remain): GM takes over", removed.MidiSound && MixerGroups.GmSounds(removed, TabForge.Audio.AudioEngineClient.Instance.Mixer));
            var empty = AutoGmTrack();
            empty.Rig.Plugins.Clear();
            MixerGroups.ApplyAutoGm(empty, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: the last plug-in removed with the chain still on: GM takes over (was silent)", empty.MidiSound && MixerGroups.RouteOf(empty, TabForge.Audio.AudioEngineClient.Instance.Mixer) != TrackRoute.Silent);
            var plain = new TrackModel { Name = "Plain", MidiSound = false };
            Check("auto GM: a plain track without a chain is left alone", !MixerGroups.ApplyAutoGm(plain, TabForge.Audio.AudioEngineClient.Instance.Mixer) && !plain.MidiSound);

            // Untrusted / missing file, quarantined (the instrument crashed), failed load.
            var missing = @"C:\TabForgeAutoGmTest\does-not-exist\Missing Synth.vst3";
            var settings = new PluginSettings();
            var client = new AudioEngineClient();
            var quarantine = new List<string>();
            client.Quarantine = () => quarantine;
            var gone = AutoGmTrack(missing);
            client.RefreshAvailability(new[] { gone }, settings);
            MixerGroups.ApplyAutoGm(gone, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: a missing / untrusted plug-in file: the slot is unavailable and GM takes over", gone.Rig.Plugins[0].Unavailable && gone.MidiSound && MixerGroups.GmSounds(gone, TabForge.Audio.AudioEngineClient.Instance.Mixer));
            quarantine.Add(missing);
            client.RefreshAvailability(new[] { gone }, settings);
            Check("auto GM: a quarantined (crashed) instrument counts as not playing", gone.Rig.Plugins[0].Unavailable && !MixerGroups.InstrumentPlays(gone));
            var fine = AutoGmTrack();
            client.RefreshAvailability(new[] { fine }, settings);
            Check("auto GM: a plug-in with no file to load is never marked unavailable", !fine.Rig.Plugins[0].Unavailable && MixerGroups.InstrumentPlays(fine));

            var ack = new ChainAck(3, 5, new[] { new PluginLoadResult(0, PluginLoadStatus.Failed, "x"), new PluginLoadResult(1, PluginLoadStatus.Loaded, "y") });
            Check("auto GM: a failed load of the current request marks that plug-in unavailable", AudioEngineClient.InstrumentFailed(ack, 5, 0) && !AudioEngineClient.InstrumentFailed(ack, 5, 1));
            Check("auto GM: an acknowledgement of an older request, or none, says nothing", !AudioEngineClient.InstrumentFailed(ack, 6, 0) && !AudioEngineClient.InstrumentFailed(null, 5, 0));
            var failed = AutoGmTrack();
            failed.Rig.Plugins[0].Unavailable = true;
            MixerGroups.ApplyAutoGm(failed, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            var gmOnWhileFailed = failed.MidiSound && MixerGroups.GmSounds(failed, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            failed.Rig.Plugins[0].Unavailable = false;
            MixerGroups.ApplyAutoGm(failed, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: failed load: GM takes over, and hands back when the instrument loads", gmOnWhileFailed && !failed.MidiSound);
            Check("auto GM: the unavailable flag is runtime only (not saved with the song)", !System.Text.Json.JsonSerializer.Serialize(new PluginSlot { Unavailable = true }).Contains("Unavailable"));

            // Manual override: an untick by the user stays silent; a tick by the user stays on when the instrument returns.
            var manualOff = AutoGmTrack();
            manualOff.MidiSound = false; manualOff.MidiSoundAuto = false; manualOff.MidiSoundManualOff = true;   // what the GM checkbox does on an untick
            manualOff.SoundSource = SoundSources.Midi;
            MixerGroups.ApplyAutoGm(manualOff, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: GM unticked by the user stays off with the chain off (silence is what they asked for)", !manualOff.MidiSound && !MixerGroups.GmSounds(manualOff, TabForge.Audio.AudioEngineClient.Instance.Mixer) && MixerGroups.RouteOf(manualOff, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.Silent);
            var manualOn = AutoGmTrack();
            manualOn.SoundSource = SoundSources.Midi;
            MixerGroups.ApplyAutoGm(manualOn, TabForge.Audio.AudioEngineClient.Instance.Mixer);   // automatic tick
            manualOn.MidiSound = true; manualOn.MidiSoundAuto = false; manualOn.MidiSoundManualOff = false;   // the user ticks it by hand
            manualOn.SoundSource = SoundSources.Plugins;
            MixerGroups.ApplyAutoGm(manualOn, TabForge.Audio.AudioEngineClient.Instance.Mixer);
            Check("auto GM: GM ticked by the user stays on when the instrument plays again", manualOn.MidiSound);

            TabForge.Audio.AudioEngineClient.Instance.Mixer.AutoGmSound = false;
            var off = AutoGmTrack(); off.SoundSource = SoundSources.Midi;
            Check("auto GM: with the setting off nothing is switched automatically", !MixerGroups.ApplyAutoGm(off, TabForge.Audio.AudioEngineClient.Instance.Mixer) && !off.MidiSound);
        }
        finally { TabForge.Audio.AudioEngineClient.Instance.Mixer.AutoGmSound = before; }
    }
}
