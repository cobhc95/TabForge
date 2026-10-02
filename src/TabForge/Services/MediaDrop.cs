using System.IO;
using TabForge.Audio;
using TabForge.Models;

namespace TabForge.Services;

public enum DropItemKind { Audio, Midi }

/// <summary>One audio or MIDI file being dragged onto the timeline (measured once when the drag enters).</summary>
public sealed class DropItem
{
    /// <summary>The file on disk: the dragged file itself, or a TabForge copy of a virtual (in-memory) file.</summary>
    public required string Path { get; init; }
    public required string Name { get; init; }
    public DropItemKind Kind { get; init; }
    /// <summary>Audio length (or an absolute-time MIDI file's length) in seconds; 0 = not measured yet.</summary>
    public double Seconds { get; set; }
    public MidiFileData? Midi { get; init; }
    /// <summary>A file that may vanish after the drop (a temp file of a plug-in, or a virtual file): copied next to the song on drop.</summary>
    public bool Transient { get; init; }
    /// <summary>Why the file cannot be added (null = fine).</summary>
    public string? Problem { get; init; }

    /// <summary>A virtual file whose contents the source gives only on drop (a placeholder until then).</summary>
    public bool Deferred { get; init; }

    public bool IsMidi => Kind == DropItemKind.Midi;
}

public sealed record PlannedClip(DropItem Item, double StartSec, double LengthSec);

/// <summary>Where a drop lands: the track (== track count for a new track below the last), the lane, and each file's span.</summary>
public sealed class MediaDropPlan
{
    public int TrackIndex { get; init; } = -1;
    public bool NewTrack { get; init; }
    public TrackKind NewTrackKind { get; init; } = TrackKind.Guitar;
    public int Lane { get; init; }
    /// <summary>The lane does not exist yet (the drop creates it).</summary>
    public bool NewLane { get; init; }
    public double StartSec { get; init; }
    public double EndSec { get; init; }
    public IReadOnlyList<PlannedClip> Clips { get; init; } = Array.Empty<PlannedClip>();
    /// <summary>Why nothing can be dropped here (null when the drop is valid).</summary>
    public string? Problem { get; init; }
    /// <summary>Some file's length is a placeholder until the drop measures it.</summary>
    public bool Estimated { get; init; }

    public bool Valid => Problem is null && Clips.Count > 0;
}

// Owns: planning where dropped audio and MIDI files land on the timeline.
// Does not own: the timeline drawing and the clip edit commands.
// Tests: TestMediaDropPlan, TestClipMoves.
/// <summary>
/// Dropping audio and MIDI files on the timeline: what a drag carries (<see cref="MediaDropSession"/>), where it lands
/// (<see cref="Plan"/>), and which dropped files are kept as copies beside the song (<see cref="IsTransient"/>).
/// </summary>
public static class MediaDrop
{
    /// <summary>
    /// The "Add track" lane below the last track: audio or MIDI files dropped there make an AUDIO track (any clip kind fits it) with the
    /// clips laid end to end from the song start (or <paramref name="startSec"/>).
    /// </summary>
    public static MediaDropPlan PlanAddTrackLane(SongProject project, IReadOnlyList<DropItem> items, SongQuarterMap time, double startSec = 0) =>
        Plan(project, items, project.Tracks.Count, 0, startSec, time, newTrackKind: TrackKind.Audio);

    /// <summary>
    /// Lays the files end to end from <paramref name="startSec"/> on one lane of the track: the hovered lane when the whole span is
    /// free there, otherwise the first free lane (a new one when every lane is busy; an audio track goes multi-lane this way for any clip kind). <paramref name="trackIndex"/> equal to the
    /// track count means "below the last track": a new audio track.
    /// </summary>
    public static MediaDropPlan Plan(SongProject project, IReadOnlyList<DropItem> items, int trackIndex, int preferredLane, double startSec, SongQuarterMap time,
        IReadOnlyCollection<AudioClip>? ignore = null, TrackKind? newTrackKind = null)
    {
        var usable = items.Where(i => i.Problem is null).ToList();
        if (usable.Count == 0)
            return new MediaDropPlan { TrackIndex = trackIndex, StartSec = startSec, Problem = items.FirstOrDefault(i => i.Problem is not null)?.Problem ?? "Not an audio or MIDI file" };
        if (trackIndex < 0 || trackIndex > project.Tracks.Count)
            return new MediaDropPlan { TrackIndex = -1, StartSec = startSec, Problem = "Drop on a track" };
        var newTrack = trackIndex == project.Tracks.Count;
        var track = newTrack ? null : project.Tracks[trackIndex];
        if (track is { IsBus: true })
            return new MediaDropPlan { TrackIndex = trackIndex, StartSec = startSec, Problem = "A bus track holds no clips" };

        startSec = Math.Max(0, startSec);
        var clips = new List<PlannedClip>(usable.Count);
        var at = startSec;
        var estimated = false;
        foreach (var item in usable)
        {
            var length = LengthAt(item, at, time, out var guess);
            estimated |= guess;
            clips.Add(new PlannedClip(item, at, length));
            at += length;
        }
        int lane; bool newLane;
        if (track is null) { lane = 0; newLane = true; }
        else
        {
            var preferred = Math.Max(0, preferredLane);
            var skip = ignore ?? Array.Empty<AudioClip>();   // clips being moved do not block their own destination
            lane = ClipLanes.FreeLane(track, startSec, at, skip, startLane: preferred) == preferred ? preferred : ClipLanes.FreeLane(track, startSec, at, skip);
            newLane = lane >= ClipLanes.Count(track);
        }
                return new MediaDropPlan
        {
            TrackIndex = trackIndex, NewTrack = newTrack, NewTrackKind = newTrackKind ?? TrackKind.Audio, Lane = lane, NewLane = newLane,
            StartSec = startSec, EndSec = at, Clips = clips, Estimated = estimated,
        };
    }

    /// <summary>
    /// Where a clip lands when it is dragged to a lane (the same plan as a dropped file: hovered lane when free, else the first free
    /// lane, a new lane or track when none is). The clip itself never blocks its own destination.
    /// </summary>
    public static MediaDropPlan PlanMove(SongProject project, AudioClip clip, TrackKind sourceKind, int trackIndex, int preferredLane, double startSec, SongQuarterMap time, bool copy = false)
    {
        var item = new DropItem
        {
            Path = clip.File, Name = copy ? clip.Name + " (copy)" : clip.Name, Kind = clip.IsMidi ? DropItemKind.Midi : DropItemKind.Audio,
            Seconds = Math.Max(0.05, clip.LengthSec),
        };
        return Plan(project, new[] { item }, trackIndex, preferredLane, startSec, time, copy ? null : new[] { clip });   // below the last track: an audio track, whatever the clip's kind
    }

    /// <summary>
    /// A MIDI file for a drum track: all its notes on channel 10, or (drum plug-ins often write grooves on channel 1) a name that
    /// says so (drum, groove, beat, kit, fill).
    /// </summary>
    public static bool LooksLikeDrums(DropItem item)
    {
        if (item.Midi is not { } midi) return false;
        if (midi.Notes.Count > 0 && midi.Notes.All(n => n.Channel == 9)) return true;
        var name = item.Name.ToLowerInvariant();
        return new[] { "drum", "groove", "beat", "kit", "fill" }.Any(name.Contains);
    }

    /// <summary>A file's length on the timeline when it starts at <paramref name="at"/> (MIDI follows the song's tempo there).</summary>
    public static double LengthAt(DropItem item, double at, SongQuarterMap time, out bool estimated)
    {
        estimated = false;
        if (item.Midi is { Musical: true } midi) return Math.Max(0.05, time.SecAt(time.QuarterAt(at) + midi.LengthQuarters) - at);
        if (item.Midi is { } smpte) return Math.Max(0.05, smpte.LengthSeconds);
        if (item.Seconds > 0) return item.Seconds;
        estimated = true;
        return Math.Max(0.05, time.BarSecAt(at));
    }

    /// <summary>
    /// Adds dropped clips to one lane of a track with the recording-take rules: when they overlap clips of the same kind (audio or
    /// MIDI), their lane becomes the playing one and lanes holding older clips of that kind grey out; MIDI and audio still mix.
    /// </summary>
    public static void AddClips(TrackModel track, IReadOnlyList<AudioClip> clips, int lane)
    {
        if (clips.Count == 0) return;
        var from = clips.Min(c => c.StartSec);
        var to = clips.Max(c => c.EndSec);
        var overlapsAudio = clips.Any(c => !c.IsMidi) && track.AudioClips.Any(c => !c.IsMidi && c.Overlaps(from, to));
        var overlapsMidi = clips.Any(c => c.IsMidi) && track.AudioClips.Any(c => c.IsMidi && c.Overlaps(from, to));
        ClipLanes.Ensure(track, lane + 1);
        foreach (var clip in clips)
        {
            clip.Lane = lane;
            track.AudioClips.Add(clip);
        }
        if (overlapsAudio) ClipLanes.PlayNewTake(track, lane, midi: false);
        if (overlapsMidi) ClipLanes.PlayNewTake(track, lane, midi: true);
    }

    /// <summary>
    /// Moves (or, with <paramref name="copy"/>, copies) a clip to a lane of <paramref name="to"/> at <paramref name="startSec"/> with the
    /// recording-take rules, and (when <paramref name="removeEmptyLanes"/>) closes the lanes this leaves empty on both tracks.
    /// Returns the clip that now sits there (a new object for a copy).
    /// </summary>
    public static AudioClip ApplyMove(TrackModel from, TrackModel to, AudioClip clip, int lane, double startSec, bool copy, bool removeEmptyLanes)
    {
        var moved = copy ? clip.Clone() : clip;
        if (!copy) from.AudioClips.Remove(clip);
        moved.StartSec = Math.Max(0, startSec);
        AddClips(to, new[] { moved }, lane);
        if (removeEmptyLanes)
        {
            ClipLanes.Compact(from);
            if (!ReferenceEquals(from, to)) ClipLanes.Compact(to);
        }
        return moved;
    }

    /// <summary>Kinds of file a drop can carry.</summary>
    public enum FileRole { Song, Audio, Midi, Unsupported }

    public static FileRole RoleOf(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (FileTypes.IsOpenable(ext)) return FileRole.Song;
        if (MediaPathPolicy.IsAudioExtension(path)) return FileRole.Audio;
        if (MidiFileImport.IsMidiFile(path)) return FileRole.Midi;
        return FileRole.Unsupported;
    }

    /// <summary>
    /// A dropped file that may disappear once the drag ends, so it is copied beside the song: anything in a temp folder (drum
    /// samplers and groove libraries drag a temp .wav or .mid and delete it afterwards) and TabForge's own copies of virtual files.
    /// </summary>
    public static bool IsTransient(string path)
    {
        string full;
        try { full = System.IO.Path.GetFullPath(path); } catch (Exception) { return false; }
        if (MediaPathPolicy.IsInside(full, MediaPathPolicy.Normalize(System.IO.Path.GetTempPath()))) return true;
        if (MediaPathPolicy.IsInside(full, StagingRoot)) return true;
        var folder = System.IO.Path.GetDirectoryName(full) ?? "";
        return folder.Split('\\').Any(s => s.Equals("temp", StringComparison.OrdinalIgnoreCase) || s.Equals("tmp", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Where virtual files are written while a drag is over the timeline (removed after the drop or the next drag).</summary>
    public static string StagingRoot => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TabForge", "Drops");

    /// <summary>Copies a dropped file into the song's media folder under a name that is not taken yet; returns the copy's path.</summary>
    public static string KeepCopy(string source, string mediaFolder)
    {
        Directory.CreateDirectory(mediaFolder);
        var name = SafeFileName(System.IO.Path.GetFileName(source));
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var ext = System.IO.Path.GetExtension(name);
        var target = System.IO.Path.Combine(mediaFolder, name);
        for (var n = 2; File.Exists(target); n++)
        {
            if (n > 9999) throw new IOException("Too many files with that name in the media folder");
            target = System.IO.Path.Combine(mediaFolder, $"{stem} ({n}){ext}");
        }
        File.Copy(source, target, overwrite: false);
        return target;
    }

    /// <summary>A file name without folders, invalid characters, device names or a leading dot; never empty.</summary>
    public static string SafeFileName(string? name)
    {
        name = (name ?? "").Replace('/', '\\');
        name = name[(name.LastIndexOf('\\') + 1)..];
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || c == ':' ? '_' : c).ToArray();
        var clean = new string(chars).Trim().TrimStart('.').TrimEnd('.', ' ');
        if (clean.Length > 120) clean = clean[..80] + clean[^40..];
        var stem = System.IO.Path.GetFileNameWithoutExtension(clean);
        if (stem.Length == 0 || MediaPathPolicy.Classify(System.IO.Path.Combine(@"C:\x", clean.Length == 0 ? "a.wav" : stem + ".wav"), null).Location == MediaLocation.Device)
            clean = "Dropped" + System.IO.Path.GetExtension(clean);
        return clean;
    }
}

/// <summary>Where a song keeps its recordings and kept copies of dropped files.</summary>
public static class MediaFolders
{
    /// <summary>"&lt;song&gt; Media" beside the saved song, else Music\TabForge Recordings.</summary>
    public static string ForSong(string? songPath)
    {
        if (songPath is { Length: > 0 } path && System.IO.Path.GetDirectoryName(path) is { Length: > 0 } folder)
            return System.IO.Path.Combine(folder, $"{System.IO.Path.GetFileNameWithoutExtension(path)} Media");
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "TabForge Recordings");
    }
}
