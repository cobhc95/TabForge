namespace TabForge.Models;

// Owns: an audio clip on a track's audio lane (position, length, source, loop and trim), and the MIDI-note clips and lanes it sits with.
// Does not own: audio playback (AudioEngineClient) and the clip edit commands (ClipEditController, in the app).
// Tests: RepeatTakeLandingTests.

/// <summary>Which take group a clip belongs to: only score-repeat takes are grouped (transport-loop laps are separate clips).</summary>
public enum TakeGroupKind
{
    /// <summary>Legacy value of files written before repeat takes: old loop-record pass groups load with this and are migrated to separate clips.</summary>
    Loop = 0,
    /// <summary>An old stamped repeat take (the T2 build): one clip per play; the load migrates these to one ordinary clip (repeat view). Never written now.</summary>
    Repeat = 1,
}

/// <summary>
/// A piece of audio on a track's audio lane (a recording or a dropped file). Positions are in seconds of song time
/// (the time the song has been playing, repeats included), like an audio track in a DAW. The file itself is not
/// changed: trims, level, pitch and speed are applied when it plays.
/// </summary>
public sealed class AudioClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Full path of the audio file (WAV, MP3, AIFF, FLAC… whatever Windows can decode).</summary>
    public string File { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Where the clip starts on the song timeline, in seconds.</summary>
    public double StartSec { get; set; }
    /// <summary>How much of the file's start is trimmed away, in seconds of the file.</summary>
    public double OffsetSec { get; set; }
    /// <summary>How much of the file plays (after the trim), in seconds of the file.</summary>
    public double SourceLengthSec { get; set; }
    /// <summary>The whole file's length, in seconds (limits how far the edges can be dragged out).</summary>
    public double FileLengthSec { get; set; }
    public double GainDb { get; set; }
    /// <summary>Pitch shift in semitones (speed unchanged).</summary>
    public double Pitch { get; set; }
    /// <summary>Playback speed (1 = original; pitch unchanged).</summary>
    public double Speed { get; set; } = 1;
    public bool Muted { get; set; }
    /// <summary>Fade-in length on the timeline, in seconds (0 = none); a gain ramp from silence at the clip's start.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double FadeInSec { get; set; }
    /// <summary>Fade-out length on the timeline, in seconds (0 = none); a gain ramp to silence at the clip's end.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double FadeOutSec { get; set; }
    /// <summary>Which of the track's clip lanes (under its row) the clip sits on; 0 = the first.</summary>
    public int Lane { get; set; }
    /// <summary>Loop-record pass group this clip belongs to (null = an ordinary clip). Only repeat takes are grouped.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TakeGroupId { get; set; }
    /// <summary>
    /// Which group the clip belongs to: a score-repeat take. Old files carry no field and
    /// load as <see cref="TakeGroupKind.Loop"/>; the loader migrates those to separate clips.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public TakeGroupKind TakeGroupKind { get; set; }
    /// <summary>The part number inside <see cref="TakeGroupId"/> (old stamped repeat takes only: 1 = the first play; 0 = a pre/post span).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int TakeGroupPass { get; set; }
    /// <summary>Which loop recording cut this clip into laps (null = unknown, old files: takes are then grouped by file and name).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LoopRecordingId { get; set; }
    /// <summary>A MIDI clip's notes (times in seconds from the source start); null for an audio clip.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<ClipNote>? Notes { get; set; }

    /// <summary>A MIDI clip's controller points (control changes and pitch bend, times in seconds from the source start); null when it has none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<ClipController>? Controllers { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMidi => Notes is not null;

    /// <summary>Length on the timeline (faster playback makes the clip shorter).</summary>
    public double LengthSec => SourceLengthSec / Math.Clamp(Speed, 0.25, 4);
    public double EndSec => StartSec + LengthSec;

    public AudioClip Clone() => new()
    {
        File = File, Name = Name, StartSec = StartSec, OffsetSec = OffsetSec, SourceLengthSec = SourceLengthSec,
        FileLengthSec = FileLengthSec, GainDb = GainDb, Pitch = Pitch, Speed = Speed, Muted = Muted, Lane = Lane, FadeInSec = FadeInSec, FadeOutSec = FadeOutSec,
        LoopRecordingId = LoopRecordingId, TakeGroupId = TakeGroupId, TakeGroupKind = TakeGroupKind, TakeGroupPass = TakeGroupPass,
        Notes = Notes?.Select(n => n with { }).ToList(),
        Controllers = Controllers?.ToList(),
    };

    /// <summary>True when the clip covers any of [from, to) on the timeline.</summary>
    public bool Overlaps(double from, double to) => StartSec < to && EndSec > from;
}

/// <summary>One note of a MIDI clip: start and length in seconds of the clip's source, pitch, velocity. A muted note stays in the clip (the piano roll
/// draws it hollow) but never plays; old files carry no Muted field and load unmuted, and an unmuted note writes none.
/// <paramref name="Channel"/> is the note's own MIDI channel (0-15), null for the track's channel.</summary>
public sealed record ClipNote(double StartSec, double LengthSec, int Pitch, int Velocity,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Muted = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? Channel = null);

/// <summary>One controller point of a MIDI clip at a time in seconds of the clip's source: a control change (<paramref name="Controller"/> 0-127,
/// value 0-127) or the pitch bend (<see cref="PitchBend"/>, value -8192 to 8191). It holds until the next point of its controller.</summary>
public sealed record ClipController(double StartSec, int Controller, int Value)
{
    public const int PitchBend = 128;
}

/// <summary>A clip lane under a track row. Fixed lanes: only playing lanes are heard, and a new take
/// lane becomes the only playing one (older takes grey out); Ctrl+click on a lane's play button adds lanes.
/// MIDI and audio never replace each other: a new audio take leaves MIDI-only lanes playing, and vice versa.</summary>
public sealed class ClipLane
{
    public bool Plays { get; set; } = true;
}

/// <summary>Lane helpers shared by the timeline, recording and the engine sync.</summary>
public static class ClipLanes
{
    /// <summary>How many lanes the track shows (at least one while armed).</summary>
    public static int Count(TrackModel track)
    {
        var used = track.AudioClips.Count == 0 ? 0 : track.AudioClips.Max(c => c.Lane) + 1;
        return Math.Max(Math.Max(used, track.RecordArm ? 1 : 0), track.AudioClips.Count > 0 || track.RecordArm ? track.Lanes.Count : 0);
    }

    /// <summary>Height in DIPs of one lane band: every lane of a track is this tall.</summary>
    public const double AudioLaneHeight = 54;

    /// <summary>
    /// The lanes a track's row lays out: its <see cref="Count"/>, but none while it has no clips. An armed track with no clips shows its armed
    /// sub-row as its lane, so its row does not grow a lane band for it.
    /// </summary>
    public static int LaidOutCount(TrackModel track) => track.AudioClips.Count == 0 ? 0 : Count(track);

    /// <summary>Height of a track's lane bands together: its laid-out lane count times <see cref="AudioLaneHeight"/>.</summary>
    public static double LanesHeight(TrackModel track) => LaidOutCount(track) * AudioLaneHeight;

    /// <summary>Height of a track's notation band: none for an audio track that has lanes (its lanes are its whole row), else <paramref name="rowHeight"/>.</summary>
    public static double NotationBandHeight(TrackModel track, double rowHeight) => track.IsAudio && LaidOutCount(track) > 0 ? 0 : rowHeight;

    public static bool Plays(TrackModel track, int lane) => lane >= track.Lanes.Count || track.Lanes[lane].Plays;

    /// <summary>Is this clip heard? A repeat take plays every unmuted part on its own pass; an ordinary clip follows Muted, on a playing lane.</summary>
    public static bool Audible(TrackModel track, AudioClip clip) => TakeGroups.Audible(track, clip) && Plays(track, clip.Lane);

    public static void Ensure(TrackModel track, int count)
    {
        while (track.Lanes.Count < count) track.Lanes.Add(new ClipLane());
    }

    /// <summary>First lane with nothing in [from, to) (ignoring <paramref name="except"/>); a new lane when all are busy.</summary>
    public static int FreeLane(TrackModel track, double from, double to, AudioClip? except = null, int startLane = 0)
    {
        for (var lane = startLane; ; lane++)
            if (!track.AudioClips.Any(c => c.Lane == lane && !ReferenceEquals(c, except) && c.Overlaps(from, to))) return lane;
    }

    /// <summary>First lane with nothing in [from, to) besides <paramref name="except"/> and the clips of the same take group: the passes of
    /// one loop recording overlap on purpose. A null <paramref name="takeGroupId"/> ignores no group (the same result as the plain overload).</summary>
    public static int FreeLane(TrackModel track, double from, double to, AudioClip? except, string? takeGroupId, int startLane = 0)
    {
        for (var lane = startLane; ; lane++)
            if (!track.AudioClips.Any(c => c.Lane == lane && !ReferenceEquals(c, except)
                && (takeGroupId is null || c.TakeGroupId != takeGroupId) && c.Overlaps(from, to))) return lane;
    }

    /// <summary>True when both clips carry the same non-null loop-record pass group (an ordinary clip never matches).</summary>
    public static bool SameTakeGroup(AudioClip a, AudioClip b) => a.TakeGroupId is not null && a.TakeGroupId == b.TakeGroupId;

    /// <summary>First lane (from <paramref name="startLane"/>) with nothing of the clips not in <paramref name="ignore"/> in [from, to).</summary>
    public static int FreeLane(TrackModel track, double from, double to, IReadOnlyCollection<AudioClip> ignore, int startLane = 0)
    {
        for (var lane = startLane; ; lane++)
            if (!track.AudioClips.Any(c => c.Lane == lane && !ignore.Contains(c) && c.Overlaps(from, to))) return lane;
    }

    /// <summary>
    /// Removes every lane that holds no clip (middle ones too) and renumbers the clips of the lanes below, keeping each remaining
    /// lane's play / grey state. A track that is armed for recording is never touched. When a removed lane was a playing one and
    /// no lane of a kind (audio or MIDI) plays any more, the last lane holding that kind plays again (nothing goes silent by accident).
    /// Returns whether a lane was removed.
    /// </summary>
    public static bool Compact(TrackModel track)
    {
        if (track.RecordArm) return false;
        var count = Math.Max(track.Lanes.Count, track.AudioClips.Count == 0 ? 0 : track.AudioClips.Max(c => c.Lane) + 1);
        if (count == 0) return false;
        Ensure(track, count);
        var used = new bool[count];
        foreach (var clip in track.AudioClips) if (clip.Lane >= 0) used[clip.Lane] = true;
        if (used.All(u => u)) return false;
        var map = new int[count];
        var kept = new List<ClipLane>(count);
        var removedPlaying = false;
        for (var i = 0; i < count; i++)
        {
            if (used[i]) { map[i] = kept.Count; kept.Add(track.Lanes[i]); }
            else removedPlaying |= track.Lanes[i].Plays;
        }
        foreach (var clip in track.AudioClips) if (clip.Lane >= 0) clip.Lane = map[clip.Lane];
        track.Lanes.Clear();
        track.Lanes.AddRange(kept);
        if (removedPlaying)
            foreach (var midi in new[] { false, true })
            {
                var holding = Enumerable.Range(0, kept.Count).Where(l => track.AudioClips.Any(c => c.Lane == l && c.IsMidi == midi)).ToList();
                if (holding.Count > 0 && !holding.Any(l => kept[l].Plays)) kept[holding[^1]].Plays = true;
            }
        return true;
    }

    /// <summary>A new take of one kind (audio or MIDI) on <paramref name="lane"/>: it plays, other lanes holding that
    /// kind grey out, lanes holding only the other kind keep playing (MIDI and audio mix).</summary>
    public static void PlayNewTake(TrackModel track, int lane, bool midi)
    {
        Ensure(track, lane + 1);
        for (var i = 0; i < track.Lanes.Count; i++)
        {
            if (i == lane) { track.Lanes[i].Plays = true; continue; }
            if (track.AudioClips.Any(c => c.Lane == i && c.IsMidi == midi)) track.Lanes[i].Plays = false;
        }
    }

    /// <summary>
    /// Fixed lanes: clicking a take makes its lane the one that plays (Ctrl+click adds or removes it). Only lanes holding
    /// the same kind of clip (audio or MIDI) are silenced, so a MIDI take and an audio take keep playing together.
    /// Returns whether anything changed; <paramref name="apply"/> false only asks.
    /// </summary>
    public static bool SelectTake(TrackModel track, int lane, bool midi, bool add, bool apply = true)
    {
        var count = Count(track);
        if (count < 2 || lane < 0 || lane >= count) return false;
        if (apply) Ensure(track, count);
        var next = Enumerable.Range(0, count).Select(i => i < track.Lanes.Count ? track.Lanes[i].Plays : true).ToArray();
        if (add) next[lane] = !next[lane];
        else
            for (var i = 0; i < next.Length; i++)
            {
                if (i == lane) next[i] = true;
                else if (track.AudioClips.Any(c => c.Lane == i && c.IsMidi == midi)) next[i] = false;
            }
        var changed = false;
        for (var i = 0; i < next.Length; i++) changed |= next[i] != (i >= track.Lanes.Count || track.Lanes[i].Plays);
        if (changed && apply) for (var i = 0; i < next.Length; i++) track.Lanes[i].Plays = next[i];
        return changed;
    }

    /// <summary>Lane play button: plain click plays only this lane (of those with clips), Ctrl+click toggles it.</summary>
    public static void ClickPlay(TrackModel track, int lane, bool add)
    {
        Ensure(track, lane + 1);
        if (add) { track.Lanes[lane].Plays = !track.Lanes[lane].Plays; return; }
        for (var i = 0; i < track.Lanes.Count; i++) track.Lanes[i].Plays = i == lane;
    }

    /// <summary>Removes empty lanes at the bottom (keeps one while armed).</summary>
    public static void Trim(TrackModel track)
    {
        var used = track.AudioClips.Count == 0 ? (track.RecordArm ? 1 : 0) : track.AudioClips.Max(c => c.Lane) + 1;
        while (track.Lanes.Count > Math.Max(used, track.RecordArm ? 1 : 0)) track.Lanes.RemoveAt(track.Lanes.Count - 1);
    }
}
