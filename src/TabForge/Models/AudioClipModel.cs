namespace TabForge.Models;

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
    /// <summary>Which of the track's clip lanes (under its row) the clip sits on; 0 = the first.</summary>
    public int Lane { get; set; }
    /// <summary>A MIDI clip's notes (times in seconds from the source start); null for an audio clip.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<ClipNote>? Notes { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMidi => Notes is not null;

    /// <summary>Length on the timeline (faster playback makes the clip shorter).</summary>
    public double LengthSec => SourceLengthSec / Math.Clamp(Speed, 0.25, 4);
    public double EndSec => StartSec + LengthSec;

    public AudioClip Clone() => new()
    {
        File = File, Name = Name, StartSec = StartSec, OffsetSec = OffsetSec, SourceLengthSec = SourceLengthSec,
        FileLengthSec = FileLengthSec, GainDb = GainDb, Pitch = Pitch, Speed = Speed, Muted = Muted, Lane = Lane,
        Notes = Notes?.Select(n => n with { }).ToList(),
    };

    /// <summary>True when the clip covers any of [from, to) on the timeline.</summary>
    public bool Overlaps(double from, double to) => StartSec < to && EndSec > from;
}

/// <summary>One note of a MIDI clip: start and length in seconds of the clip's source, pitch, velocity.</summary>
public sealed record ClipNote(double StartSec, double LengthSec, int Pitch, int Velocity);

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

    public static bool Plays(TrackModel track, int lane) => lane >= track.Lanes.Count || track.Lanes[lane].Plays;

    /// <summary>Is this clip heard? Not muted, on a playing lane.</summary>
    public static bool Audible(TrackModel track, AudioClip clip) => !clip.Muted && Plays(track, clip.Lane);

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

    /// <summary>A lane just recorded becomes the only playing lane (older takes are greyed out).</summary>
    public static void PlayOnly(TrackModel track, int lane)
    {
        Ensure(track, lane + 1);
        for (var i = 0; i < track.Lanes.Count; i++) track.Lanes[i].Plays = i == lane;
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
        Ensure(track, count);
        var next = track.Lanes.Select(l => l.Plays).ToArray();
        if (add) next[lane] = !next[lane];
        else
            for (var i = 0; i < next.Length; i++)
            {
                if (i == lane) next[i] = true;
                else if (track.AudioClips.Any(c => c.Lane == i && c.IsMidi == midi)) next[i] = false;
            }
        var changed = false;
        for (var i = 0; i < next.Length; i++) changed |= next[i] != track.Lanes[i].Plays;
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

/// <summary>Which input channels a track records / monitors.</summary>
public static class AudioInputs
{
    public const string Input1 = "Input 1";
    public const string Input2 = "Input 2";
    public const string Stereo = "Inputs 1+2 (stereo)";
    /// <summary>MIDI from every MIDI input device (recorded as a MIDI clip, played live through the track).</summary>
    public const string Midi = "MIDI (all inputs)";
    public static readonly string[] All = { Input1, Input2, Stereo, Midi };
    /// <summary>Audio inputs only (the engine's input modes, in order).</summary>
    public static readonly string[] Audio = { Input1, Input2, Stereo };
    public static bool IsMidi(string input) => input == Midi;
}
