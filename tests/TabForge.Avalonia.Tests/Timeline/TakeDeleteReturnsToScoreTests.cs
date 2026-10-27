using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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

// Owns: deleting the MIDI take the piano roll shows after a take click (the automatic switch): the last take on the lane returns the score for the track
//   (the panel with it, also when a lane below shifts up into its number), another take on the lane keeps the roll on the lane, and a piano roll the user chose by hand stays.
// Does not own: the take click itself (TakeClickAutomaticViewTests) or the delete command (the clip key tests).
// Tests: EditorPaneSwitch.Update, MainWindowClipKeys.Edit.
public sealed class TakeDeleteReturnsToScoreTests
{
    private sealed record Rig(MainWindow Window, ArrangementView Arrangement, PianoRollView Roll, EditorModePills Pills, AudioClip Clip, AudioClip Other, SongDocument Document);

    private static Rig Open(bool secondTake, int otherLane = 0)
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new MainWindow(first: document);
        window.Show();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().Single();
        var project = document.Project;
        var span = BarRangeGaps.Span(project, 2, 3).GetValueOrDefault();
        var clip = Take(span.From, span.To);
        var other = Take(span.To + 1, span.To + 2);
        other.Lane = otherLane;
        ClipLanes.Ensure(project.Tracks[0], otherLane + 1);
        project.Tracks[0].AudioClips.Add(clip);
        if (secondTake) project.Tracks[0].AudioClips.Add(other);
        arrangement.Timeline.Reload(project);
        Settle(window);
        document.MarkClean();
        return new Rig(window, arrangement, window.GetLogicalDescendants().OfType<PianoRollView>().Single(),
            window.GetVisualDescendants().OfType<EditorModePills>().Single(), clip, other, document);
    }

    private static AudioClip Take(double from, double to) =>
        new() { File = "", Name = "take", StartSec = from, SourceLengthSec = to - from, FileLengthSec = to - from, Notes = new List<ClipNote> { new(0, 0.2, 60, 100) } };

    private static void Settle(MainWindow window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static void ClickClip(Rig rig)
    {
        rig.Window.UpdateLayout();
        var box = TimelineAudioClips.ClipBounds(rig.Arrangement.Timeline, rig.Document.Project.Tracks[0], rig.Clip);
        var at = rig.Arrangement.Timeline.TranslatePoint(new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2), rig.Window) ?? default;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        Settle(rig.Window);
    }

    private static void Delete(Rig rig, AudioClip clip)
    {
        MainWindowClipKeys.Edit(rig.Document, rig.Arrangement.Timeline, () => rig.Document.Project.Tracks[0].AudioClips.Remove(clip), "Delete clip");
        Settle(rig.Window);
    }

    [AvaloniaFact]
    public void DeletingTheOnlyTakeOfTheShownLaneReturnsToTheScoreAndTheFretboard()
    {
        var rig = Open(secondTake: false);
        ClickClip(rig);
        Assert.True(rig.Roll.IsVisible);
        Assert.False(rig.Window.DockWorkspace.IsPanelVisible("instrument"));

        Delete(rig, rig.Clip);

        Assert.False(rig.Roll.IsVisible);
        Assert.True(rig.Window.DockWorkspace.IsPanelVisible("instrument"));
        Assert.Equal(TrackEditorView.Auto, rig.Document.Project.Tracks[0].EditorView);
        rig.Document.MarkClean();
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void DeletingOneOfTwoTakesOnTheLaneStaysOnTheLane()
    {
        var rig = Open(secondTake: true);
        ClickClip(rig);
        Assert.True(rig.Roll.IsVisible);

        Delete(rig, rig.Clip);

        Assert.True(rig.Roll.IsVisible);
        Assert.Equal(PianoRollSourceChoice.OnLane(0), rig.Roll.Presenter.Choice);
        Delete(rig, rig.Other);   // the lane's last take goes too
        Assert.False(rig.Roll.IsVisible);
        rig.Document.MarkClean();
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void DeletingTheWholeLaneReturnsToTheScoreEvenWhenALaneBelowShiftsIntoItsNumber()
    {
        var rig = Open(secondTake: true, otherLane: 1);
        ClickClip(rig);
        Assert.True(rig.Roll.IsVisible);

        Delete(rig, rig.Clip);   // lane 0 is empty: the compaction moves the other take from lane 1 to lane 0

        Assert.Equal(0, rig.Other.Lane);
        Assert.False(rig.Roll.IsVisible);
        Assert.True(rig.Window.DockWorkspace.IsPanelVisible("instrument"));
        rig.Document.MarkClean();
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ARollTheUserChoseByHandStaysWhenItsTakeIsDeleted()
    {
        var rig = Open(secondTake: true);
        rig.Pills.Click(1);   // the Piano pill: the user own choice
        Assert.True(rig.Roll.IsVisible);

        Delete(rig, rig.Clip);
        Delete(rig, rig.Other);

        Assert.True(rig.Roll.IsVisible);
        Assert.Equal(TrackEditorView.PianoRoll, rig.Document.Project.Tracks[0].EditorView);
        rig.Document.MarkClean();
        rig.Window.Close();
    }
}
