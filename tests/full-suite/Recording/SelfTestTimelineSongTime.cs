using System.Diagnostics;
using System.IO;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Repeat-aware mapping for clip placement on the arrangement timeline.</summary>
public static partial class SelfTest
{
    private static void TestTimelineSongTimeRepeatGrowth()
    {
        var project = new SongProject { Tempo = 120 };
        var track = new TrackModel { Name = "Timing", Kind = TrackKind.Guitar };
        track.Measures.AddRange(Enumerable.Range(0, 2).Select(_ => new MeasureModel()));
        track.Measures[0].RepeatStart = true;
        track.Measures[1].RepeatEnd = true;
        track.Measures[1].RepeatCount = 3;
        project.Tracks.Add(track);

        var clock = new SongClock(AudioEngineClient.Instance);
        var timeline = new TrackTimeline { Project = project, MeasureWidth = 30 };
        timeline.BarStartSec = bar => clock.BarStartSec(project, bar);
        timeline.BarOfSec = sec => clock.BarAt(project, sec);

        var repeatedBarX = timeline.XOfBar(1) + timeline.BarWidthOf(1) * 0.5;
        var repeatedBarSec = timeline.SecOfX(repeatedBarX);
        Check("timeline song time: a repeat-end bar uses its first-occurrence local time",
            Math.Abs(repeatedBarSec - 3) < 0.001,
            $"bar center {repeatedBarX:0.##} px maps to {repeatedBarSec:0.###} s; expected 3 s");

        // Three performances of two 2-second bars end at 12 s. A clip at 14 s previews two bars
        // beyond the source score; growing to cover it must keep that time at the same x position.
        const double importedStartSec = 14;
        var beforeX = timeline.XOfSec(importedStartSec);
        var clipEndBefore = timeline.ClipEndX(3, 6);
        var clipSecBefore = timeline.ClipSecOfX(3, clipEndBefore);
        var beforeBarCount = track.Measures.Count;
        var growth = SongExtent.EnsureCovers(project, importedStartSec + 1);
        timeline.ValidateTimelineGeometry();
        var afterX = timeline.XOfSec(importedStartSec);
        var clipEndAfter = timeline.ClipEndX(3, 6);
        var clipSecAfter = timeline.ClipSecOfX(3, clipEndAfter);
        var afterBarCount = track.Measures.Count;
        var afterRepeatedBarSec = timeline.SecOfX(repeatedBarX);
        var afterRoundTripX = timeline.XOfSec(afterRepeatedBarSec);
        timeline.Snap = new SnapSettings { Enabled = true, ToGrid = true, Grid = "Bar", GridAtAnyDistance = true };
        var snappedFuture = timeline.SnapSec(importedStartSec, null, out _, altHeld: false);

        Log.Add($"  info  repeat-growth timing: {beforeBarCount} bars/{beforeX:0.##} px -> {afterBarCount} bars/{afterX:0.##} px; added {growth.BarsAdded}");
        Log.Add($"  info  repeat-bar round trip after growth: {repeatedBarX:0.##} px -> {afterRepeatedBarSec:0.###} s -> {afterRoundTripX:0.##} px");
        Log.Add($"  info  future bar snap at {importedStartSec:0.###} s -> {snappedFuture:0.###} s");
        Log.Add($"  info  clip crossing repeat end: x={timeline.XOfSec(3):0.##}..{clipEndBefore:0.##} before, x={timeline.XOfSec(3):0.##}..{clipEndAfter:0.##} after growth; end x maps to {clipSecBefore:0.###}/{clipSecAfter:0.###} s");
        Check("timeline song time: growing after repeated bars preserves an imported clip's start position",
            growth.BarsAdded > 0 && Math.Abs(afterX - beforeX) < 1,
            $"start {importedStartSec:0.###} s moved from {beforeX:0.##} px to {afterX:0.##} px");
        Check("timeline song time: source repeat-end duration stays local after song growth",
            Math.Abs(afterRepeatedBarSec - 3) < 0.001 && Math.Abs(afterRoundTripX - repeatedBarX) < 1,
            $"bar center {repeatedBarX:0.##} px -> {afterRepeatedBarSec:0.###} s -> {afterRoundTripX:0.##} px");
        Check("timeline song time: grid snapping in terminal extension preserves future clip time",
            Math.Abs(snappedFuture - importedStartSec) < 0.001,
            $"start {importedStartSec:0.###} s snapped to {snappedFuture:0.###} s");
        Check("timeline song time: a clip spanning a repeat remains visible and stable across growth",
            clipEndBefore > timeline.XOfSec(3) && Math.Abs(clipEndAfter - clipEndBefore) < 1 && Math.Abs(clipSecBefore - 6) < 0.001 && Math.Abs(clipSecAfter - 6) < 0.001,
            $"clip from 3 to 6 s ends at {clipEndBefore:0.##} px before growth and {clipEndAfter:0.##} px after growth");
        var replayedNoteStartX = timeline.ClipEndX(3, 8.5);
        var replayedNoteEndX = timeline.ClipEndX(3, 9);
        Check("timeline song time: MIDI note endpoints stay on their parent clip after a repeat",
            replayedNoteStartX > timeline.XOfSec(8.5) && replayedNoteEndX > replayedNoteStartX,
            $"parent-anchored note at 8.5–9 s maps to {replayedNoteStartX:0.##}..{replayedNoteEndX:0.##} px while performed-time start maps to {timeline.XOfSec(8.5):0.##} px");

        var changed = RepeatingTimingSong();
        changed.Tracks[0].Measures[1].TimeSigNum = 3;
        changed.Tracks[0].Measures[1].TempoChange = 60;
        var changedTimeline = TimingTimeline(changed);
        var changedX = changedTimeline.XOfBar(1) + changedTimeline.BarWidthOf(1) * 0.5;
        var changedSec = changedTimeline.SecOfX(changedX);
        Check("timeline song time: mixed meter and tempo keep the repeat-end bar's exact duration",
            Math.Abs(changedSec - 3.5) < 0.001,
            $"3/4 at 60 BPM bar center maps to {changedSec:0.###} s; expected 3.5 s");

        var held = RepeatingTimingSong();
        held.Tracks[0].Measures[1].Cells[0] = new TabCell { IsRest = true, Fermata = true, DurationDenominator = 4 };
        var heldTimeline = TimingTimeline(held);
        var heldX = heldTimeline.XOfBar(1) + heldTimeline.BarWidthOf(1) * 0.25;
        var heldSec = heldTimeline.SecOfX(heldX);
        var heldEndX = heldTimeline.ClipEndX(2.5, 3.2);
        var heldEndSec = heldTimeline.ClipSecOfX(2.5, heldEndX);
        Check("timeline song time: fermata holds keep exact repeat-bar timing",
            Math.Abs(heldSec - 3) < 0.001,
            $"bar quarter maps to {heldSec:0.###} s through a fermata; expected 3 s");
        Check("timeline song time: clip endpoint mapping preserves fermata time in both directions",
            heldEndX > heldTimeline.XOfSec(2.5) && Math.Abs(heldEndSec - 3.2) < 0.001,
            $"2.5 s -> x {heldEndX:0.##} -> {heldEndSec:0.###} s; expected 3.2 s");

        var pickup = RepeatingTimingSong();
        pickup.ImportedFrom = "fixture";
        pickup.Tracks[0].Measures[1].Cells[0] = new TabCell { IsRest = true, DurationDenominator = 8 };
        var pickupTimeline = TimingTimeline(pickup);
        var pickupX = pickupTimeline.XOfBar(1) + pickupTimeline.BarWidthOf(1) * 0.5;
        var pickupSec = pickupTimeline.SecOfX(pickupX);
        Check("timeline song time: an imported short bar keeps its pickup length",
            Math.Abs(pickupSec - 2.125) < 0.001,
            $"short repeat bar center maps to {pickupSec:0.###} s; expected 2.125 s");

        ProfileTimelineSongTimeFromEnvironment();
    }

    private static SongProject RepeatingTimingSong()
    {
        var project = new SongProject { Tempo = 120 };
        var track = new TrackModel { Name = "Timing", Kind = TrackKind.Guitar };
        track.Measures.AddRange(Enumerable.Range(0, 2).Select(_ => new MeasureModel()));
        track.Measures[0].RepeatStart = true;
        track.Measures[1].RepeatEnd = true;
        track.Measures[1].RepeatCount = 3;
        project.Tracks.Add(track);
        return project;
    }

    private static TrackTimeline TimingTimeline(SongProject project)
    {
        var clock = new SongClock(AudioEngineClient.Instance);
        var timeline = new TrackTimeline { Project = project, MeasureWidth = 30 };
        timeline.BarStartSec = bar => clock.BarStartSec(project, bar);
        timeline.BarOfSec = sec => clock.BarAt(project, sec);
        return timeline;
    }

    private static void ProfileTimelineSongTimeFromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("TABFORGE_TIMELINE_SPEED_PROFILE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var project = GuitarProImporter.Import(path!);
        var timeline = TimingTimeline(project);
        var bars = Math.Max(1, project.Tracks.Max(t => t.Measures.Count));
        var cold = Stopwatch.StartNew();
        _ = timeline.SecOfX(timeline.XOfBar(Math.Min(8, bars - 1)) + timeline.BarWidthOf(Math.Min(8, bars - 1)) * 0.5);
        cold.Stop();
        var hot = Stopwatch.StartNew();
        for (var i = 0; i < 5000; i++)
        {
            var bar = i % bars;
            var x = timeline.XOfBar(bar) + timeline.BarWidthOf(bar) * 0.5;
            var sec = timeline.SecOfX(x);
            _ = timeline.XOfSec(sec);
        }
        hot.Stop();
        project.MarkTimelineChanged();
        timeline.ValidateTimelineGeometry();
        var rebuild = Stopwatch.StartNew();
        _ = timeline.SecOfX(timeline.XOfBar(Math.Min(8, bars - 1)) + timeline.BarWidthOf(Math.Min(8, bars - 1)) * 0.5);
        rebuild.Stop();
        Log.Add($"  info  timeline map profile: {bars} bars; cold build {cold.Elapsed.TotalMilliseconds:0.##} ms; 5000 cached x/time pairs {hot.Elapsed.TotalMilliseconds:0.##} ms ({hot.Elapsed.TotalMicroseconds / 5000:0.###} μs/pair); after-revision rebuild {rebuild.Elapsed.TotalMilliseconds:0.##} ms");
    }
}
