using TabForge.Models;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>A chain saved as the default for one instrument type (see <see cref="AutoChains"/>); property names are the settings file format.</summary>
public sealed class AutoChain
{
    public string Key { get; set; } = "";
    /// <summary>GM sound as it was when the chain was saved (older settings files). Not applied: only <see cref="MidiSoundManualOn"/> is the user's choice.</summary>
    public bool MidiSound { get; set; } = true;
    /// <summary>The user had ticked GM sound themselves when saving: tracks that get this chain keep GM playing beside the instrument.</summary>
    public bool MidiSoundManualOn { get; set; }
    public List<PluginSlot> Plugins { get; set; } = new();
}

// Owns: the default FX chain per instrument type: saving it, and looking it up for a track of that type.
// Does not own: the FX window that edits a chain, or the plug-in state of a chain (ChainStateStore).
// Tests: no dedicated test class.

/// <summary>Per-instrument default FX chains ("Auto-load for this instrument"): saved from the FX window, applied to tracks of that type.</summary>
public static class AutoChains
{
    /// <summary>The instrument type of a track: the drum kit, else its General MIDI program.</summary>
    public static string KeyOf(TrackModel track) => track.IsDrumTrack ? "drums" : $"gm:{track.MidiProgram}";

    public static AutoChain? Find(PluginSettings settings, TrackModel track) => settings.AutoChains.FirstOrDefault(a => a.Key == KeyOf(track));

    public static bool Has(PluginSettings settings, TrackModel track) => Find(settings, track) is not null;

    public static void Save(PluginSettings settings, TrackModel track)
    {
        Remove(settings, track);
        var plugins = Clone(track.Rig.Plugins);
        ChainTree.Pack(plugins, track.Rig.Containers);   // on the copy: the containers travel with the list
        ChainStateStore.Externalise(plugins);   // states go to their own files, not into settings.json
        settings.AutoChains.Add(new AutoChain { Key = KeyOf(track), MidiSound = track.MidiSound, MidiSoundManualOn = track.MidiSound && track.MidiSoundManualOn, Plugins = plugins });
    }

    public static void Remove(PluginSettings settings, TrackModel track) => settings.AutoChains.RemoveAll(a => a.Key == KeyOf(track));

    /// <summary>Gives a track without plug-ins the default chain of its instrument type. True when it changed.</summary>
    public static bool Apply(PluginSettings settings, TrackModel track)
    {
        if (track.IsAudio || track.Rig.Plugins.Count > 0 || Find(settings, track) is not { Plugins.Count: > 0 } auto) return false;
        track.Rig.Plugins = Clone(auto.Plugins);
        ChainStateStore.Resolve(track.Rig.Plugins);
        ChainTree.Refresh(track.Rig);
        track.SoundSource = SoundSources.Plugins;
        // GM sound follows the instrument (off while it plays, see MixerGroups.ApplyAutoGm) unless the user ticked it on for this chain.
        track.MidiSound = auto.MidiSoundManualOn;
        track.MidiSoundManualOn = auto.MidiSoundManualOn;
        track.MidiSoundManualOff = false;
        track.MidiSoundAuto = false;
        return true;
    }

    public static void Apply(PluginSettings settings, SongProject project)
    {
        if (settings.AutoChains.Count == 0) return;
        foreach (var t in project.Tracks) Apply(settings, t);
    }

    private static List<PluginSlot> Clone(List<PluginSlot> plugins) =>
        plugins.Select(p => p.Clone(keepId: false)).ToList();
}
