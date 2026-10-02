using System.Text.Json;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>A chain saved as the default for one instrument type (see <see cref="AutoChains"/>); property names are the settings file format.</summary>
public sealed class AutoChain
{
    public string Key { get; set; } = "";
    public bool MidiSound { get; set; } = true;
    public List<PluginSlot> Plugins { get; set; } = new();
}

/// <summary>Per-instrument default FX chains ("Auto-load for this instrument"): saved from the FX window, applied to tracks of that type.</summary>
public static class AutoChains
{
    /// <summary>The instrument type of a track: the drum kit, else its General MIDI program.</summary>
    public static string KeyOf(TrackModel track) => track.Kind == TrackKind.Drums || track.MidiChannel == 9 ? "drums" : $"gm:{track.MidiProgram}";

    public static AutoChain? Find(PluginSettings settings, TrackModel track) => settings.AutoChains.FirstOrDefault(a => a.Key == KeyOf(track));

    public static bool Has(PluginSettings settings, TrackModel track) => Find(settings, track) is not null;

    public static void Save(PluginSettings settings, TrackModel track)
    {
        Remove(settings, track);
        var plugins = Clone(track.Rig.Plugins);
        ChainStateStore.Externalise(plugins);   // states go to their own files, not into settings.json
        settings.AutoChains.Add(new AutoChain { Key = KeyOf(track), MidiSound = track.MidiSound, Plugins = plugins });
    }

    public static void Remove(PluginSettings settings, TrackModel track) => settings.AutoChains.RemoveAll(a => a.Key == KeyOf(track));

    /// <summary>Gives a track without plug-ins the default chain of its instrument type. True when it changed.</summary>
    public static bool Apply(PluginSettings settings, TrackModel track)
    {
        if (track.IsAudio || track.Rig.Plugins.Count > 0 || Find(settings, track) is not { Plugins.Count: > 0 } auto) return false;
        track.Rig.Plugins = Clone(auto.Plugins);
        ChainStateStore.Resolve(track.Rig.Plugins);
        track.SoundSource = SoundSources.Plugins;
        track.MidiSound = auto.MidiSound;
        return true;
    }

    public static void Apply(PluginSettings settings, SongProject project)
    {
        if (settings.AutoChains.Count == 0) return;
        foreach (var t in project.Tracks) Apply(settings, t);
    }

    private static List<PluginSlot> Clone(List<PluginSlot> plugins)
    {
        var copy = JsonSerializer.Deserialize<List<PluginSlot>>(JsonSerializer.SerializeToUtf8Bytes(plugins)) ?? new();
        foreach (var p in copy) p.Id = Guid.NewGuid().ToString("N");
        return copy;
    }
}
