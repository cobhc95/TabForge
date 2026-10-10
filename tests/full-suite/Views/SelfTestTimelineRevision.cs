using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Timing caches (SongClock map, plug-in transport map) key on SongProject.TimelineRevision, so every timing edit rebuilds them.</summary>
public static partial class SelfTest
{
    private static void TestTimelineRevision()
    {
        var p = SingleTrack(4, 120);
        var clock = new SongClock(AudioEngineClient.Instance);
        string Fingerprint()
        {
            var transport = SongClock.TransportBars(p);
            var parts = transport.Select(b => FormattableString.Invariant($"{b.StartSec:0.###}/{b.Tempo:0.#}/{b.Numerator}/{b.Denominator}")).ToList();
            for (var bar = 0; bar < p.Tracks[0].Measures.Count; bar++)
                parts.Add(FormattableString.Invariant($"b{bar}:{clock.BarStartSec(p, bar):0.###}-{clock.BarEndSec(p, bar):0.###}"));
            for (var sec = 0.25; sec < 40; sec += 0.5)
            {
                var (bar, fraction) = clock.BarAt(p, sec);
                parts.Add(FormattableString.Invariant($"{bar}:{fraction:0.###}"));
            }
            return string.Join(" ", parts);
        }

        var first = SongClock.TransportBars(p);
        Check("A5-08: an unchanged project reuses its cached transport map", ReferenceEquals(first, SongClock.TransportBars(p)));
        Check("A5-08: a new project starts at timeline revision 0 and MarkTimelineChanged bumps it",
            new SongProject().TimelineRevision == 0 && Bump(new SongProject()) == 1);

        var undo = new UndoController();
        var start = undo.Snapshot(p);
        var startPrint = Fingerprint();
        void Edit(string what, Action<SongProject> change)
        {
            var before = Fingerprint();
            var revision = p.TimelineRevision;
            change(p);
            p.MarkTimelineChanged();
            var after = Fingerprint();
            Check($"A5-08: {what} on the same project rebuilds the timing maps", after != before && p.TimelineRevision == revision + 1, after);
        }
        Edit("a per-bar tempo change", s => s.Tracks[0].Measures[1].TempoChange = 60);
        Edit("a mid-bar tempo change", s => s.Tracks[0].Measures[2].MidBarTempos = new List<TempoPoint> { new(8, 200) });
        Edit("a mid-bar tempo ramp", s => s.Tracks[0].Measures[2].MidBarTempos = new List<TempoPoint> { new(4, 200, 8) });
        Edit("a time signature change", s => s.Tracks[0].Measures[3].TimeSigNum = 3);
        Edit("repeat marks", s => { s.Tracks[0].Measures[0].RepeatStart = true; s.Tracks[0].Measures[1].RepeatEnd = true; });
        Edit("a repeat count", s => s.Tracks[0].Measures[1].RepeatCount = 3);
        Edit("an alternate ending", s => s.Tracks[0].Measures[1].AlternateEnding = 1);
        Edit("a D.C. direction", s => s.Tracks[0].Measures[3].Directions = "DaCapo");
        Edit("a bar move (same bar count)", s => { var m = s.Tracks[0].Measures; (m[2], m[3]) = (m[3], m[2]); });
        Edit("a bar insert", s => s.Tracks[0].Measures.Insert(1, new MeasureModel { TimeSigNum = 2 }));
        Edit("a bar delete", s => s.Tracks[0].Measures.RemoveAt(1));

        // Undo/redo: the window bumps the revision of whatever the restore returns (in place or a new object).
        var editedPrint = Fingerprint();
        var restored = undo.Restore(start, p);
        restored.MarkTimelineChanged();
        p = restored;
        var undonePrint = Fingerprint();
        Check("A5-08: undo back to the start rebuilds the timing maps to the original timing", undonePrint != editedPrint && undonePrint == startPrint, undonePrint);
    }

    private static int Bump(SongProject project) { project.MarkTimelineChanged(); return project.TimelineRevision; }
}
