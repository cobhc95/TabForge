using System.Windows;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Dragging clips between lanes and tracks: the plan (same as a file drop), moving / copying with the take rules, closing empty
/// lanes (and leaving them with the setting off or on an armed track), one undo step, a MIDI clip on a drum track, and the ghost
/// (part of <see cref="SelfTest"/>).
/// </summary>
public static partial class SelfTest
{
    private static AudioClip MoveClip(string name, double start, double length, int lane, bool midi = false)
    {
        var clip = LaneClip(start, length, lane, midi);
        clip.Name = name;
        if (midi) { clip.Notes!.Add(new ClipNote(0, 0.25, 36, 100)); }
        return clip;
    }

    private static void TestClipMoves()
    {
        var time = SongQuarterMap.For(DropSong());

        // ---- within one track, to another lane ----
        foreach (var tidy in new[] { false, true })
        {
            var song = DropSong();
            var t = song.Tracks[1];
            var a = MoveClip("A", 0, 4, 0);
            var b = MoveClip("B", 10, 2, 1);
            t.AudioClips.Add(a); t.AudioClips.Add(b);
            ClipLanes.Ensure(t, 2);
            var plan = MediaDrop.PlanMove(song, a, t.Kind, 1, 1, 5, time);
            Check($"clip move: to a free stretch of lane 2 of the same track plans that lane, no new lane (tidy {tidy})", plan.Valid && plan.TrackIndex == 1 && plan.Lane == 1 && !plan.NewLane && Near(plan.StartSec, 5));
            MediaDrop.ApplyMove(t, t, a, plan.Lane, plan.StartSec, copy: false, removeEmptyLanes: tidy);
            Check($"clip move: the clip is on the new lane at the new time (tidy {tidy})", Near(a.StartSec, 5) && a.Lane == (tidy ? 0 : 1) && Near(b.StartSec, 10));
            Check(tidy ? "clip move: the emptied source lane is removed and the lane below closes up" : "clip move: with the setting off the emptied lane stays",
                tidy ? t.Lanes.Count == 1 && b.Lane == 0 && ClipLanes.Count(t) == 1 : t.Lanes.Count == 2 && b.Lane == 1 && ClipLanes.Count(t) == 2);
        }

        // ---- a clip never blocks its own spot; a busy target moves to the first free lane ----
        {
            var song = DropSong();
            var t = song.Tracks[0];
            var a = MoveClip("A", 0, 4, 0);
            t.AudioClips.Add(a);
            ClipLanes.Ensure(t, 1);
            Check("clip move: nudging a clip over its own old position keeps its lane", MediaDrop.PlanMove(song, a, t.Kind, 0, 0, 1, time) is { Lane: 0, NewLane: false });
            var other = MoveClip("O", 6, 4, 0);
            t.AudioClips.Add(other);
            var onto = MediaDrop.PlanMove(song, a, t.Kind, 0, 0, 5, time);
            Check("clip move: over another clip of the lane it drops to a new lane", onto.Lane == 1 && onto.NewLane);
        }

        // ---- to another track (one with no lanes: a lane is made), take rules, play state ----
        var project = DropSong();
        var from = project.Tracks[1];
        var keep = MoveClip("A", 0, 4, 0);
        var take = MoveClip("B", 1, 2, 1);
        from.AudioClips.Add(keep); from.AudioClips.Add(take);
        ClipLanes.Ensure(from, 2);
        ClipLanes.PlayNewTake(from, 1, midi: false);   // the newer take plays, lane 1 greyed
        var undo = new UndoController();
        undo.Capture(project);
        var expectedBefore = (keep.Lane, take.Lane, from.Lanes.Select(l => l.Plays).ToArray());
        var toPlan = MediaDrop.PlanMove(project, take, from.Kind, 2, 0, 8, time);
        Check("clip move: to a track with no lanes plans lane 1 as a new lane", toPlan.Valid && toPlan.TrackIndex == 2 && toPlan.Lane == 0 && toPlan.NewLane && !toPlan.NewTrack);
        var moved = MediaDrop.ApplyMove(from, project.Tracks[2], take, toPlan.Lane, toPlan.StartSec, copy: false, removeEmptyLanes: true);
        Check("clip move: the clip is on the other track and a lane was created there", ReferenceEquals(moved, take) && project.Tracks[2].AudioClips.Contains(take) && !from.AudioClips.Contains(take) && project.Tracks[2].Lanes.Count == 1 && Near(take.StartSec, 8));
        Check("clip move: the source lane that was playing is removed and the older take plays again (nothing goes silent)",
            from.Lanes.Count == 1 && ClipLanes.Plays(from, 0) && ClipLanes.Audible(from, keep));

        var snapshot = undo.Snapshot(project);
        Check("clip move: one undo restores the clip, the lanes and the take state", undo.TryUndo(snapshot, out var target)
            && undo.Restore(target, project) is { } back
            && back.Tracks[1].AudioClips.Count == 2 && back.Tracks[2].AudioClips.Count == 0 && back.Tracks[2].Lanes.Count == 0
            && back.Tracks[1].AudioClips.Single(c => c.Name == "A").Lane == expectedBefore.Item1 && back.Tracks[1].AudioClips.Single(c => c.Name == "B").Lane == expectedBefore.Item2
            && back.Tracks[1].Lanes.Select(l => l.Plays).SequenceEqual(expectedBefore.Item3));

        // ---- Ctrl+drag copies ----
        {
            var song = DropSong();
            var t = song.Tracks[0];
            var a = MoveClip("A", 0, 4, 0);
            t.AudioClips.Add(a);
            ClipLanes.Ensure(t, 1);
            var plan = MediaDrop.PlanMove(song, a, t.Kind, 1, 0, 6, time, copy: true);
            Check("clip copy: the ghost's name says it is a copy", plan.Clips.Count == 1 && plan.Clips[0].Item.Name == "A (copy)");
            var copy = MediaDrop.ApplyMove(t, song.Tracks[1], a, plan.Lane, plan.StartSec, copy: true, removeEmptyLanes: true);
            Check("clip copy: the original stays, a new clip appears on the other track", !ReferenceEquals(copy, a) && t.AudioClips.Single() == a && a.Lane == 0 && Near(a.StartSec, 0)
                && song.Tracks[1].AudioClips.Single() == copy && copy.Id != a.Id && Near(copy.StartSec, 6) && copy.File == a.File);
            var overOwn = MediaDrop.PlanMove(song, a, t.Kind, 0, 0, 1, time, copy: true);
            Check("clip copy: a copy dropped over its original goes to a new lane (the original still blocks)", overOwn.Lane == 1 && overOwn.NewLane);
        }

        // ---- below the last track: a new track of a fitting kind ----
        {
            var song = DropSong(2);
            var audio = MoveClip("A", 0, 4, 0);
            song.Tracks[0].AudioClips.Add(audio);
            var midi = MoveClip("M", 0, 2, 0, midi: true);
            Check("clip move: an audio clip below the last track plans a new audio track", MediaDrop.PlanMove(song, audio, TrackKind.Guitar, 2, 0, 0, time) is { NewTrack: true, NewTrackKind: TrackKind.Audio, Lane: 0 });
            Check("clip move: a MIDI clip from a drum track below the last track plans a new audio track", MediaDrop.PlanMove(song, midi, TrackKind.Drums, 2, 0, 0, time).NewTrackKind == TrackKind.Audio);
            var bus = DropSong(2);
            bus.Tracks[1].BusSlot = 0;
            Check("clip move: a bus track takes no clips", !MediaDrop.PlanMove(bus, audio, TrackKind.Guitar, 1, 0, 0, time).Valid);
        }

        // ---- armed track: lanes untouched ----
        {
            var t = new TrackModel { RecordArm = true };
            var a = MoveClip("A", 0, 4, 1);
            t.AudioClips.Add(a);
            ClipLanes.Ensure(t, 3);
            Check("empty lanes: an armed track keeps every lane", !ClipLanes.Compact(t) && t.Lanes.Count == 3 && a.Lane == 1);
            var song = DropSong(2);
            var armed = song.Tracks[0];
            armed.RecordArm = true;
            armed.AudioClips.Add(MoveClip("X", 0, 4, 0)); armed.AudioClips.Add(MoveClip("Y", 5, 1, 1));
            ClipLanes.Ensure(armed, 2);
            var y = armed.AudioClips[1];
            MediaDrop.ApplyMove(armed, song.Tracks[1], y, 0, 0, copy: false, removeEmptyLanes: true);
            Check("empty lanes: moving a clip off a recording-armed track leaves its lanes alone", armed.Lanes.Count == 2 && armed.AudioClips.Count == 1);
        }

        // ---- Compact: middle lanes close, play state is kept ----
        {
            var t = new TrackModel();
            t.AudioClips.Add(MoveClip("a", 0, 1, 0)); t.AudioClips.Add(MoveClip("c", 0, 1, 3)); t.AudioClips.Add(MoveClip("m", 2, 1, 4, midi: true));
            ClipLanes.Ensure(t, 6);
            t.Lanes[0].Plays = false; t.Lanes[3].Plays = true; t.Lanes[4].Plays = true;
            Check("empty lanes: lanes 2, 3 and 6 are removed and the clips renumbered", ClipLanes.Compact(t) && t.Lanes.Count == 3
                && t.AudioClips.Select(c => c.Lane).SequenceEqual(new[] { 0, 1, 2 }));
            Check("empty lanes: each remaining lane keeps its play / grey state", !t.Lanes[0].Plays && t.Lanes[1].Plays && t.Lanes[2].Plays);
            Check("empty lanes: nothing to remove changes nothing", !ClipLanes.Compact(t));
            var gone = new TrackModel();
            gone.AudioClips.Add(MoveClip("a", 0, 1, 0));
            ClipLanes.Ensure(gone, 1);
            gone.AudioClips.Clear();
            Check("empty lanes: a track whose last clip is gone keeps its row but loses its lanes", ClipLanes.Compact(gone) && gone.Lanes.Count == 0 && ClipLanes.Count(gone) == 0);
        }

        // ---- the setting ----
        Check("setting: removing empty clip lanes is on by default", new AppSettings().Timeline.AutoRemoveEmptyLanes);
        var changed = new AppSettings();
        var row = SettingsCatalog.Build(changed).FirstOrDefault(d => d.Key == "timeline.removeemptylanes");
        Check("setting: the Preferences row sits on Timeline & Tracks with search words", row is not null && row.Category == SettingsCatalog.Timeline && row.Title == "Remove empty clip lanes automatically" && row.Keywords.Contains("lanes"));
        row!.Set(false);
        Check("setting: the row writes and reads the property", !changed.Timeline.AutoRemoveEmptyLanes && row.Get() is false);

        // ---- a MIDI clip moved onto a drum track keeps playing, on the drum channel ----
        {
            var song = new SongProject { Tempo = 120 };
            song.Tracks.Add(new TrackModel { Name = "Keys", Kind = TrackKind.Keys, MidiChannel = 0, Measures = TabForge.Presets.TemplateFactory.Measures(4) });
            song.Tracks.Add(new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, Measures = TabForge.Presets.TemplateFactory.Measures(4) });
            var clip = MoveClip("Groove", 0, 2, 0, midi: true);
            song.Tracks[0].AudioClips.Add(clip);
            ClipLanes.Ensure(song.Tracks[0], 1);
            var plan = MediaDrop.PlanMove(song, clip, TrackKind.Keys, 1, 0, 0, SongQuarterMap.For(song));
            MediaDrop.ApplyMove(song.Tracks[0], song.Tracks[1], clip, plan.Lane, plan.StartSec, copy: false, removeEmptyLanes: true);
            var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions());
            var hits = timeline.Events.Where(e => (e.Status & 0xF0) == 0x90 && e.Data1 == 36).ToList();
            Check("clip move: a MIDI clip moved onto a drum track plays its notes on the drum channel", plan.Valid && hits.Count == 1 && hits[0].TrackIndex == 1 && (hits[0].Status & 0x0F) == 9);
        }
    }

    private static void TestClipMoveGhost()
    {
        var song = DropSong();
        var clip = MoveClip("Loop", 4, 4, 0);
        song.Tracks[0].AudioClips.Add(clip);
        ClipLanes.Ensure(song.Tracks[0], 1);
        var timeline = new TrackTimeline
        {
            Project = song, MeasureWidth = 30,
            BarStartSec = b => b * 2.0, BarOfSec = s => ((int)Math.Floor(s / 2), s / 2 - Math.Floor(s / 2)),
            Snap = new SnapSettings { Enabled = true, Grid = "Bar", ToGrid = true, ToItems = false, ToPlayhead = false, GridAtAnyDistance = true },
        };
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        var row = ArrangementPanel.DefaultTrackRowHeight;
        var press = new Point(timeline.XOfBar(2) + 10, gridTop + row + 10);
        var toTrack3 = new Point(timeline.XOfBar(4) + 10, gridTop + ArrangementPanel.RowTopOf(song, 2) + 10);   // track 3 (index 2): no lanes yet
        var preview = timeline.ClipGestures.SimulateMove(clip, 0, press, toTrack3);
        Check("clip ghost: dragged onto a track without lanes it shows a new-lane slot there, on the snapped bar",
            preview is { Valid: true } && preview.Plan.TrackIndex == 2 && preview.Plan.NewLane && preview.Slot is { } audioSlot
            && Near(audioSlot.Y, gridTop + ArrangementPanel.RowTopOf(song, 2))
            && Near(audioSlot.Height, ArrangementPanel.RowHeightOf(song, song.Tracks[2]))
            && preview.Block.Y >= audioSlot.Y && preview.Block.Bottom <= audioSlot.Bottom
            && Near(preview.Block.X, timeline.XOfBar(4)) && Near(preview.Plan.StartSec, 8));
        Check("clip ghost: the clip itself has not moved while dragging", Near(clip.StartSec, 4) && clip.Lane == 0 && song.Tracks[0].AudioClips.Count == 1);
        var again = timeline.ClipGestures.SimulateMove(clip, 0, press, new Point(toTrack3.X + 1, toTrack3.Y + 2));
        Check("clip ghost: staying on the same snapped target does not rebuild the ghost", ReferenceEquals(preview, again));
        Check("clip ghost: Alt places freely (no snap)", timeline.ClipGestures.SimulateMove(clip, 0, press, new Point(toTrack3.X + 7, toTrack3.Y), alt: true) is { } free && !Near(free.Block.X, timeline.XOfBar(4)));
        Check("clip ghost: Ctrl shows it as a copy", timeline.ClipGestures.SimulateMove(clip, 0, press, toTrack3, copy: true) is { } copy && copy.Label == "Loop (copy)");
        var below = timeline.ClipGestures.SimulateMove(clip, 0, press, new Point(toTrack3.X, gridTop + ArrangementPanel.RowsHeight(song) + 20));
        Check("clip ghost: below the last track it shows a new track", below is { Valid: true } && below.Plan.NewTrack && below.Slot is not null);
        Check("clip ghost: the ruler is not a place for a clip", timeline.ClipGestures.SimulateMove(clip, 0, press, new Point(toTrack3.X, 4)) is { Valid: false });
        timeline.ClipGestures.Cancel();

        // An audio clip stays a lane move over an instrument's notation row, but that preview zone differs from lane 1.
        song.Tracks[1].AudioClips.Add(MoveClip("Occupied", 4, 8, 0));
        ClipLanes.Ensure(song.Tracks[1], 1);
        var targetX = timeline.XOfBar(3) + 10;
        var targetRow = gridTop + ArrangementPanel.RowTopOf(song, 1);
        var notationMove = timeline.ClipGestures.SimulateMove(clip, 0, press, new Point(targetX, targetRow + 5));
        var notationSlot = notationMove?.Slot;
        var laneMove = timeline.ClipGestures.SimulateMove(clip, 0, press, new Point(targetX, timeline.LaneTop(1, 0) + 5));
        Check("clip ghost: same-time move from notation area to lane 1 refreshes the bounded lane preview",
            notationMove is { Valid: true, Plan.NewLane: true } && notationSlot is { } scoreRow
            && laneMove is { Valid: true, Plan.NewLane: true, Slot: { } clipLane }
            && !ReferenceEquals(notationMove, laneMove) && Near(scoreRow.Y, targetRow)
            && Near(scoreRow.Height, ArrangementPanel.RowHeightFor(song))
            && Near(clipLane.Y, timeline.LaneTop(1, 0)) && Near(clipLane.Height, ArrangementPanel.AudioLaneHeight)
            && laneMove.Block.Y >= clipLane.Y && laneMove.Block.Bottom <= clipLane.Bottom);
        timeline.ClipGestures.Cancel();
        song.Tracks[2].Kind = TrackKind.Audio;
        var emptyAudioTop = gridTop + ArrangementPanel.RowTopOf(song, 2);
        var emptyAudioMove = timeline.ClipGestures.SimulateMove(clip, 0, press, new Point(toTrack3.X, emptyAudioTop + 10));
        Check("clip ghost: first lane on an empty audio track stays in its compact row",
            emptyAudioMove is { Valid: true, Plan.NewLane: true } && emptyAudioMove.Slot is { } compactSlot
            && Near(compactSlot.Y, emptyAudioTop) && Near(compactSlot.Height, ArrangementPanel.RowHeightOf(song, song.Tracks[2]))
            && emptyAudioMove.Block.Y >= compactSlot.Y && emptyAudioMove.Block.Bottom <= compactSlot.Bottom);
        Check("clip ghost: Esc cancels (ghost gone, clip untouched)", timeline.ClipGestures.Cancel() && timeline.CurrentDropPreview is null && Near(clip.StartSec, 4) && !timeline.ClipGestures.Cancel());
    }
}
