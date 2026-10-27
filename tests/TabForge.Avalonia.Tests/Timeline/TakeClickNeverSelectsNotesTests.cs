using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: clicking a take or a MIDI clip in the timeline selects the clip only (never notes, bars or a score range), and a MIDI take click opens its track
//   in the piano roll while an audio take click leaves the pane alone.
// Does not own: the piano roll switch itself (PianoRollPaneSwitchTests) or the box selection (TimelineBoxSelectionTests).
// Tests: TimelineInputController, SelectionSync, EditorPaneSwitch.
public sealed class TakeClickNeverSelectsNotesTests
{
    private sealed record Rig(MainWindow Window, ScorePreviewView Score, ArrangementView Arrangement, PianoRollView Roll, AudioClip Clip, TrackModel Track);

    private static Rig Open(bool midi)
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new MainWindow(first: document);
        window.Show();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().Single();
        var project = document.Project;
        var track = project.Tracks[0];
        var span = BarRangeGaps.Span(project, 2, 3).GetValueOrDefault();
        var clip = new AudioClip { File = midi ? "" : "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From };
        if (midi) clip.Notes = new List<ClipNote> { new(0, 0.2, 60, 100) };
        track.AudioClips.Add(clip);
        arrangement.Timeline.Reload(project);
        window.UpdateLayout();
        return new Rig(window, window.GetLogicalDescendants().OfType<ScorePreviewView>().Single(), arrangement, window.GetLogicalDescendants().OfType<PianoRollView>().Single(), clip, track);
    }

    private static void ClickClip(Rig rig)
    {
        var view = rig.Arrangement.Timeline;
        var box = TimelineAudioClips.ClipBounds(view, rig.Track, rig.Clip);
        var at = view.TranslatePoint(new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2), rig.Window) ?? default;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
    }

    [AvaloniaFact]
    public void AMidiTakeClickSelectsOnlyTheClipAndOpensItsTrackInThePianoRoll()
    {
        var rig = Open(midi: true);
        rig.Window.Songs.Active.MarkClean();
        var steps = rig.Window.Songs.Active.Undo.UndoCount;
        Assert.False(rig.Roll.IsVisible);   // the demo track has written notes: it shows the tab
        ClickClip(rig);
        var model = rig.Arrangement.Input.Selection;
        Assert.Equal(1, model.ClipCount);
        Assert.False(rig.Score.Editor.HasSelection);
        Assert.False(model.HasScoreSelection);
        Assert.False(model.HasRange);
        Assert.True(rig.Roll.IsVisible);
        Assert.Equal(TrackEditorView.Auto, rig.Track.EditorView);   // the take click is an automatic switch: no view choice is saved
        Assert.False(rig.Roll.Presenter.IsTab);   // the take is what the roll edits, not the track tab
        Assert.Equal(PianoRollSourceChoice.OnLane(0), rig.Roll.Presenter.Choice);
        Assert.Equal(new[] { 60 }, rig.Roll.Presenter.Notes.Select(n => n.Pitch).ToArray());
        Assert.Equal(steps, rig.Window.Songs.Active.Undo.UndoCount);   // no undo step, and the song was not marked changed by the switch
        Assert.False(rig.Window.Songs.Active.Project.IsDirty);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void EditingAfterATakeClickWritesIntoTheTakeNeverAsksAboutTheTabAndAClickWhileOpenSwitchesBackToTheTake()
    {
        var rig = Open(midi: true);
        var tab = rig.Track.Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Select(n => (n.StringIndex, n.Fret, n.MidiValue)).ToList();
        ClickClip(rig);
        var presenter = rig.Roll.Presenter;
        presenter.Select(new[] { 0 });
        Assert.True(presenter.TransposeAsync(2).GetAwaiter().GetResult());

        Assert.Empty(rig.Window.OwnedWindows.OfType<PromptWindow>());   // no "edit the tab?" question
        Assert.Equal(62, rig.Clip.Notes!.Single().Pitch);
        Assert.Equal(tab, rig.Track.Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Select(n => (n.StringIndex, n.Fret, n.MidiValue)).ToList());

        rig.Roll.UseSource(PianoRollSourceChoice.OnTab);   // the roll is open on this track: switched to the tab by hand
        Assert.True(presenter.IsTab);
        ClickClip(rig);   // a click on the take while the roll is open switches the source back to the take
        Assert.False(presenter.IsTab);
        Assert.Equal(PianoRollSourceChoice.OnLane(0), presenter.Choice);
        rig.Window.Songs.Active.MarkClean();   // the edit above is a real one: Close would ask to save
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void AnAudioTakeClickSelectsOnlyTheClipAndLeavesThePaneAlone()
    {
        var rig = Open(midi: false);
        ClickClip(rig);
        var model = rig.Arrangement.Input.Selection;
        Assert.Equal(1, model.ClipCount);
        Assert.False(rig.Score.Editor.HasSelection);
        Assert.False(model.HasScoreSelection);
        Assert.False(rig.Roll.IsVisible);
        rig.Window.Close();
    }
}
