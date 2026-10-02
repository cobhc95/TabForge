using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>The song grows to cover clips that end after its last bar (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestSongExtent()
    {
        // 12 bars of 4/4 at 120 = 2 s per bar = 24 s.
        var song = DropSong();
        var t = song.Tracks[0];
        var undo = new UndoController();

        var fits = MoveClip("fits", 0, 20, 0);
        t.AudioClips.Add(fits);
        Check("extent: a clip inside the song adds no bars", SongExtent.EnsureCovers(song, fits.EndSec) is { BarsAdded: 0, Capped: false } && t.Measures.Count == 12);

        undo.Capture(song);
        var before = undo.Snapshot(song);
        var longAudio = MoveClip("long", 10, 31, 0);   // ends at 41 s: 17 s past the end = 9 bars of 2 s
        t.AudioClips.Add(longAudio);
        var result = SongExtent.EnsureCovers(song, longAudio.EndSec);
        Check("extent: a longer audio clip adds whole bars so the end is covered", result.BarsAdded == 9 && !result.Capped && song.Tracks.All(x => x.Measures.Count == 21));
        Check("extent: the clip is not trimmed", Near(longAudio.StartSec, 10) && Near(longAudio.SourceLengthSec, 31) && Near(longAudio.EndSec, 41));
        Check("extent: the song now reaches the clip's end and one bar more is not added", SongExtent.Measure(song).EndSec >= 41 && SongExtent.EnsureCovers(song, 41).BarsAdded == 0);
        Check("extent: the new bars are empty and keep the time signature", t.Measures.Skip(12).All(m => m.TimeSigNum == 4 && m.TimeSigDenom == 4 && m.Cells.All(c => c is null || c.Notes.Count == 0)));
        Check("extent: undo removes the clip and the bars together", undo.TryUndo(undo.Snapshot(song), out var target) && undo.Restore(target, song) is { } back
            && back.Tracks[0].Measures.Count == 12 && back.Tracks.All(x => x.Measures.Count == 12) && back.Tracks[0].AudioClips.Count == 1);
        _ = before;

        // a clip ending exactly on a bar line adds only the bars needed
        var exact = DropSong();
        Check("extent: a clip ending exactly at the last bar adds nothing, one second later adds one bar",
            SongExtent.EnsureCovers(exact, 24).BarsAdded == 0 && SongExtent.EnsureCovers(exact, 25).BarsAdded == 1 && exact.Tracks[0].Measures.Count == 13);

        // MIDI: its length follows the tempo at the end
        var midiSong = DropSong();
        var time = SongQuarterMap.For(midiSong);
        var plan = MediaDrop.Plan(midiSong, new[] { MidiItem("groove", 80) }, 0, 0, 20, time);   // 80 quarters = 40 s at 120: ends at 60 s
        Check("extent: a MIDI drop longer than the song plans past the end", plan.Valid && plan.EndSec > 24);
        var midiEnd = SongExtent.EnsureCovers(midiSong, plan.EndSec);
        Check("extent: the MIDI drop's end is covered", midiEnd.BarsAdded == 18 && SongExtent.Measure(midiSong).EndSec >= plan.EndSec - 1e-6);

        // the last bar's time signature and the tempo in effect at the end decide the new bars' length
        var odd = DropSong(2, 4, 3, 4, 120);   // 3/4 at 120: 1.5 s a bar, 6 s in all
        odd.Tracks[0].Measures[^1].TempoChange = 60;   // the last bar (and the end of the song) at 60: 3 s a bar
        odd.Tracks[1].Measures[^1].TempoChange = 60;
        var oddEnd = SongExtent.Measure(odd);
        var added = SongExtent.EnsureCovers(odd, oddEnd.EndSec + 7);
        Check("extent: new bars use the last bar's time signature and the tempo at the end (3 s bars: 7 s needs 3 bars)",
            Near(oddEnd.BarSec, 3, 1e-3) && added.BarsAdded == 3 && odd.Tracks.All(x => x.Measures.Count == 7 && x.Measures[^1].TimeSigNum == 3 && x.Measures[^1].TimeSigDenom == 4));

        // moving a clip later extends too (the same call the move uses)
        var moved = DropSong();
        var mc = MoveClip("m", 0, 4, 0);
        moved.Tracks[1].AudioClips.Add(mc);
        MediaDrop.ApplyMove(moved.Tracks[1], moved.Tracks[2], mc, 0, 30, copy: false, removeEmptyLanes: true);
        Check("extent: a clip moved past the end extends the song for all tracks", SongExtent.EnsureCovers(moved, mc.EndSec).BarsAdded == 5 && moved.Tracks.All(x => x.Measures.Count == 17));

        // the cap: add up to the limit, report it, never fail
        var capped = DropSong(1, InputLimits.MaxMeasuresPerTrack - 3);
        var cap = SongExtent.EnsureCovers(capped, 1_000_000);
        Check("extent: at the length limit it adds up to the limit and reports it", cap.Capped && cap.BarsAdded == 3 && capped.Tracks[0].Measures.Count == InputLimits.MaxMeasuresPerTrack);
        Check("extent: no clip ending and no tracks are harmless", SongExtent.EnsureCovers(new SongProject(), 50).BarsAdded == 0 && SongExtent.EnsureCovers(DropSong(), 0).BarsAdded == 0);
    }
}
