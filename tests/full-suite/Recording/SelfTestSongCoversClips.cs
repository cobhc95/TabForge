using System.IO;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// A clip past the song end always extends the song (part of <see cref="SelfTest"/>): after loading a file whose clips run past the last bar, and after bar
/// deletes that leave a clip past the end (undo restores both). Also the drag-start path: a file drag shows its ghost from placeholders and measures off the UI thread.
/// </summary>
public static partial class SelfTest
{
    private static void TestSongCoversClipsOnLoadAndDelete()
    {
        // load: 12 bars (24 s) with a clip ending at 38 s -> 19 bars; the opened song is not marked changed
        var dir = Path.Combine(Path.GetTempPath(), $"tabforge-cover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var song = DropSong(2, 12);
            song.Tracks[0].AudioClips.Add(new AudioClip { File = @"C:\Media\late.wav", Name = "late", StartSec = 30, SourceLengthSec = 8, FileLengthSec = 8 });
            var path = Path.Combine(dir, "late.tforge");
            ProjectService.Save(path, song);
            var opened = new DocumentController().Open(path);
            Check("cover on load: a project whose clip runs past the last bar gets bars added to every track",
                opened.Project.Tracks.All(t => t.Measures.Count == 19), string.Join(",", opened.Project.Tracks.Select(t => t.Measures.Count)));
            Check("cover on load: the notice says so and the song is not marked changed", opened.Notice?.Contains("bars added") == true && !opened.Project.IsDirty, opened.Notice);
            var fits = DropSong(2, 12);
            fits.Tracks[0].AudioClips.Add(new AudioClip { File = @"C:\Media\in.wav", Name = "in", StartSec = 2, SourceLengthSec = 8, FileLengthSec = 8 });
            var fitsPath = Path.Combine(dir, "fits.tforge");
            ProjectService.Save(fitsPath, fits);
            var fitsOpened = new DocumentController().Open(fitsPath);
            Check("cover on load: a project whose clips fit gets nothing added", fitsOpened.Project.Tracks.All(t => t.Measures.Count == 12) && fitsOpened.Notice is null);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }

        // delete: 20 bars, a clip 30..38 s (bars 15..19); deleting bars 10..17 would leave 12 bars (24 s) under it
        var doc = new DocumentSession(new PlaybackEngine(new NullMidiOutput())) { Project = DropSong(2, 20) };
        doc.Project.Tracks[0].AudioClips.Add(new AudioClip { File = @"C:\Media\late.wav", Name = "late", StartSec = 30, SourceLengthSec = 8, FileLengthSec = 8 });
        var arrangement = new ArrangementController();
        arrangement.DeleteBars(doc, 10, 17);
        Check("cover on delete: bars deleted from under a clip leave the song covering the clip", SongExtent.Measure(doc.Project).EndSec >= 38 - 1e-6 && Bars(doc.Project) == 19, $"{Bars(doc.Project)} bars");
        var undone = doc.Undo.TryUndo(doc.Undo.Snapshot(doc.Project), out var target) ? doc.Undo.Restore(target) : null;
        Check("cover on delete: one undo restores the bars", undone is not null && Bars(undone) == 20 && undone.Tracks.All(t => t.Measures.Count == 20));

        var single = new DocumentSession(new PlaybackEngine(new NullMidiOutput())) { Project = DropSong(2, 20) };
        single.Project.Tracks[0].AudioClips.Add(new AudioClip { File = @"C:\Media\late.wav", Name = "late", StartSec = 30, SourceLengthSec = 8, FileLengthSec = 8 });
        arrangement.DeleteBar(single, 3, 0, allTracks: true, moveMarkers: false);
        Check("cover on delete: a single-bar delete keeps the song covering the clip", SongExtent.Measure(single.Project).EndSec >= 38 - 1e-6 && Bars(single.Project) >= 19);
        var free = new DocumentSession(new PlaybackEngine(new NullMidiOutput())) { Project = DropSong(2, 20) };
        arrangement.DeleteBars(free, 10, 17);
        Check("cover on delete: with no clip the bars are removed as before", Bars(free.Project) == 12);
    }

    private static void TestDragStartPlaceholdersAndAuditEntries()
    {
        Check("drag start: both speed-audit entries exist and are selected by 'drag start'",
            SpeedAuditDragEntries.All.Length == 2 && SpeedAuditDragEntries.All.All(n => n.Contains("drag start", StringComparison.Ordinal)) && SpeedAuditDragEntries.All.Distinct().Count() == 2);
        var wav = Path.Combine(Path.GetTempPath(), $"tabforge-drag-{Guid.NewGuid():N}.wav");
        WriteTestWav(wav, 3);
        try
        {
            var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { wav });
            using var session = MediaDropSession.From(data, measureLater: true)!;
            Check("drag start: a file drag enters as placeholders without reading the file", session.HasPending && session.Items[0].Seconds == 0 && session.Items[0].Pending);
            var song = DropSong(2, 12);
            var plan = MediaDrop.Plan(song, session.Items, 0, 0, 0, SongQuarterMap.For(song));
            Check("drag start: the ghost is planned at once with an estimated length", plan.Valid && plan.Estimated);
            session.MeasurePending(null);
            session.MeasurePending(null);   // idempotent: the drop calls it again
            Check("drag start: measuring fills in the real length once", !session.HasPending && Near(session.Items[0].Seconds, 3, 0.05), session.Items[0].Seconds.ToString());
            var again = MediaDrop.Plan(song, session.Items, 0, 0, 0, SongQuarterMap.For(song));
            Check("drag start: the refined plan is exact", again.Valid && !again.Estimated && Near(again.EndSec - again.StartSec, 3, 0.05));
            using var eager = MediaDropSession.From(data)!;
            Check("drag start: without measureLater the items are measured as before", !eager.HasPending && eager.Items[0].Seconds > 0);
        }
        finally { try { File.Delete(wav); } catch (IOException) { } }
    }
}
