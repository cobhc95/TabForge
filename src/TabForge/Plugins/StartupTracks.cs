using System.Text.Json;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>A chain kept as a startup track template (see <see cref="StartupTracks"/>); property names are the settings file format.</summary>
public sealed class StartupTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>The track it was saved from: a song that already holds that track is not given a second copy.</summary>
    public string SourceTrackId { get; set; } = "";
    public string Name { get; set; } = "Live guitar";
    public TrackKind Kind { get; set; } = TrackKind.Guitar;
    public string InstrumentName { get; set; } = "Electric Guitar";
    public int MidiProgram { get; set; } = 29;
    public int Volume { get; set; } = 100;
    public int Pan { get; set; } = 64;
    public string SoundSource { get; set; } = SoundSources.Plugins;
    public bool MidiSound { get; set; } = true;
    public string AudioInput { get; set; } = AudioInputs.Input1;
    public bool MonitorInput { get; set; } = true;
    public string ColorHex { get; set; } = "#F61A16";
    public List<PluginSlot> Plugins { get; set; } = new();
}

/// <summary>
/// "Add as a track on startup" (FX window): a chain (plug-ins with their states, wiring and MIDI configuration, volume,
/// audio input and monitor setting) is remembered in the app settings and added to every song that is opened or created,
/// never armed. The added track is tagged (<see cref="TrackModel.StartupTemplateId"/>), does not make the song dirty and is
/// left out of the saved file until the user unticks the option (it then becomes an ordinary track).
/// </summary>
public static class StartupTracks
{
    public static StartupTrack? Find(PluginSettings settings, TrackModel track) =>
        settings.StartupTracks.FirstOrDefault(t => (track.StartupTemplateId is not null && t.Id == track.StartupTemplateId) || t.SourceTrackId == track.Id.ToString("N"));

    public static bool Has(PluginSettings settings, TrackModel track) => Find(settings, track) is not null;

    public static void Save(PluginSettings settings, TrackModel track)
    {
        var existing = Find(settings, track);
        var id = existing?.Id ?? track.StartupTemplateId ?? Guid.NewGuid().ToString("N");
        settings.StartupTracks.RemoveAll(t => t.Id == id);
        settings.StartupTracks.Add(new StartupTrack
        {
            Id = id, SourceTrackId = existing?.SourceTrackId ?? track.Id.ToString("N"), Name = track.Name, Kind = track.Kind,
            InstrumentName = track.InstrumentName, MidiProgram = track.MidiProgram, Volume = track.Volume, Pan = track.Pan,
            SoundSource = track.SoundSource, MidiSound = track.MidiSound, AudioInput = track.AudioInput,
            MonitorInput = track.MonitorInput, ColorHex = track.ColorHex, Plugins = Clone(track.Rig.Plugins, keepIds: true)
        });
        ChainStateStore.Externalise(settings.StartupTracks[^1].Plugins);   // states go to their own files, not into settings.json
    }

    /// <summary>Removes the template; a tagged track becomes an ordinary one (it is saved with the song from now on).</summary>
    public static void Remove(PluginSettings settings, TrackModel track)
    {
        if (Find(settings, track) is { } t) settings.StartupTracks.RemoveAll(x => x.Id == t.Id);
        track.StartupTemplateId = null;
    }

    /// <summary>Adds every template's track that the song does not have yet. True when a track was added. Does not touch IsDirty.</summary>
    public static bool Apply(PluginSettings settings, SongProject project)
    {
        var added = false;
        foreach (var t in settings.StartupTracks.ToList())
        {
            if (project.Tracks.Any(x => x.StartupTemplateId == t.Id || x.Id.ToString("N") == t.SourceTrackId)) continue;
            var measures = Math.Max(32, project.Tracks.FirstOrDefault()?.Measures.Count ?? 32);
            var used = project.Tracks.Select(x => x.MidiChannel).ToHashSet();
            var channel = Enumerable.Range(0, 16).FirstOrDefault(c => c != 9 && !used.Contains(c), 0);
            project.Tracks.Add(new TrackModel
            {
                Name = t.Name, Kind = t.Kind, InstrumentName = t.InstrumentName, MidiProgram = t.MidiProgram, MidiChannel = t.Kind == TrackKind.Drums ? 9 : channel,
                Volume = t.Volume, Pan = t.Pan, SoundSource = t.SoundSource, MidiSound = t.MidiSound, AudioInput = t.AudioInput,
                MonitorInput = t.MonitorInput, ColorHex = t.ColorHex, RecordArm = false, StartupTemplateId = t.Id,
                Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = t.Name, Plugins = Clone(t.Plugins, keepIds: false) }
            });
            ChainStateStore.Resolve(project.Tracks[^1].Rig.Plugins);
            added = true;
        }
        return added;
    }

    private static List<PluginSlot> Clone(List<PluginSlot> plugins, bool keepIds)
    {
        var copy = JsonSerializer.Deserialize<List<PluginSlot>>(JsonSerializer.SerializeToUtf8Bytes(plugins)) ?? new();
        if (!keepIds) foreach (var p in copy) p.Id = Guid.NewGuid().ToString("N");
        return copy;
    }
}
