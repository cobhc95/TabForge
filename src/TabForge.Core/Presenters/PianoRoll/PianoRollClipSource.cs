using TabForge.Models;

namespace TabForge.Presenters;

// Owns: a MIDI track's notes in the piano roll: the notes of its MIDI clips on one chosen lane (every clip on it) or on the playing lanes (every MIDI clip when none plays), placed on
//       the performed clock as the clips play them (start, trim and speed applied, the clip's pitch shift added), and the write back: each note
//       goes to the shown clip that holds its start (else the nearest one, which grows to hold it; a track with no MIDI clip gets a new one on a
//       free lane), with the notes outside a clip's trimmed part kept as they were.
// Does not own: the clip model and lanes (AudioClip, ClipLanes), the song growth and engine sync after the edit (the app's clip refresh).
// Tests: TabForge.Avalonia.Tests.PianoRollSourceTests.
public sealed class PianoRollClipSource : IPianoRollSource
{
    private const double Epsilon = 1e-9;

    private readonly int? _lane;
    private readonly bool _strict;

    /// <param name="lane">The clip lane to show and edit (every MIDI clip on it, however many); null: the playing lanes, as before.</param>
    /// <param name="strict">The lane alone, even with no take on it (an empty take lane): nothing from other lanes shows, and the first note written creates a one-bar take on it.</param>
    public PianoRollClipSource(int? lane = null, bool strict = false)
    {
        _lane = lane;
        _strict = strict;
    }

    public bool IsTab => false;

    public PianoRollTimeMap Map(SongProject song) => PianoRollTimeMap.Performed(song);

    /// <summary>The MIDI clips this source shows: every MIDI clip on its lane, else (no lane, or none left on it) those on playing lanes, or every MIDI clip when none plays.</summary>
    public List<AudioClip> ShownClips(TrackModel track) => Shown(track, _lane, _strict);

    /// <summary>The MIDI clips the piano roll shows: those on <paramref name="lane"/> when given and it holds some, else those on playing lanes, or every MIDI clip when none plays.</summary>
    public static List<AudioClip> Shown(TrackModel track, int? lane = null, bool strict = false)
    {
        var midi = track.AudioClips.Where(clip => clip.IsMidi).ToList();
        if (strict && lane is { } only) return midi.Where(clip => clip.Lane == only).ToList();
        if (lane is { } wanted && midi.Any(clip => clip.Lane == wanted)) return midi.Where(clip => clip.Lane == wanted).ToList();
        var playing = midi.Where(clip => ClipLanes.Plays(track, clip.Lane)).ToList();
        return playing.Count > 0 ? playing : midi;
    }

    internal static double Speed(AudioClip clip) => Math.Clamp(clip.Speed, 0.25, 4);

    private static int Shift(AudioClip clip) => (int)Math.Round(clip.Pitch);

    private static bool InWindow(AudioClip clip, ClipNote note) =>
        note.StartSec >= clip.OffsetSec - 1e-6 && note.StartSec < clip.OffsetSec + clip.SourceLengthSec;

    public List<PianoRollNote> Read(SongProject song, TrackModel track, PianoRollTimeMap map)
    {
        var list = new List<PianoRollNote>();
        foreach (var clip in ShownClips(track)) list.AddRange(ReadClip(clip, map));
        return PianoRollSources.Sorted(list);
    }

    /// <summary>One clip's notes inside its trimmed part, on <paramref name="map"/>'s clock.</summary>
    internal static IEnumerable<PianoRollNote> ReadClip(AudioClip clip, PianoRollTimeMap map)
    {
        foreach (var note in clip.Notes ?? new List<ClipNote>())
        {
            if (!InWindow(clip, note)) continue;
            var startSec = clip.StartSec + (note.StartSec - clip.OffsetSec) / Speed(clip);
            var start = map.BeatsAt(startSec);
            var end = map.BeatsAt(startSec + note.LengthSec / Speed(clip));
            yield return new PianoRollNote(start, Math.Max(PianoRollEdits.MinLength, end - start), PianoRollEdits.ClampPitch(note.Pitch + Shift(clip)),
                PianoRollEdits.ClampVelocity(note.Velocity), note.Muted, 0, note.Channel ?? -1);
        }
    }

    public bool Write(SongProject song, TrackModel track, PianoRollTimeMap map, IReadOnlyList<PianoRollNote> after)
    {
        var timed = after.Select(n => (Note: n, From: map.SecondsAt(n.Start), To: map.SecondsAt(n.End))).ToList();
        var clips = ShownClips(track);
        if (clips.Count == 0)
        {
            if (timed.Count == 0) return false;
            if (_strict && _lane is { } lane)
            {
                var bar = map.BarAtBeat(after.Min(n => n.Start));   // an empty take lane: the take is the bar of the first note, grown below to hold the rest
                clips.Add(NewClip(track, map.SecondsAt(bar.StartBeat), map.SecondsAt(bar.EndBeat), lane));
            }
            else clips.Add(NewClip(track, timed.Min(t => t.From), timed.Max(t => t.To)));
        }
        var assigned = clips.ToDictionary(clip => clip, _ => new List<(PianoRollNote Note, double From, double To)>());
        foreach (var t in timed) assigned[Owner(clips, t.From)].Add(t);
        var changed = false;
        foreach (var clip in clips)
        {
            var mine = assigned[clip];
            var hidden = (clip.Notes ?? new List<ClipNote>()).Where(note => !InWindow(clip, note)).ToList();
            var front = mine.Count == 0 ? 0 : Cover(clip, mine.Min(t => t.From), mine.Max(t => t.To));
            if (front > 0 && clip.Controllers is { } points) clip.Controllers = points.Select(p => p with { StartSec = p.StartSec + front }).ToList();
            var notes = hidden.Select(note => note with { StartSec = note.StartSec + front }).ToList();
            foreach (var (note, from, to) in mine)
                notes.Add(new ClipNote(clip.OffsetSec + (from - clip.StartSec) * Speed(clip), Math.Max(0.001, (to - from) * Speed(clip)),
                    PianoRollEdits.ClampPitch(note.Pitch - Shift(clip)), PianoRollEdits.ClampVelocity(note.Velocity), note.Muted, note.Channel is >= 0 and <= 15 ? note.Channel : null));
            notes = notes.OrderBy(note => note.StartSec).ThenBy(note => note.Pitch).ToList();
            if (front > 0 || clip.Notes is null || !clip.Notes.SequenceEqual(notes, NoteComparer.Instance)) changed = true;
            clip.Notes = notes;
        }
        return changed;
    }

    /// <summary>The shown clip that holds <paramref name="sec"/> (the first such on the lowest lane), else the nearest one.</summary>
    internal static AudioClip Owner(List<AudioClip> clips, double sec)
    {
        var holding = clips.Where(clip => sec >= clip.StartSec - Epsilon && sec < clip.EndSec).OrderBy(clip => clip.Lane).FirstOrDefault();
        return holding ?? clips.OrderBy(clip => sec < clip.StartSec ? clip.StartSec - sec : sec - clip.EndSec).First();
    }

    /// <summary>Grows <paramref name="clip"/> so it holds <paramref name="from"/> to <paramref name="to"/> (song seconds); returns how many source seconds
    /// were added in front of the clip's data (its notes move later by that much), 0 when the trimmed start could be uncovered instead.</summary>
    internal static double Cover(AudioClip clip, double from, double to)
    {
        var speed = Speed(clip);
        var front = 0.0;
        if (from < clip.StartSec - Epsilon)
        {
            var source = (clip.StartSec - from) * speed;
            clip.StartSec = from;
            if (clip.OffsetSec >= source) clip.OffsetSec -= source;
            else
            {
                front = source - clip.OffsetSec;
                clip.OffsetSec = 0;
            }
            clip.SourceLengthSec += source;
        }
        if (to > clip.EndSec + Epsilon) clip.SourceLengthSec += (to - clip.EndSec) * speed;
        clip.FileLengthSec = Math.Max(clip.FileLengthSec + front, clip.OffsetSec + clip.SourceLengthSec);
        return front;
    }

    /// <summary>A new, empty MIDI clip on <paramref name="onLane"/>, else the track's first free lane, over <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal static AudioClip NewClip(TrackModel track, double from, double to, int? onLane = null)
    {
        var lane = onLane ?? ClipLanes.FreeLane(track, from, to);
        ClipLanes.Ensure(track, lane + 1);
        var clip = new AudioClip { Name = track.Name + " MIDI", Notes = new List<ClipNote>(), StartSec = from, SourceLengthSec = Math.Max(0.001, to - from),
            FileLengthSec = Math.Max(0.001, to - from), Lane = lane };
        track.AudioClips.Add(clip);
        return clip;
    }

    // Notes the same within a microsecond count as unchanged (a read and write round trip goes through beats and back).
    private sealed class NoteComparer : IEqualityComparer<ClipNote>
    {
        public static readonly NoteComparer Instance = new();

        public bool Equals(ClipNote? a, ClipNote? b) => a is not null && b is not null && Math.Abs(a.StartSec - b.StartSec) < 1e-6
            && Math.Abs(a.LengthSec - b.LengthSec) < 1e-6 && a.Pitch == b.Pitch && a.Velocity == b.Velocity && a.Muted == b.Muted && a.Channel == b.Channel;

        public int GetHashCode(ClipNote note) => note.Pitch;
    }
}
