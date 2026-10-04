using System.Windows;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Dragging a MIDI clip (a take on an instrument track's lane, or the converted clip on an audio track) along its lane, to another lane
/// and to another track: the release asks for a move (never the notation row) and the moved clip exists where the plan put it.
/// </summary>
public static partial class SelfTest
{
    private static void TestMidiClipMoves()
    {
        foreach (var audio in new[] { false, true })
        {
            var kind = audio ? "converted clip on an audio track" : "MIDI take on an instrument lane";
            foreach (var (what, toTrack, toLane) in new[] { ("same lane", 0, 0), ("another lane", 0, 1), ("another track", 2, 0) })
            {
                var song = DropSong();
                if (audio) foreach (var t in song.Tracks) t.Kind = TrackKind.Audio;
                var clip = MoveClip("Midi", 4, 4, 0, midi: true);
                song.Tracks[0].AudioClips.Add(clip);
                song.Tracks[0].AudioClips.Add(MoveClip("Other", 20, 2, 1, midi: true));   // a second lane to move to
                ClipLanes.Ensure(song.Tracks[0], 2);
                song.Tracks[2].AudioClips.Add(MoveClip("There", 20, 2, 0, midi: true));
                ClipLanes.Ensure(song.Tracks[2], 1);
                var timeline = new TrackTimeline
                {
                    Project = song, MeasureWidth = 30,
                    BarStartSec = b => b * 2.0, BarOfSec = s => ((int)Math.Floor(s / 2), s / 2 - Math.Floor(s / 2)),
                    Snap = new SnapSettings { Enabled = true, Grid = "Bar", ToGrid = true, ToItems = false, ToPlayhead = false, GridAtAnyDistance = true },
                };
                MediaDropPlan? requested = null; var toNotation = false;
                timeline.ClipGestures.ClipMoveRequested += (_, _, plan, _) => requested = plan;
                timeline.ClipGestures.MidiClipToNotation += (_, _, _) => toNotation = true;
                var press = new Point(timeline.XOfBar(2) + 10, timeline.LaneTop(0, 0) + 10);
                var to = new Point(timeline.XOfBar(5) + 10, timeline.LaneTop(toTrack, toLane) + 10);
                timeline.ClipGestures.SimulateMove(clip, 0, press, to);
                timeline.ClipGestures.MouseUp();
                var ok = requested is { Valid: true } && !toNotation;
                if (ok)
                {
                    var moved = MediaDrop.ApplyMove(song.Tracks[0], song.Tracks[requested!.TrackIndex], clip, requested.Lane, requested.StartSec, copy: false, removeEmptyLanes: true);
                    ok = song.Tracks[toTrack].AudioClips.Contains(moved) && Near(moved.StartSec, 10) && moved.IsMidi;
                }
                Check($"MIDI clip move ({kind}, {what}): a move, not the notation row, and the clip lands there", ok,
                    $"plan {requested?.TrackIndex}/{requested?.Lane}/{requested?.StartSec} valid {requested?.Valid} problem {requested?.Problem}, notation {toNotation}");
            }
        }
        // A press near the clip's top edge with a little upward jitter (or into a group header above an audio track) stays a move.
        foreach (var audio in new[] { false, true })
        {
            var song = DropSong();
            if (audio) song.Tracks[1].Kind = TrackKind.Audio;
            song.Mixer.ShowGroupsInTrackList = true;
            var clip = MoveClip("Midi", 4, 4, 0, midi: true);
            song.Tracks[1].AudioClips.Add(clip);
            ClipLanes.Ensure(song.Tracks[1], 1);
            var timeline = new TrackTimeline { Project = song, MeasureWidth = 30, BarStartSec = b => b * 2.0, BarOfSec = s => ((int)Math.Floor(s / 2), s / 2 - Math.Floor(s / 2)) };
            var top = timeline.LaneTop(1, 0);
            var press = new Point(timeline.XOfSec(5), top + 4);
            timeline.ClipGestures.SimulateMove(clip, 1, press, new Point(timeline.XOfSec(9), top - 6));
            Check($"MIDI clip move ({(audio ? "audio track" : "instrument lane")}): grabbed at the top edge and nudged up, it stays a move on its lane",
                timeline.ClipGestures.DropTarget is null && timeline.ClipGestures.SimulatedMovePlan is { Valid: true, TrackIndex: 1, Lane: 0 }, $"target {timeline.ClipGestures.DropTarget}, plan {timeline.ClipGestures.SimulatedMovePlan?.TrackIndex}/{timeline.ClipGestures.SimulatedMovePlan?.Lane}");
            if (!audio)
            {
                timeline.ClipGestures.SimulateMove(clip, 1, press, new Point(timeline.XOfSec(9), top - ArrangementPanel.NotationHeightOf(song, song.Tracks[1]) / 2 - (ArrangementPanel.AudioLaneHeight / 2 - 4)));   // the clip's centre mid-row
                Check("MIDI clip move: dragged well into its notation row it is still written into the score", timeline.ClipGestures.DropTarget is { NotationRow: true, Track: 1 });
            }
            timeline.ClipGestures.Cancel();
        }
        RunInWindowFixture((window, _) =>
        {
            foreach (var audio in new[] { false, true })
                foreach (var (what, toTrack, toLane) in new[] { ("same lane", 0, 0), ("another lane", 0, 1), ("another track", 2, 0) })
                {
                    var song = DropSong();
                    if (audio) foreach (var t in song.Tracks) t.Kind = TrackKind.Audio;
                    var clip = MoveClip("Midi", 4, 4, 0, midi: true);
                    song.Tracks[0].AudioClips.Add(clip);
                    song.Tracks[0].AudioClips.Add(MoveClip("Other", 20, 2, 1, midi: true));
                    ClipLanes.Ensure(song.Tracks[0], 2);
                    song.Tracks[2].AudioClips.Add(MoveClip("There", 20, 2, 0, midi: true));
                    ClipLanes.Ensure(song.Tracks[2], 1);
                    song.Mixer.ShowGroupsInTrackList = true;
                    song.Tracks[0].RecordArm = !audio;   // a just-recorded take: the track is often still armed
                    var doc = DoOpen(window, song);
                    var before = DoHash(doc);
                    var timeline = LtField<ArrangementPanel>(window, "Arrangement")!.TimelineForTest;
                    var press = new Point(timeline.XOfSec(5), timeline.LaneTop(0, 0) + 10);
                    timeline.ClipGestures.SimulateMove(clip, 0, press, new Point(timeline.XOfSec(11), timeline.LaneTop(toTrack, toLane) + 10));
                    var plan = timeline.ClipGestures.SimulatedMovePlan!;
                    timeline.ClipGestures.MouseUp();   // the real release: the panel and the window route the move
                    var hit = timeline.ClipGestures.LaneHitAt(new Point(timeline.XOfSec(clip.StartSec + 0.5), timeline.LaneTop(toTrack, clip.Lane) + 10));
                    var drawn = MidiClipDrawn(timeline, clip, toTrack);
                    var there = doc.Project.Tracks[toTrack].AudioClips.Contains(clip) && Near(clip.StartSec, plan.StartSec) && clip.IsMidi && hit?.Clip == clip && drawn
                        && doc.Project.Tracks.Sum(t => t.AudioClips.Count(c => c.Name == "Midi")) == 1;
                    var where = string.Join(";", doc.Project.Tracks.Select(t => string.Join(",", t.AudioClips.Select(c => $"{c.Name}@{c.StartSec}/{c.Lane}"))));
                    CseUndo(window);
                    Check($"MIDI clip move in the window ({(audio ? "audio" : "instrument")} track, {what}): the clip exists there, is drawn, and undo restores",
                        there && DoHash(doc) == before, $"plan {plan.TrackIndex}/{plan.Lane}/{plan.StartSec}; drawn {drawn}; tracks {where}");
                }
        });
    }

    /// <summary>The clip's box differs from the empty lane beside it in a render of the timeline.</summary>
    private static bool MidiClipDrawn(TrackTimeline timeline, AudioClip clip, int track)
    {
        timeline.Measure(new Size(1400, 900)); timeline.Arrange(new Rect(0, 0, 1400, 900)); timeline.UpdateLayout();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(1400, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(timeline);
        int Pixel(double x, double y)
        {
            var px = new int[1];
            bmp.CopyPixels(new Int32Rect((int)Math.Clamp(x, 0, 1399), (int)Math.Clamp(y, 0, 899), 1, 1), px, 4, 0);
            return px[0];
        }
        var y = timeline.LaneTop(track, clip.Lane) + 4;   // the box's top edge row (no notes there)
        return Pixel(timeline.XOfSec(clip.StartSec + clip.LengthSec / 2), y) != Pixel(timeline.XOfSec(clip.EndSec) + 30, y);
    }
}
