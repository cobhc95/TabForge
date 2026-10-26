using TabForge.Models;

namespace TabForge.Presenters;

// Owns: the piano roll's ghost notes (an option, off by default): the notes of the shown track's other MIDI takes (clips on lanes that do not play)
//       and of the other tracks, placed on the shown track's clock through song seconds. They are drawn faint and are never edited.
// Does not own: the shown notes (the sources), the option (EditingSettings.PianoRollGhosts) or the drawing (the app's PianoRollSurface).
// Tests: TabForge.Avalonia.Tests.PianoRollRoundTests.
public static class PianoRollGhosts
{
    /// <summary>Every ghost note for track <paramref name="trackIndex"/> on <paramref name="map"/>'s clock.</summary>
    public static List<PianoRollNote> Read(SongProject song, int trackIndex, PianoRollTimeMap map)
    {
        var list = new List<PianoRollNote>();
        for (var t = 0; t < song.Tracks.Count; t++)
        {
            var track = song.Tracks[t];
            if (track.IsBus) continue;
            if (t == trackIndex)
            {
                if (!PianoRollViewRules.UsesClips(track)) continue;
                var shown = PianoRollClipSource.Shown(track);
                foreach (var clip in track.AudioClips.Where(c => c.IsMidi && !shown.Contains(c))) list.AddRange(PianoRollClipSource.ReadClip(clip, map));
                continue;
            }
            if (!track.HasNotation && !PianoRollViewRules.HasMidiClips(track)) continue;
            var source = PianoRollSources.For(track);
            var own = source.Map(song);
            foreach (var note in source.Read(song, track, own))
            {
                var start = map.BeatsAt(own.SecondsAt(note.Start));
                var end = map.BeatsAt(own.SecondsAt(note.End));
                list.Add(note with { Start = start, Length = Math.Max(PianoRollEdits.MinLength, end - start) });
            }
        }
        return list;
    }
}
