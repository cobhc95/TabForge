using TabForge.Models;

namespace TabForge.Playback;

// Owns: deciding whether an edit changed what the playing song sounds like (bars, tempo, time signature, tuning, capo), so a mixer-only mark never re-schedules playback.
// Does not own: the re-schedule (PlaybackEditRefresh), the song copy (SongSnapshot) or track list changes (AvaloniaPlaybackSessions.TracksChanged owns those).
// Tests: SelfTestPlaybackEditRefresh.

/// <summary>Compares the song copy that is playing with the copy taken after an edit.</summary>
internal static class PlaybackEditDiff
{
    /// <param name="playing">The copy the playing timeline was compiled from (decoded: the compile read it).</param>
    /// <param name="now">The copy taken after the edit.</param>
    /// <param name="live">The live song, read on the owner thread.</param>
    public static bool SoundChanged(SongSnapshot playing, SongSnapshot now, SongProject live)
    {
        if (ReferenceEquals(playing, now)) return false;
        var was = playing.Song;
        if (was.Tempo != live.Tempo || was.TimeSignatureNumerator != live.TimeSignatureNumerator || was.TimeSignatureDenominator != live.TimeSignatureDenominator) return true;
        if (was.Tracks.Count != live.Tracks.Count) return false;   // a track added, deleted or moved: AvaloniaPlaybackSessions.TracksChanged re-plans it
        for (var i = 0; i < was.Tracks.Count; i++)
            if (was.Tracks[i].Id != live.Tracks[i].Id) return false;
        for (var i = 0; i < was.Tracks.Count; i++)
        {
            var before = was.Tracks[i];
            var after = live.Tracks[i];
            if (before.Capo != after.Capo || before.Transpose != after.Transpose || !before.StringTunings.SequenceEqual(after.StringTunings)) return true;
        }
        var a = playing.State.Tracks;
        var b = now.State.Tracks;
        for (var track = 0; track < a.Length && track < b.Length; track++)
        {
            if (a[track].Bars.Length != b[track].Bars.Length) return true;
            for (var bar = 0; bar < a[track].Bars.Length; bar++)
                if (!a[track].Bars[bar].SameContent(b[track].Bars[bar]) || a[track].Numbers[bar] != b[track].Numbers[bar]) return true;
        }
        return false;
    }
}
