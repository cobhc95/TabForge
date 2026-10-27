using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the automatic switch a MIDI take click makes (piano roll on that take's lane, back to the score on a click on the same track's tab lane or header)
//   never saves a view choice, never marks the song changed and never adds an undo step; a piano roll the user picked stays on a tab click; Tab clears it.
// Does not own: the take click selecting only the clip (TakeClickNeverSelectsNotesTests) or the manual switch itself (PianoRollPaneSwitchTests).
// Tests: EditorPaneSwitch, TimelineInputController (MidiClipClicked, ScoreRequested).
public sealed class TakeClickAutomaticViewTests
{
    private sealed record Rig(MainWindow Window, ArrangementView Arrangement, PianoRollView Roll, ScorePreviewView Score, EditorModePills Pills, AudioClip Clip, SongDocument Document);

    private static Rig Open()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new MainWindow(first: document);
        window.Show();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().Single();
        var project = document.Project;
        var span = BarRangeGaps.Span(project, 2, 3).GetValueOrDefault();
        var clip = new AudioClip { File = "", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From, Notes = new List<ClipNote> { new(0, 0.2, 60, 100) } };
        project.Tracks[0].AudioClips.Add(clip);
        arrangement.Timeline.Reload(project);
        window.UpdateLayout();
        document.MarkClean();
        return new Rig(window, arrangement, window.GetLogicalDescendants().OfType<PianoRollView>().Single(), window.GetLogicalDescendants().OfType<ScorePreviewView>().Single(),
            window.GetVisualDescendants().OfType<EditorModePills>().Single(), clip, document);
    }

    private static void ClickClip(Rig rig)
    {
        rig.Window.UpdateLayout();
        var view = rig.Arrangement.Timeline;
        var box = TimelineAudioClips.ClipBounds(view, rig.Document.Project.Tracks[0], rig.Clip);
        Click(rig, new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2));
    }

    private static void ClickTabLane(Rig rig, int row = 0)
    {
        rig.Window.UpdateLayout();
        Click(rig, new Point(rig.Arrangement.Timeline.BarX(0) + 5, rig.Arrangement.Timeline.RowTop(row) + 10));
    }

    private static void Click(Rig rig, Point onTimeline)
    {
        var at = rig.Arrangement.Timeline.TranslatePoint(onTimeline, rig.Window) ?? default;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
    }

    // The view switch adds no undo step. (A click on a tab cell may add its own "Highlight bars" step: that is the highlight, not the switch.)
    private static void Quiet(Rig rig, int steps, bool highlight = false)
    {
        Assert.False(rig.Document.Project.IsDirty);
        if (highlight) Assert.DoesNotContain("Show in", rig.Document.Undo.Latest?.Label ?? "");
        else Assert.True(steps == rig.Document.Undo.UndoCount, "undo: " + rig.Document.Undo.Latest?.Label);
        Assert.Equal(TrackEditorView.Auto, rig.Document.Project.Tracks[0].EditorView);
    }

    [AvaloniaFact]
    public void ATakeClickShowsTheRollOnItsLaneAndATabLaneClickOnTheSameTrackBringsTheScoreBackWithoutSavingAnything()
    {
        var rig = Open();
        var steps = rig.Document.Undo.UndoCount;
        Assert.False(rig.Roll.IsVisible);

        ClickClip(rig);
        Assert.True(rig.Roll.IsVisible);
        Assert.False(rig.Roll.Presenter.IsTab);
        Assert.Equal(PianoRollSourceChoice.OnLane(0), rig.Roll.Presenter.Choice);
        Quiet(rig, steps);

        ClickTabLane(rig);
        Assert.False(rig.Roll.IsVisible);
        Assert.True(rig.Score.IsVisible);
        Quiet(rig, steps, highlight: true);

        ClickClip(rig);   // and again: the take opens the roll every time
        Assert.True(rig.Roll.IsVisible);
        Quiet(rig, steps, highlight: true);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void SelectingAnotherTrackAndComingBackShowsTheScoreAndOtherTracksAreUnaffected()
    {
        var rig = Open();
        ClickClip(rig);
        Assert.True(rig.Roll.IsVisible);

        rig.Document.TrackIndex = 1;   // another track: its own view (the score)
        Assert.False(rig.Roll.IsVisible);
        rig.Document.TrackIndex = 0;   // back: the take click's roll is over
        Assert.False(rig.Roll.IsVisible);
        Assert.Equal(TrackEditorView.Auto, rig.Document.Project.Tracks[1].EditorView);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void APianoRollChosenByHandStaysOnATabClickAndChoosingTabGoesBackToTheAutomaticBehaviour()
    {
        var rig = Open();
        rig.Pills.Click(1);   // the Piano pill
        Assert.True(rig.Roll.IsVisible);
        Assert.Equal(TrackEditorView.PianoRoll, rig.Document.Project.Tracks[0].EditorView);

        ClickTabLane(rig);
        Assert.True(rig.Roll.IsVisible);   // the user's own choice is not overridden
        ClickClip(rig);
        ClickTabLane(rig);
        Assert.True(rig.Roll.IsVisible);

        rig.Pills.Click(93);   // the Tab pill clears it
        Assert.False(rig.Roll.IsVisible);
        Assert.Equal(TrackEditorView.Score, rig.Document.Project.Tracks[0].EditorView);
        ClickClip(rig);   // automatic again: the take opens the roll, the tab click closes it, the choice is untouched
        Assert.True(rig.Roll.IsVisible);
        ClickTabLane(rig);
        Assert.False(rig.Roll.IsVisible);
        Assert.Equal(TrackEditorView.Score, rig.Document.Project.Tracks[0].EditorView);
        rig.Document.MarkClean();
        rig.Window.Close();
    }
}
