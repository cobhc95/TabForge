using System.IO;
using System.Windows;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Clip edges (part of <see cref="SelfTest"/>): an edge can always be dragged back out to the media and past it (the clip loops), the song grows to hold
/// the clip after every crop and extension, the left edge keeps the audio in place, snapping follows the button, loops survive save and reload, and
/// the empty bars at the end go when clips shrink (setting on) without touching notation, sections, mix points or repeats; undo restores clip and bars.
/// </summary>
public static partial class SelfTest
{
    private sealed class EdgeClipHost : IClipHost
    {
        public AppSettings Settings { get; } = new();
        public AudioClip? SelectedClip { get; set; }
        public bool IsShown(DocumentSession document) => true;
        public bool CancelClipDrag() => false;
        public void SetStatus(string text) { }
        public void CheckpointUndo() { }
        public void SyncAudioEngine() { }
        public void RefreshTracks() { }
        public void RefreshArrangement() { }
        public void RefreshAfterSongGrew() { }
        public void InvalidateScoreLayout() { }
        public void UpdateTitle() { }
        public void ShowNewTrack(int index) { }
        public bool ShowClipProperties(AudioClip clip) => false;
        public DropItem MeasureDroppedFile(string file, DropItemKind kind, bool transient, MediaContext media) => throw new NotSupportedException();
        public DropItem ReadDroppedMidi(string file, bool transient) => throw new NotSupportedException();
    }

    private static int Bars(SongProject song) => song.Tracks.Max(t => t.Measures.Count);

    private static void TestClipEdgesAndLoops()
    {
        // ---- import into a 100-bar song (2 s bars): a 300 s file grows it to 150 bars; crop and extend both ways ----
        var song = DropSong(1, 100);
        var clip = new AudioClip { File = @"C:\Media\long.wav", Name = "long", StartSec = 0, SourceLengthSec = 300, FileLengthSec = 300 };
        song.Tracks[0].AudioClips.Add(clip);
        SongExtent.EnsureCoversClips(song);
        Check("clip edges: importing 300 s into 100 bars grows the song to 150", Bars(song) == 150, $"{Bars(song)} bars");
        foreach (var cropBar in new[] { 120, 80 })
        {
            ClipTrim.TrimEnd(clip, clip.StartSec, cropBar * 2.0);
            SongExtent.EnsureCoversClips(song);
            ClipTrim.TrimEnd(clip, clip.StartSec, 300);   // dragged back out to the media end
            SongExtent.EnsureCoversClips(song);
            Check($"clip edges: cropped to bar {cropBar}, the end drags back out to the full 150 bars", Math.Abs(clip.EndSec - 300) < 1e-9 && Bars(song) == 150 && !ClipLoop.Loops(clip),
                $"end {clip.EndSec}, {Bars(song)} bars");
        }
        ClipTrim.TrimEnd(clip, clip.StartSec, 400);
        var grown = SongExtent.EnsureCoversClips(song);
        var pieces = ClipLoop.Pieces(clip);
        Check("clip edges: dragged past the media the clip loops and the song grows to hold it",
            ClipLoop.Loops(clip) && grown.BarsAdded == 50 && Bars(song) == 200 && pieces.Count == 2
            && pieces[1].StartSec == 300 && pieces[1].OffsetSec == 0 && Math.Abs(pieces[1].SourceLengthSec - 100) < 1e-9,
            $"loops {ClipLoop.Loops(clip)}, +{grown.BarsAdded}, {Bars(song)} bars, {pieces.Count} pieces");

        // ---- a recording (no media length) loops what it held when the end was grabbed ----
        var take = new AudioClip { File = @"C:\Media\take.wav", StartSec = 0, SourceLengthSec = 4, FileLengthSec = 0 };
        take.FileLengthSec = ClipLoop.MediaLengthSec(take);
        ClipTrim.TrimEnd(take, 0, 10);
        Check("clip edges: a clip with no known media length loops the part it held", ClipLoop.Pieces(take).Count == 3 && Math.Abs(ClipLoop.Pieces(take)[2].SourceLengthSec - 2) < 1e-9);

        // ---- left edge: the audio stays in place (offset grows, start moves, the end does not) ----
        var left = new AudioClip { File = @"C:\Media\a.wav", StartSec = 10, OffsetSec = 2, SourceLengthSec = 20, FileLengthSec = 60, Speed = 2 };
        var origin = (left.StartSec, left.OffsetSec, left.SourceLengthSec);
        var endBefore = left.EndSec;
        ClipTrim.TrimStart(left, origin, 12);   // 2 s of timeline = 4 s of file
        Check("clip edges: trimming the left edge keeps the audio in place",
            Math.Abs(left.StartSec - 12) < 1e-9 && Math.Abs(left.OffsetSec - 6) < 1e-9 && Math.Abs(left.EndSec - endBefore) < 1e-9, $"start {left.StartSec}, offset {left.OffsetSec}, end {left.EndSec}");
        ClipTrim.TrimStart(left, origin, 3);   // back out: stops where the media starts (offset 0)
        Check("clip edges: the left edge drags back out to the start of the media", Math.Abs(left.OffsetSec) < 1e-9 && Math.Abs(left.EndSec - endBefore) < 1e-9, $"offset {left.OffsetSec}");

        // ---- split of a looping clip keeps the second part's offset inside the media ----
        var track = song.Tracks[0];
        var second = ClipSplitGlue.Split(track, clip, 350);
        Check("clip edges: splitting inside the second pass starts the second part inside the media", second is not null && second.OffsetSec >= 0 && second.OffsetSec < 300 && Math.Abs(second.OffsetSec - 50) < 1e-9, $"offset {second?.OffsetSec}");

        // ---- snapping: on snaps to beats, off leaves the edge free ----
        var timelineSong = DropSong(1, 8);
        var timeline = new TrackTimeline
        {
            Project = timelineSong, MeasureWidth = 30,
            BarStartSec = b => b * 2.0, BarOfSec = s => ((int)Math.Floor(s / 2), s / 2 - Math.Floor(s / 2)),
        };
        timeline.Snap = new SnapSettings { Enabled = true, Grid = "1/4", ToItems = false, ToPlayhead = false, GridAtAnyDistance = true };
        var on = timeline.SnapSec(1.13, null, out _, altHeld: false);
        timeline.Snap = new SnapSettings { Enabled = false, Grid = "1/4" };
        var off = timeline.SnapSec(1.13, null, out _, altHeld: false);
        Check("clip edges: snap on puts an edge on a beat, snap off leaves it free", Math.Abs(on - 1.0) < 1e-6 && Math.Abs(off - 1.13) < 1e-9, $"on {on}, off {off}");
        Check("clip edges: snapping is on out of the box", new SnapSettings().Enabled);

        // ---- save and reopen: the offsets, lengths and the loop survive ----
        var path = Path.Combine(Path.GetTempPath(), $"tf-edges-{Guid.NewGuid():N}.tforge");
        try
        {
            ProjectService.Save(path, song);
            var reopened = ProjectService.Load(path);
            var back = reopened.Tracks[0].AudioClips.First(c => c.Name == "long");
            Check("clip edges: offset, length and loop survive save and reopen",
                Math.Abs(back.SourceLengthSec - clip.SourceLengthSec) < 1e-9 && back.FileLengthSec == 300 && ClipLoop.Pieces(back).Count == ClipLoop.Pieces(clip).Count
                && reopened.Tracks[0].AudioClips.Any(c => Math.Abs(c.OffsetSec - second!.OffsetSec) < 1e-9),
                $"length {back.SourceLengthSec}, file {back.FileLengthSec}");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static void TestTrimEmptyBars()
    {
        static (DocumentSession Doc, ClipEditController Clips, EdgeClipHost Host, AudioClip Clip) Setup(int bars = 20)
        {
            var song = DropSong(2, bars);
            var clip = new AudioClip { File = @"C:\Media\x.wav", Name = "x", StartSec = 0, SourceLengthSec = 20, FileLengthSec = 20 };   // 10 bars at 2 s
            song.Tracks[0].AudioClips.Add(clip);
            var host = new EdgeClipHost();
            var doc = new DocumentSession(new PlaybackEngine(new NullMidiOutput())) { Project = song };
            return (doc, new ClipEditController(host, new TrackController()), host, clip);
        }

        // delete the last clip: the empty bars at the end go; one undo brings back clip and bars
        var (doc, clips, host, clip) = Setup();
        clips.Remove(doc, doc.Project.Tracks[0], clip, null, true);
        Check("trim bars: deleting the last clip removes the empty bars at the end", Bars(doc.Project) == 1, $"{Bars(doc.Project)} bars");
        var undone = doc.Undo.TryUndo(doc.Undo.Snapshot(doc.Project), out var target) ? doc.Undo.Restore(target) : null;
        Check("trim bars: undo restores the clip and the bars in one step", undone is not null && Bars(undone) == 20 && undone.Tracks[0].AudioClips.Count == 1, $"{(undone is null ? -1 : Bars(undone))} bars");
        var redone = doc.Undo.TryRedo(doc.Undo.Snapshot(undone!), out var again) ? doc.Undo.Restore(again) : null;
        Check("trim bars: redo removes them again", redone is not null && Bars(redone) == 1 && redone.Tracks[0].AudioClips.Count == 0, $"{(redone is null ? -1 : Bars(redone))} bars");

        // cropping the clip shrinks to the clip's new end (the clip starts at 0, 8 s = 4 bars)
        (doc, clips, host, clip) = Setup();
        clips.BeginClipGesture(doc);
        ClipTrim.TrimEnd(clip, 0, 8);
        clips.FinishClipGesture(doc);
        Check("trim bars: cropping the clip shrinks the song to the clip's new end", Bars(doc.Project) == 4, $"{Bars(doc.Project)} bars");

        // extending a clip never removes the empty bars the user left at the end
        (doc, clips, host, clip) = Setup(30);
        clips.BeginClipGesture(doc);
        ClipTrim.TrimEnd(clip, 0, 30);
        clips.FinishClipGesture(doc);
        Check("trim bars: extending a clip keeps the empty bars after it", Bars(doc.Project) == 30, $"{Bars(doc.Project)} bars");

        // setting off: nothing shrinks
        (doc, clips, host, clip) = Setup();
        host.Settings.Editing.TrimEmptyBarsAtEnd = false;
        clips.Remove(doc, doc.Project.Tracks[0], clip, null, true);
        Check("trim bars: with the setting off nothing shrinks", Bars(doc.Project) == 20 && new EditingSettings().TrimEmptyBarsAtEnd, $"{Bars(doc.Project)} bars");

        // notation, sections, mix points and repeats past the clip are kept
        foreach (var kind in new[] { "notation", "section", "mix", "repeat", "other clip" })
        {
            (doc, clips, host, clip) = Setup();
            var p = doc.Project;
            switch (kind)
            {
                case "notation": p.Tracks[1].Measures[14].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3 }); break;
                case "section": p.Markers.Add(new MarkerModel { MeasureIndex = 14, Title = "Outro" }); break;
                case "mix": p.Tracks[1].Measures[14].Cells[0].Mix = new MixChange { Volume = 8 }; break;
                case "repeat": p.Tracks[0].Measures[14].RepeatEnd = true; break;
                case "other clip": p.Tracks[1].AudioClips.Add(new AudioClip { File = @"C:\Media\y.wav", StartSec = 12, SourceLengthSec = 2, FileLengthSec = 2 }); break;
            }
            clips.Remove(doc, p.Tracks[0], clip, null, true);
            var expected = kind == "other clip" ? 7 : 15;   // the other clip ends at 14 s = inside bar 7
            Check($"trim bars: {kind} past the deleted clip's end keeps its bar and the ones before it", Bars(p) == expected, $"{Bars(p)} bars");
        }
    }
}
