using System.IO;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>The song grows to cover clips that end after its last bar (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestSongExtent()
    {
        TestSongExtentMeasureCache();

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
        var undoAvailable = undo.TryUndo(undo.Snapshot(song), out var target);
        var back = undoAvailable ? undo.Restore(target, song) : null;
        Check("extent: undo removes the clip and the bars together", back is not null
            && back.Tracks[0].Measures.Count == 12 && back.Tracks.All(x => x.Measures.Count == 12) && back.Tracks[0].AudioClips.Count == 1);
        var restoredExtent = back is null ? default : SongExtent.Measure(back);
        Check("extent cache: undo restores a project with its own fresh measurement cache",
            back is not null && !ReferenceEquals(song.SongExtentMeasures, back.SongExtentMeasures) && Near(restoredExtent.EndSec, 24));
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

    private static void TestSongExtentMeasureCache()
    {
        var clips = DropSong(3, 2);
        var clip = MoveClip("cached", 0, 1, 0);
        clips.Tracks[1].AudioClips.Add(clip);
        var initial = SongExtent.Measure(clips);
        var cache = clips.SongExtentMeasures;
        var builds = cache.BuildCount;
        Check("extent cache: repeated bar measurements reuse the same result", SongExtent.Measure(clips) == initial && cache.BuildCount == builds);

        clip.StartSec = 5;
        var growth = SongExtent.EnsureCovers(clips, clip.EndSec);
        Check("extent cache: a moved clip uses its current end while keeping the bar measurement", growth.BarsAdded == 1 && cache.BuildCount == builds);
        var grown = SongExtent.Measure(clips);
        Check("extent cache: appending bars invalidates the measured end", grown.EndSec >= clip.EndSec && cache.BuildCount == builds + 1);

        var tempo = DropSong(1, 4);
        var tempoBefore = SongExtent.Measure(tempo);
        var tempoBuilds = tempo.SongExtentMeasures.BuildCount;
        tempo.Tempo = 60;
        var tempoAfter = SongExtent.Measure(tempo);
        Check("extent cache: a direct song-tempo change refreshes the end without a revision mark",
            Near(tempoBefore.EndSec, 8) && Near(tempoAfter.EndSec, 16) && tempo.SongExtentMeasures.BuildCount == tempoBuilds + 1);

        var signature = DropSong(1, 4);
        var signatureBefore = SongExtent.Measure(signature);
        signature.Tracks[0].Measures[1].TimeSigNum = 3;
        signature.Tracks[0].Measures[1].TimeSigDenom = 4;
        var signatureAfter = SongExtent.Measure(signature);
        Check("extent cache: a direct inner-bar time-signature change refreshes the end without a revision mark",
            Near(signatureBefore.EndSec, 8) && Near(signatureAfter.EndSec, 7.5));
        signature.Tracks[0].Measures[^1].TimeSigNum = 3;
        var lastSignatureAfter = SongExtent.Measure(signature);
        Check("extent cache: a direct last-bar signature change refreshes the next-bar length", Near(lastSignatureAfter.BarSec, 1.5));

        var tempoMap = DropSong(1, 4);
        var mapBefore = SongExtent.Measure(tempoMap);
        tempoMap.Tracks[0].Measures[1].MidBarTempos = new List<TempoPoint> { new(8, 60) };
        var mapAfter = SongExtent.Measure(tempoMap);
        Check("extent cache: a direct inner-bar tempo-map change refreshes the end without a revision mark",
            Near(mapBefore.EndSec, 8) && Near(mapAfter.EndSec, 13));

        var repeat = DropSong(1, 2);
        var repeatBefore = SongExtent.Measure(repeat);
        repeat.Tracks[0].Measures[0].RepeatStart = true;
        repeat.Tracks[0].Measures[1].RepeatEnd = true;
        repeat.Tracks[0].Measures[1].RepeatCount = 3;
        var repeatAfter = SongExtent.Measure(repeat);
        Check("extent cache: direct repeat changes refresh the performed end without a revision mark",
            Near(repeatBefore.EndSec, 4) && Near(repeatAfter.EndSec, 12));

        var fermata = DropSong(1, 2);
        var fermataBefore = SongExtent.Measure(fermata);
        var heldCell = fermata.Tracks[0].Measures[0].Cells[0];
        heldCell.IsRest = true;
        heldCell.Fermata = true;
        var fermataAdded = SongExtent.Measure(fermata);
        Check("extent cache: an unmarked fermata addition refreshes the performed end", fermataAdded.EndSec > fermataBefore.EndSec);
        heldCell.DurationDenominator = 4;
        var fermataChanged = SongExtent.Measure(fermata);
        Check("extent cache: an unmarked fermata duration change refreshes the performed end", fermataChanged.EndSec > fermataAdded.EndSec);
        heldCell.Fermata = false;
        var fermataRemoved = SongExtent.Measure(fermata);
        Check("extent cache: removing an unmarked fermata refreshes the performed end", Near(fermataRemoved.EndSec, fermataBefore.EndSec));

        var imported = DropSong(1, 2);
        imported.ImportedFrom = "GPX";
        var importedBefore = SongExtent.Measure(imported);
        var importedCell = imported.Tracks[0].Measures[0].Cells[0];
        importedCell.IsRest = true;
        var importedAdded = SongExtent.Measure(imported);
        importedCell.DurationDenominator = 4;
        var importedLengthened = SongExtent.Measure(imported);
        importedCell.RhythmicPosition = 4;
        var importedMoved = SongExtent.Measure(imported);
        importedCell.IsRest = false;
        var importedRemoved = SongExtent.Measure(imported);
        Check("extent cache: imported beat addition, duration, position and removal refresh the performed end",
            Near(importedBefore.EndSec, 4) && Near(importedAdded.EndSec, 2.25) && Near(importedLengthened.EndSec, 2.5)
            && Near(importedMoved.EndSec, 3) && Near(importedRemoved.EndSec, 4));

        var cloned = DropSong(1, 2);
        SongExtent.Measure(cloned);
        var originalCache = cloned.SongExtentMeasures;
        var withoutStartup = cloned.WithoutStartupTracks();
        withoutStartup.Tempo = 60;
        var cloneAfter = SongExtent.Measure(withoutStartup);
        Check("extent cache: a startup-track copy owns an independent measurement cache",
            !ReferenceEquals(originalCache, withoutStartup.SongExtentMeasures) && Near(cloneAfter.EndSec, 8) && Near(SongExtent.Measure(cloned).EndSec, 4));

        var fileSong = DropSong(1, 2);
        var fileExtent = SongExtent.Measure(fileSong);
        var fileCache = fileSong.SongExtentMeasures;
        var path = Path.Combine(Path.GetTempPath(), "tf-extent-cache-" + Guid.NewGuid().ToString("N") + ".tforge");
        try
        {
            ProjectService.Save(path, fileSong);
            var loaded = ProjectService.Load(path);
            var loadedCache = loaded.SongExtentMeasures;
            var loadedExtent = SongExtent.Measure(loaded);
            Check("extent cache: project save and load omit runtime cache state and recompute independently",
                !ReferenceEquals(fileCache, loadedCache) && fileCache.BuildCount == 1 && loadedCache.BuildCount == 1 && loadedExtent == fileExtent);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
