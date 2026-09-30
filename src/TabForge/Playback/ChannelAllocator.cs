using TabForge.Models;

namespace TabForge.Playback;

/// <summary>
/// Assigns the MIDI channel each track actually plays on.
/// Drum tracks always use channel 9 (GM percussion). Melodic tracks keep the channel stored in the
/// score when it is free, so mixer choices are respected; a conflict is resolved by handing the
/// track the next free melodic channel. Without this, two tracks sharing channel 0 fight over the
/// program and cross each other's note-offs.
/// </summary>
public static class ChannelAllocator
{
    public const int PercussionChannel = 9;
    public const int ChannelCount = 16;

    public static int[] Assign(SongProject project)
    {
        var result = new int[project.Tracks.Count];
        var used = new bool[ChannelCount];

        // Pass 1: percussion always wins channel 9.
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            if (project.Tracks[i].Kind == TrackKind.Drums) { result[i] = PercussionChannel; used[PercussionChannel] = true; }
            else result[i] = -1;
        }

        // Pass 2: honour stored channels where they are free and not percussion.
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var track = project.Tracks[i];
            if (result[i] >= 0) continue;
            var preferred = track.MidiChannel;
            if (preferred is >= 0 and < ChannelCount && preferred != PercussionChannel && !used[preferred])
            {
                result[i] = preferred;
                used[preferred] = true;
            }
        }

        // Pass 3: anything still unassigned (or conflicted) gets the next free melodic channel.
        var next = 0;
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            if (result[i] >= 0) continue;
            while (next < ChannelCount && (used[next] || next == PercussionChannel)) next++;
            if (next >= ChannelCount) next = FirstMelodic(next: 0, used);   // >15 melodic tracks: reuse
            result[i] = next;
            used[next] = true;
            next++;
        }
        return result;
    }

    /// <summary>
    /// Bent and whammied notes play on the track's second ("effect") channel so a bend never detunes the other notes
    /// ringing on the track. Same rule the compiler uses to flag <see cref="NoteEvent.UsesEffectChannel"/>.
    /// </summary>
    public static bool UsesEffectChannel(TabNote note, TabCell cell)
    {
        var t = note.Techniques;
        return note.BendPoints.Count > 0 || t.Contains("Bend") || t.Contains("TremBar") || t.Contains("TremBarWide") || cell.WhammyPoints.Count > 0;
    }

    /// <summary>True when any note of the track is bent or whammied (the track then needs an effect channel).</summary>
    public static bool HasEffectNotes(TrackModel track)
    {
        foreach (var measure in track.Measures)
        {
            foreach (var cell in measure.Cells)
                foreach (var note in cell.Notes) if (UsesEffectChannel(note, cell)) return true;
            foreach (var cell in measure.Voice2Cells)
                foreach (var note in cell.Notes) if (UsesEffectChannel(note, cell)) return true;
        }
        return false;
    }

    /// <summary>
    /// The effect channel of each track (-1: none). Only melodic tracks with bent / whammied notes get one, taken from the channels no
    /// track uses (never percussion). With none left the track keeps bending on its own channel, as before. Deterministic, so playback,
    /// MIDI export, the engine routing and the mixer all agree.
    /// </summary>
    public static int[] AssignEffect(SongProject project, int[] main)
    {
        var result = new int[project.Tracks.Count];
        Array.Fill(result, -1);
        var used = new bool[ChannelCount];
        used[PercussionChannel] = true;
        foreach (var c in main) if (c is >= 0 and < ChannelCount) used[c] = true;
        var next = 0;
        for (var i = 0; i < project.Tracks.Count && i < main.Length; i++)
        {
            var track = project.Tracks[i];
            if (track.Kind == TrackKind.Drums || main[i] == PercussionChannel || !HasEffectNotes(track)) continue;
            while (next < ChannelCount && used[next]) next++;
            if (next >= ChannelCount) break;
            result[i] = next; used[next] = true;
        }
        return result;
    }

    private static int FirstMelodic(int next, bool[] used)
    {
        for (var c = 0; c < ChannelCount; c++)
            if (c != PercussionChannel) return c;
        return 0;
    }
}
