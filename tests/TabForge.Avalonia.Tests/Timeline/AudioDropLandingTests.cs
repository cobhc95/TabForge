using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: where a file dragged in from outside lands in time (snapping, repeat pass), the readout, the file length probe, the ghost clips the preview draws (lane, fold, stacking)
//   and the track-list refresh after a drop that added a track.
// Does not own: the plan itself (AudioDropPlannerTests) or the preview plan alone (AudioDropPreviewTests).
// Tests: this file.
public class AudioDropLandingTests
{
    private static readonly UiColor Fallback = new(255, 1, 2, 3);

    private static List<(string File, double LengthSec)> Files(params (string, double)[] files) => files.ToList();

    private static InstrumentFretNumberTests.RecordingDrawContext Draw(TimelineView view, AudioFileDropPreview preview)
    {
        view.FileDropPreview = preview;
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        TimelineAudioClips.DrawFileDropPreview(draw, _ => Fallback, view, 800);
        return draw;
    }

    [AvaloniaFact]
    public void TheStartSnapsToTheGridUnlessAltIsHeldAndPreviewAndDropShareIt()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var view = new TimelineView(song) { Snap = new SnapSettings { Enabled = true, ToItems = false, ToPlayhead = false, Grid = "1/4", GridAtAnyDistance = true } };
        var x = view.BarX(2) + (view.BarX(3) - view.BarX(2)) * 0.3;

        var snapped = AudioDropLanding.StartSec(view, x, altHeld: false);
        var free = AudioDropLanding.StartSec(view, x, altHeld: true);

        var beats = MusicTime.BarSlots(song, 2) / (double)MusicTime.SlotsPerQuarter;
        var fraction = (view.XOfSeconds(snapped) - view.BarX(2)) / (view.BarX(3) - view.BarX(2)) * beats;
        Assert.Equal(Math.Round(fraction), fraction, 4);
        Assert.Equal(view.SecondsOfX(x), free, 6);
        Assert.NotEqual(free, snapped, 6);
        // The preview and the dropped plan read the same second, and the ghost is as wide as the file is long.
        var files = Files((@"C:\a\one.wav", 2.5));
        var preview = AudioDropPreviewPlanner.Plan(song, AudioDropTarget.On(track), snapped, files)!;
        var plan = AudioDropPlanner.Plan(new[] { @"C:\a\one.wav" }, AudioDropTarget.On(track), snapped, _ => 2.5);
        Assert.Equal(plan.StartSec, preview.StartSec);
        Assert.Equal(plan.EndSec, preview.EndSec, 9);
        Assert.Equal(2.5, preview.EndSec - preview.StartSec, 9);
    }

    [Fact]
    public void TheReadoutNamesTheBarBeatAndTime()
    {
        var song = DemoSongFactory.Create();
        Assert.Equal("Bar 1.1 \u00b7 0:00.0", AudioDropLanding.Readout(song, 0));
        var text = AudioDropLanding.Readout(song, AudioClipTimelineRules.SecondsAtBar(song, 16, 0.5));
        Assert.Matches(@"^Bar 17\.\d · \d+:\d\d\.\d$", text);
    }

    [Fact]
    public void AMidiFileLengthComesFromTheClipImportAndItPreviewsAsAGhost()
    {
        // One note, a quarter long at 120 bpm (480 ticks of 480 per quarter): half a second.
        byte[] smf =
        {
            0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0,
            0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 13,
            0x00, 0x90, 0x3C, 0x64, 0x83, 0x60, 0x80, 0x3C, 0x00, 0x00, 0xFF, 0x2F, 0x00,
        };
        var path = Path.Combine(Path.GetTempPath(), "tf-drop-" + Guid.NewGuid().ToString("N") + ".mid");
        File.WriteAllBytes(path, smf);
        try
        {
            var length = AudioDropLanding.LengthOf(path, _ => throw new InvalidOperationException("a MIDI file is not read as audio"));
            Assert.Equal(0.5, length, 3);
            var song = DemoSongFactory.Create();
            var preview = AudioDropPreviewPlanner.Plan(song, AudioDropTarget.NewTrack, 4, Files((path, length)))!;
            Assert.Equal(0.5, preview.EndSec - preview.StartSec, 3);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), preview.Label);
            Assert.False(preview.Estimated);
        }
        finally { File.Delete(path); }
        Assert.Equal(0, AudioDropLanding.LengthOf(Path.Combine(Path.GetTempPath(), "tf-missing-" + Guid.NewGuid().ToString("N") + ".mid"), _ => 9));
        Assert.Equal(7, AudioDropLanding.LengthOf(@"C:\a\x.wav", _ => 7));
    }

    [AvaloniaFact]
    public void SeveralFilesStackAsConsecutiveGhostsOnTheLaneTheyLandOn()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var view = new TimelineView(song);
        var preview = AudioDropPreviewPlanner.Plan(song, AudioDropTarget.On(track), 1, Files((@"C:\a\one.wav", 2), (@"C:\a\two.wav", 3)))!;

        Assert.Equal(2, preview.Parts!.Count);
        Assert.Equal(1, preview.Parts[0].StartSec);
        Assert.Equal(preview.Parts[0].EndSec, preview.Parts[1].StartSec, 9);
        Assert.Equal(6, preview.Parts[1].EndSec, 9);
        var draw = Draw(view, preview);
        var fill = view.TrackColourOf(track, Fallback).WithOpacity(0.38);
        var boxes = draw.Rects.Where(r => r.Fill == fill).ToList();
        Assert.Equal(2, boxes.Count);
        Assert.Equal(view.XOfSeconds(1), boxes[0].X, 6);
        Assert.Equal(view.XOfSeconds(3), boxes[1].X, 6);
        Assert.Equal(boxes[0].Y, boxes[1].Y, 6);
        Assert.Contains(draw.Texts, t => t.Text == "one");
        Assert.Contains(draw.Texts, t => t.Text == "two");
        Assert.Contains(draw.Texts, t => t.Text == preview.Readout);
    }

    [AvaloniaFact]
    public void TheGhostFoldsOverARepeatLikeALandedClip()
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var project = document.Project;
        project.Tracks[0].Measures[1].RepeatStart = true;
        project.Tracks[0].Measures[2].RepeatEnd = true;
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Add(track);
        var view = new TimelineView(project) { Snap = new SnapSettings { Enabled = false } };
        var bar = AudioClipTimelineRules.SecondsAtBar(project, 1, 0);
        var preview = AudioDropPreviewPlanner.Plan(project, AudioDropTarget.On(track), 0.5 * bar, Files((@"C:\a\long.wav", 5 * bar)))!;
        var ghost = preview.Parts![0];

        var slices = TimelineAudioClips.FoldOf(view, ghost)!;
        Assert.Contains(slices, s => s.IsPart);
        var draw = Draw(view, preview);
        var fill = view.TrackColourOf(track, Fallback).WithOpacity(0.38);
        var boxes = draw.Rects.Where(r => r.Fill == fill).ToList();
        Assert.Equal(slices.Count, boxes.Count);
        Assert.Contains(boxes, b => Math.Abs(b.X - view.BarX(1)) < 0.01);
    }

    [AvaloniaFact]
    public void TheStripDropPreviewsAGhostOnTheStripAtTheDropTime()
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var arrangement = new ArrangementView(document.Project, document);
        var window = new Window { Width = 1800, Height = 900, Content = arrangement };
        window.Show();
        window.UpdateLayout();
        var preview = AudioDropPreviewPlanner.Plan(document.Project, AudioDropTarget.NewTrack, 2, Files((@"C:\one.wav", 2)))!;

        var ghost = MainWindowAudioDrop.StripGhost(arrangement, preview);

        Assert.True(preview.NewTrack);
        Assert.NotNull(ghost);
        var timeline = arrangement.Timeline;
        var expected = timeline.TranslatePoint(new Point(timeline.XOfSeconds(2), 0), arrangement.AddTrackBar)!.Value.X;
        Assert.Equal(expected, ghost!.Value.X1, 6);
        Assert.Equal(timeline.XOfSeconds(4) - timeline.XOfSeconds(2), ghost.Value.X2 - ghost.Value.X1, 6);
        Assert.Equal("one", ghost.Value.Label);
        Assert.Equal(preview.Readout, ghost.Value.Readout);
        arrangement.AddTrackBar.DropGhost = ghost;
        arrangement.AddTrackBar.DropGhost = null;
        window.Close();
    }

    [AvaloniaFact]
    public void ADropThatAddedATrackReloadsTheRowsAtOnce()
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        document.MarkClean();
        var arrangement = new ArrangementView(document.Project, document);
        var window = new Window { Width = 1800, Height = 900, Content = arrangement };
        window.Show();
        window.UpdateLayout();
        var before = arrangement.Timeline.RowCount;
        var rowsChanged = 0;
        arrangement.TrackRowsCountChanged += () => rowsChanged++;
        // The track lands without a timeline mark (nothing reloaded the rows yet): the post-edit refresh alone must bring the new row in.
        var track = NewTrackFactory.CreateTrack(document.Project, TrackKind.Audio);
        track.AudioClips.Add(new AudioClip { File = "x.wav", Name = "x", StartSec = 0, SourceLengthSec = 3, FileLengthSec = 3 });
        document.Project.Tracks.Add(track);

        MainWindowClipRefresh.AfterEdit(arrangement, document, null, null);

        Assert.Equal(before + 1, arrangement.Timeline.RowCount);
        Assert.True(rowsChanged > 0, "the track list was not re-fitted");
        arrangement.ShowTrack(track);
        Assert.Equal(document.Project.Tracks.Count - 1, arrangement.Timeline.SelectedRow);
        window.Close();
    }
}
