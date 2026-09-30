using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Services;

/// <summary>
/// The TabForge audio data of a song (mixer groups, each track's sound source, mixer group and FX chain) in a
/// small file beside a clean Guitar Pro file: "song.gp" + "song.tfaudio". The .gp stays exactly what Guitar Pro
/// expects; TabForge re-applies this file when it opens the .gp, so edits made later in Guitar Pro are kept.
/// Untrusted input: bounded read, validated like a project.
/// </summary>
public static class AudioDataFile
{
    public const string Extension = ".tfaudio";
    private const long MaxBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 32 };

    public sealed class TrackAudio
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        /// <summary>The track's stable id (TrackModel.Id, "N") when saved: routing links inside rigs refer to it. "" in older files.</summary>
        public string Id { get; set; } = "";
        public string SoundSource { get; set; } = SoundSources.Midi;
        public bool MidiSound { get; set; } = true;
        public string? MixerGroup { get; set; }
        /// <summary>The track's own volume / pan (0-127), before any group offset. The .gp beside it holds these with the group baked in
        /// (and rounded to the standard 0..16 scale), so they are restored from here. Null in FormatVersion 1 files.</summary>
        public int? Volume { get; set; }
        public int? Pan { get; set; }
        public RigPreset Rig { get; set; } = new();
        public List<AudioClip> AudioClips { get; set; } = new();
        public List<ClipLane> Lanes { get; set; } = new();
    }

    public sealed class Contents
    {
        /// <summary>1: no per-track volume/pan (the mixer group is baked into the .gp's track volume/pan and is un-baked on apply).
        /// 2: each track carries its own volume/pan, restored exactly.</summary>
        public int FormatVersion { get; set; } = 1;
        /// <summary>SHA-256 (hex) of the .gp bytes this file was saved with; "" in older files.</summary>
        public string GpSha256 { get; set; } = "";
        public MixerSettings Mixer { get; set; } = new();
        public List<TrackAudio> Tracks { get; set; } = new();
    }

    public static string PathFor(string gpPath) => Path.ChangeExtension(gpPath, Extension);

    /// <summary>The file's bytes for <paramref name="project"/>, paired with the .gp whose bytes hash to <paramref name="gpSha256"/>.</summary>
    public static byte[] Serialize(SongProject project, string gpSha256 = "")
    {
        var contents = new Contents
        {
            FormatVersion = 2,
            GpSha256 = gpSha256,
            Mixer = project.Mixer,
            Tracks = project.Tracks.Select((t, i) => new TrackAudio { Index = i, Name = t.Name, Id = t.Id.ToString("N"), Volume = t.Volume, Pan = t.Pan, SoundSource = t.SoundSource, MidiSound = t.MidiSound, MixerGroup = t.MixerGroup, Rig = t.Rig, AudioClips = t.AudioClips, Lanes = t.Lanes }).ToList()
        };
        return JsonSerializer.SerializeToUtf8Bytes(contents, Options);
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static void Save(SongProject project, string path)
    {
        var bytes = Serialize(project);
        FilePathPolicy.WriteAtomically(FilePathPolicy.OutputFile(path, "TabForge audio data", Extension), stream => stream.Write(bytes));
    }

    /// <summary>
    /// Applies "song.tfaudio" beside a .gp when there is one. Tracks match by position and name, else by name. Routing links inside the
    /// rigs (sidechain, MIDI forward, MIDI input from another track) are re-pointed from the saved track ids to the matched tracks; links
    /// that cannot be resolved are cleared. Anything the user should know (a .gp changed since, unresolved or ambiguous links) is added to
    /// <paramref name="notices"/>.
    /// </summary>
    public static bool TryApply(SongProject project, string gpPath, List<string>? notices = null)
    {
        var path = PathFor(gpPath);
        if (!File.Exists(path)) return false;
        Contents? contents;
        try
        {
            var bytes = InputLimits.ReadBoundedBytes(path, MaxBytes, "TabForge audio data");
            contents = JsonSerializer.Deserialize<Contents>(bytes, Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return false; }
        if (contents?.Tracks is null || contents.Mixer is null) return false;

        var found = new List<string>();
        if (contents.GpSha256 is { Length: > 0 } expected)
        {
            string? actual = null;
            try { actual = Sha256Hex(InputLimits.ReadBoundedBytes(gpPath, InputLimits.MaxGuitarProFileBytes, "Guitar Pro file")); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                found.Add($"{Path.GetFileName(path)} was saved with a different version of {Path.GetFileName(gpPath)}; its mixer and FX were applied by track position and name — check them");
        }

        // Work on a copy: nothing changes unless the result validates.
        var backup = (project.Mixer, project.Tracks.Select(t => (t.SoundSource, t.MixerGroup, t.Rig, t.MidiSound, t.AudioClips, t.Lanes)).ToList());
        var volumePanBackup = project.Tracks.Select(t => (t.Volume, t.Pan)).ToList();
        var matched = new List<(TrackModel Track, TrackAudio Saved)>();
        project.Mixer = contents.Mixer;
        var used = new HashSet<TrackModel>();
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // saved track id -> matched track id
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var saved in contents.Tracks.Take(InputLimits.MaxTracks))
        {
            if (saved is null) continue;
            // By stored id (a track that kept it), else position + name, else the first unused track of that name.
            var track = saved.Id is { Length: > 0 } sid ? project.Tracks.FirstOrDefault(t => !used.Contains(t) && t.Id.ToString("N").Equals(sid, StringComparison.OrdinalIgnoreCase)) : null;
            track ??= saved.Index >= 0 && saved.Index < project.Tracks.Count && project.Tracks[saved.Index].Name == saved.Name && !used.Contains(project.Tracks[saved.Index])
                ? project.Tracks[saved.Index]
                : null;
            if (track is null)
            {
                var candidates = project.Tracks.Where(t => t.Name == saved.Name && !used.Contains(t)).ToList();
                track = candidates.FirstOrDefault();
                if (candidates.Count > 1 && saved.Id is { Length: > 0 }) ambiguous.Add(saved.Id);
            }
            if (track is null || !used.Add(track)) continue;
            if (saved.Id is { Length: > 0 } oldId) idMap[oldId] = track.Id.ToString("N");
            track.SoundSource = saved.SoundSource;
            track.MidiSound = saved.MidiSound;
            track.MixerGroup = saved.MixerGroup;
            track.Rig = saved.Rig ?? new RigPreset();
            track.AudioClips = saved.AudioClips ?? new List<AudioClip>();
            track.Lanes = saved.Lanes ?? new List<ClipLane>();
            matched.Add((track, saved));
        }
        RestoreVolumePan(project, matched, contents.FormatVersion);
        RelinkRouting(project, idMap, ambiguous, found);
        try
        {
            ProjectValidator.Validate(project);
            notices?.AddRange(found);
            return true;
        }
        catch (InvalidDataException)
        {
            project.Mixer = backup.Mixer;
            for (var i = 0; i < project.Tracks.Count; i++)
                (project.Tracks[i].SoundSource, project.Tracks[i].MixerGroup, project.Tracks[i].Rig, project.Tracks[i].MidiSound, project.Tracks[i].AudioClips, project.Tracks[i].Lanes) = backup.Item2[i];
            for (var i = 0; i < project.Tracks.Count; i++) (project.Tracks[i].Volume, project.Tracks[i].Pan) = volumePanBackup[i];
            return false;
        }
    }

    /// <summary>
    /// The .gp written beside a .tfaudio has the mixer group's level and pan (and the master pan) baked into each track's volume/balance,
    /// while the .tfaudio restores the group itself: taking the .gp values as the track's own would apply the group twice
    /// (track 100 in a 110% group would sound at 110 before saving and 122 after reopening). FormatVersion 2 stores each track's own
    /// volume/pan and they are restored exactly; FormatVersion 1 files (written before that) only have the baked values, so the group is
    /// divided / subtracted back out (approximate: the standard 0..16 scale and clamping at 0/127 lose precision, and a 0% group cannot be undone).
    /// </summary>
    private static void RestoreVolumePan(SongProject project, List<(TrackModel Track, TrackAudio Saved)> matched, int formatVersion)
    {
        foreach (var (track, saved) in matched)
        {
            if (saved.Volume is int volume && saved.Pan is int pan && formatVersion >= 2)
            {
                track.Volume = Math.Clamp(volume, 0, 127);
                track.Pan = Math.Clamp(pan, 0, 127);
                continue;
            }
            var levels = MixerGroups.LevelsFor(project, track);
            if (levels.Volume > 0 && levels.Volume != 100) track.Volume = Math.Clamp((int)Math.Round(track.Volume * 100.0 / levels.Volume), 0, 127);
            var offset = levels.Pan + project.Mixer.MasterPan;
            if (offset != 0) track.Pan = Math.Clamp(track.Pan - offset, 0, 127);
        }
    }

    /// <summary>
    /// Rewrites every track link inside every rig (tracks, group buses, master) from saved ids to the current tracks. A link whose target
    /// is not a current track after mapping is cleared and reported.
    /// </summary>
    internal static void RelinkRouting(SongProject project, IReadOnlyDictionary<string, string> idMap, IReadOnlySet<string> ambiguous, List<string> notices)
    {
        var current = project.Tracks.Select(t => t.Id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<string>();
        var guessed = new List<string>();
        string Map(string id, string owner, string what)
        {
            if (string.IsNullOrEmpty(id)) return id;
            if (idMap.TryGetValue(id, out var mapped))
            {
                if (ambiguous.Contains(id)) guessed.Add($"{owner}: {what}");
                return mapped;
            }
            if (current.Contains(id)) return id;
            unresolved.Add($"{owner}: {what}");
            return "";
        }
        void Rig(RigPreset? rig, string owner)
        {
            if (rig?.Plugins is null) return;
            foreach (var p in rig.Plugins)
            {
                if (p is null) continue;
                p.SidechainTrackId = Map(p.SidechainTrackId ?? "", owner, "sidechain");
                p.MidiOutTrackId = Map(p.MidiOutTrackId ?? "", owner, "MIDI forward");
                if (p.MidiIn is { } midiIn && midiIn.Source == PluginMidiIn.OtherTrack)
                {
                    midiIn.TrackId = Map(midiIn.TrackId ?? "", owner, "MIDI input");
                    if (midiIn.TrackId.Length == 0) midiIn.Source = PluginMidiIn.None;   // never silently fall back to the track's own MIDI
                }
            }
        }
        foreach (var t in project.Tracks) Rig(t.Rig, t.Name);
        if (project.Mixer is { } mixer)
        {
            if (mixer.Buses is not null) foreach (var (group, bus) in mixer.Buses) Rig(bus?.Rig, $"{group} bus");
            Rig(mixer.Master?.Rig, "Master");
            Rig(mixer.MonitorFx?.Rig, "Monitor");
        }
        if (unresolved.Count > 0) notices.Add($"{unresolved.Count} routing link{(unresolved.Count == 1 ? "" : "s")} could not be matched to a track and {(unresolved.Count == 1 ? "was" : "were")} cleared ({string.Join(", ", unresolved.Distinct().Take(4))})");
        if (guessed.Count > 0) notices.Add($"Several tracks share a name, so {guessed.Count} routing link{(guessed.Count == 1 ? "" : "s")} {(guessed.Count == 1 ? "was" : "were")} matched to the first track of that name ({string.Join(", ", guessed.Distinct().Take(4))}) — check them");
    }
}
