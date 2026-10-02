using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Clips lying fully inside a section move, duplicate and paste with it, by bar and beat; clips crossing an edge stay (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestSectionClips()
    {
        var arrangement = new ArrangementController();
        // DoSong: sections A (bars 1-4) and B (bars 5-8), 4/4 at the project tempo; the bar length is measured from the song.
        DocumentSession Open(out double barSec, Action<SongProject>? tune = null)
        {
            var song = DoSong(2, 8);
            barSec = SongExtent.Measure(song).EndSec / 8;
            tune?.Invoke(song);
            var d = DocumentSession.FromProject(song, null);
            d.MarkClean();
            return d;
        }
        AudioClip Clip(double start, double length) => CseClip("c", start, length);

        // ---- move: the inside clip travels, the edge-crossing clip and the clip in B stay ----
        var d1 = Open(out var bar);
        var t0 = d1.Project.Tracks[0];
        var (inside, crossing, inB) = (Clip(1.25 * bar, 0.5 * bar), Clip(3.5 * bar, bar), Clip(5.0 * bar, bar));
        t0.AudioClips.AddRange(new[] { inside, crossing, inB });
        var before = DoHash(d1);
        var moved = arrangement.MoveSection(d1, 0, 2).Changed;
        Check("section clips: moving a section moves the clip lying inside it to the same bar and beat", moved && Math.Abs(inside.StartSec - 5.25 * bar) < 1e-6, $"{inside.StartSec / bar} bars");
        Check("section clips: a clip crossing the section edge and a clip in the other section stay put",
            Math.Abs(crossing.StartSec - 3.5 * bar) < 1e-6 && Math.Abs(inB.StartSec - 5.0 * bar) < 1e-6);
        Check("section clips: the move and its clips are one undo step", d1.Undo.UndoCount == 1 && DocumentEdits.Undo(d1) is not null && DoHash(d1) == before && d1.Project.Tracks[0].AudioClips.Any(c => Math.Abs(c.StartSec - 1.25 * bar) < 1e-6));

        // ---- duplicate: the copy comes with copies of the inside clips ----
        var d2 = Open(out bar);
        var src = d2.Project.Tracks[0];
        var (a2, edge2) = (Clip(1.25 * bar, 0.5 * bar), Clip(3.5 * bar, bar));
        src.AudioClips.AddRange(new[] { a2, edge2 });
        var before2 = DoHash(d2);
        var marker = d2.Project.Markers[0];
        var snapshot = arrangement.CaptureSectionSnapshot(d2.Project, marker)!;
        arrangement.TryGetSectionBounds(d2.Project, marker, out _, out var end);
        var inserted = arrangement.InsertSection(d2, end, snapshot).Changed;
        Check("section clips: duplicating a section copies the clip lying inside it and not the edge-crossing one",
            inserted && src.AudioClips.Count == 3 && src.AudioClips.Count(c => Math.Abs(c.StartSec - 5.25 * bar) < 1e-6) == 1
            && src.AudioClips.Select(c => c.Id).Distinct().Count() == 3 && src.AudioClips.Contains(a2) && Math.Abs(a2.StartSec - 1.25 * bar) < 1e-6);
        Check("section clips: the duplicate and its clips are one undo step", d2.Undo.UndoCount == 1 && DocumentEdits.Undo(d2) is not null && DoHash(d2) == before2 && d2.Project.Tracks[0].AudioClips.Count == 2);

        // ---- copy then paste somewhere else ----
        var d3 = Open(out bar);
        d3.Project.Tracks[1].AudioClips.Add(Clip(2.5 * bar, 0.5 * bar));
        var board = new ClipboardService(null);
        var copied = arrangement.CopySection(d3.Project, d3.Project.Markers[0], board, out _);
        var clip = board.TryGetClip(out _);
        var pasted = arrangement.InsertSection(d3, 8, new SectionClipboardSnapshot(
            TimelineClips.BarsPerTrack(clip!, d3.Project, 0, out _), arrangement.SectionMarkerFor(clip), arrangement.SectionClipsFor(clip))).Changed;
        var onTrack = d3.Project.Tracks[1].AudioClips;
        Check("section clips: pasting a copied section places its clips on the pasted bars", copied is not null && pasted && onTrack.Count == 2 && onTrack.Any(c => Math.Abs(c.StartSec - 10.5 * bar) < 1e-6),
            string.Join(", ", onTrack.Select(c => (c.StartSec / bar).ToString("0.###"))));

        // ---- a tempo change between the old and the new place: same bar and beat ----
        var d4 = Open(out bar, s => { foreach (var track in s.Tracks) track.Measures[4].TempoChange = (int)Math.Round(s.Tempo / 2.0); });   // bars 5-8 run at half speed
        var c4 = Clip(1.25 * bar, 0.5 * bar);
        d4.Project.Tracks[0].AudioClips.Add(c4);
        arrangement.MoveSection(d4, 0, 2);   // B (half speed) now comes first and A follows at that speed
        var slow = SongExtent.Measure(d4.Project).EndSec / 8;
        Check("section clips: with a different tempo at the new place the clip keeps its bar and beat", Math.Abs(c4.StartSec - 5.25 * slow) < 1e-6, $"{c4.StartSec} s, bar {slow} s");

        // ---- the song grows to cover a clip that no longer fits ----
        var d5 = Open(out bar, s => { foreach (var track in s.Tracks) track.Measures[4].TempoChange = s.Tempo * 3; });   // bars 5-8 run three times faster
        var long5 = Clip(0, 3.5 * bar);
        d5.Project.Tracks[0].AudioClips.Add(long5);
        var bars5 = d5.Project.Tracks[0].Measures.Count;
        var before5 = DoHash(d5);
        arrangement.MoveSection(d5, 0, 2);
        Check("section clips: the song grows in the same step to cover the moved clip",
            d5.Project.Tracks[0].Measures.Count > bars5 && SongExtent.Measure(d5.Project).EndSec >= long5.EndSec - 1e-6 && d5.Undo.UndoCount == 1
            && DocumentEdits.Undo(d5) is not null && DoHash(d5) == before5 && d5.Project.Tracks[0].Measures.Count == bars5);

        // ---- cut: the inside clips leave with the bars in one step; paste brings them back at the same bar and beat ----
        var d6 = Open(out bar);
        var (in6, edge6) = (Clip(1.25 * bar, 0.5 * bar), Clip(3.5 * bar, bar));
        d6.Project.Tracks[0].AudioClips.AddRange(new[] { in6, edge6 });
        var before6 = DoHash(d6);
        var marker6 = d6.Project.Markers[0];
        var cutBoard = new ClipboardService(null);
        var cutClip = arrangement.CopySection(d6.Project, marker6, cutBoard, out _);
        var cutDone = arrangement.DeleteSection(d6, marker6, takeClips: true).Changed;
        var left = d6.Project.Tracks[0].AudioClips;
        Check("section clips: cutting a section removes the clips fully inside it in the same undo step and leaves the edge-crossing clip",
            cutDone && d6.Undo.UndoCount == 1 && left.Count == 1 && left[0].Name == edge6.Name && Math.Abs(left[0].StartSec - 3.5 * bar) < 1e-6);
        var cutPaste = arrangement.InsertSection(d6, 4, new SectionClipboardSnapshot(
            TimelineClips.BarsPerTrack(cutClip!, d6.Project, 0, out _), arrangement.SectionMarkerFor(cutClip), arrangement.SectionClipsFor(cutClip))).Changed;
        Check("section clips: pasting the cut section restores its clip at the same bar and beat in the pasted section",
            cutPaste && d6.Project.Tracks[0].AudioClips.Any(c => Math.Abs(c.StartSec - 5.25 * bar) < 1e-6));
        Check("section clips: undo of the cut brings the clips back", DocumentEdits.Undo(d6) is not null && DocumentEdits.Undo(d6) is not null && DoHash(d6) == before6 && d6.Project.Tracks[0].AudioClips.Count == 2);
    }
}
