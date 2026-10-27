using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the arrangement keeping up with the document after an undo or redo replaces its song: the Mute, Solo and Arm cells act on and draw from the
/// current song, the timeline shows new edits, and the selected clip is looked up again by id.
/// Tests: ArrangementView, ArrangementRowSource, TimelineInputController, TimelineView.
/// </summary>
public class ArrangementUndoRebindTests
{
    private static (Window Window, ArrangementView View, SongDocument Doc) Open(SongProject? song = null)
    {
        var doc = new SongDocument(() => null) { Project = song ?? DemoSongFactory.Create() };
        var view = new ArrangementView(doc.Project, doc) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 400, Content = view };
        window.Show();
        return (window, view, doc);
    }

    private static void Click(Window w, ArrangementView v, int row, TrackRowCells.Cell cell)
    {
        var at = ArrangementClicks.CellInWindow(v, row, cell).Center;
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
    }

    [AvaloniaFact]
    public void UndoThenMuteTogglesAndGlowsImmediately()
    {
        var (w, v, doc) = Open();
        Click(w, v, 1, TrackRowCells.Cell.Mute);
        Assert.True(doc.Project.Tracks[1].Mute);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.False(doc.Project.Tracks[1].Mute);
        Click(w, v, 1, TrackRowCells.Cell.Mute);
        Assert.True(doc.Project.Tracks[1].Mute);
        Assert.Same(doc.Project, v.Header.Song);
        Assert.Same(doc.Project, v.Timeline.Song);
        w.Close();
    }

    [AvaloniaFact]
    public void UndoThenEditNoteTimelineShowsNewBlocks()
    {
        var (w, v, doc) = Open();
        var count = doc.Project.Tracks.Count;
        DocumentEdits.Run(doc, p => { p.Tracks.Add(NewTrackFactory.CreateTrack(p, TrackKind.Audio)); return true; });
        Assert.Equal(count + 1, v.Timeline.RowCount);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Equal(count, v.Timeline.RowCount);
        DocumentEdits.Run(doc, p => { p.Tracks.Add(NewTrackFactory.CreateTrack(p, TrackKind.Audio)); return true; });
        Assert.Equal(count + 1, v.Timeline.RowCount);
        Assert.Same(doc.Project, v.Timeline.Song);
        w.Close();
    }

    [AvaloniaFact]
    public void UndoThenEditCellChangesTheBarActivity()
    {
        var (w, v, doc) = Open();
        DocumentEdits.Run(doc, p => { p.Tracks[0].Measures[0].Cells.Clear(); return true; });
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.True(v.Timeline.MasterLaneActivity[0][1], "Bar 2 plays after the undo.");
        DocumentEdits.Run(doc, p => { p.Tracks[0].Measures[1].Cells.Clear(); return true; });
        Assert.Same(doc.Project, v.Timeline.Song);
        Assert.False(v.Timeline.MasterLaneActivity[0][1], "The edit after the undo reaches the timeline.");
        w.Close();
    }

    [AvaloniaFact]
    public void UndoThenMoveClipEditReachesTheCurrentSong()
    {
        var (w, v, doc) = Open(WithAudioClip(out var track, out var clip));
        DocumentEdits.Run(doc, p => { p.Tracks[0].Name = "renamed"; return true; });
        Assert.NotNull(DocumentEdits.Undo(doc));
        v.Timeline.SelectedClip = new TimelineClipSelection(doc.Project.Tracks.First(t => t.Id == track.Id), doc.Project.Tracks.First(t => t.Id == track.Id).AudioClips.First(c => c.Id == clip.Id));   // the step's before-selection: undo shows it again, looked up by id
        DocumentEdits.Run(doc, p => { p.Tracks.First(t => t.Id == track.Id).AudioClips.First(c => c.Id == clip.Id).StartSec = 1.5; return true; });
        var moved = doc.Project.Tracks.First(t => t.Id == track.Id).AudioClips.First(c => c.Id == clip.Id);
        Assert.Equal(1.5, moved.StartSec);
        Assert.Same(doc.Project, v.Timeline.Song);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Equal(0, doc.Project.Tracks.First(t => t.Id == track.Id).AudioClips.First(c => c.Id == clip.Id).StartSec);
        Assert.NotNull(v.Timeline.SelectedClip);
        w.Close();
    }

    [AvaloniaFact]
    public void TimelineSongIsDocumentProjectAfterUndoRedoUndo()
    {
        var (w, v, doc) = Open();
        Click(w, v, 1, TrackRowCells.Cell.Mute);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Same(doc.Project, v.Timeline.Song);
        Assert.NotNull(DocumentEdits.Redo(doc));
        Assert.Same(doc.Project, v.Timeline.Song);
        Assert.Same(doc.Project, v.Header.Song);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Same(doc.Project, v.Timeline.Song);
        Assert.Same(doc.Project, v.Header.Song);
        w.Close();
    }

    [AvaloniaFact]
    public void SoloAndArmAfterUndoActOnCurrentProject()
    {
        var (w, v, doc) = Open();
        Click(w, v, 1, TrackRowCells.Cell.Solo);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Click(w, v, 1, TrackRowCells.Cell.Solo);
        Assert.True(doc.Project.Tracks[1].Solo);
        Click(w, v, 1, TrackRowCells.Cell.Arm);
        Assert.True(doc.Project.Tracks[1].RecordArm);
        Assert.Same(doc.Project, v.Header.Song);
        w.Close();
    }

    private static SongProject WithAudioClip(out TrackModel track, out AudioClip clip)
    {
        var song = DemoSongFactory.Create();
        track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        clip = new AudioClip { Name = "take", StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2 };
        track.AudioClips.Add(clip);
        song.Tracks.Add(track);
        return song;
    }

    [AvaloniaFact]
    public void SelectedClipResolvedByIdAfterUndo()
    {
        var (w, v, doc) = Open(WithAudioClip(out var track, out var clip));
        v.Timeline.SelectedClip = new TimelineClipSelection(track, clip);
        DocumentEdits.Run(doc, p => { p.Tracks[0].Name = "renamed"; return true; });
        Assert.NotNull(DocumentEdits.Undo(doc));
        var current = doc.Project.Tracks.First(t => t.Id == track.Id);
        var selected = v.Timeline.SelectedClip;
        Assert.NotNull(selected);
        Assert.Same(current, selected!.Track);
        Assert.Same(current.AudioClips.First(c => c.Id == clip.Id), selected.Clip);
        w.Close();
    }

    [AvaloniaFact]
    public void SelectedClipClearedWhenRestoreRemovesIt()
    {
        var (w, v, doc) = Open();
        TrackModel? track = null;
        AudioClip? clip = null;
        DocumentEdits.Run(doc, p =>
        {
            track = NewTrackFactory.CreateTrack(p, TrackKind.Audio);
            clip = new AudioClip { Name = "take", SourceLengthSec = 2, FileLengthSec = 2 };
            track.AudioClips.Add(clip);
            p.Tracks.Add(track);
            return true;
        });
        v.Timeline.SelectedClip = new TimelineClipSelection(track!, clip!);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Null(v.Timeline.SelectedClip);
        w.Close();
    }
}
