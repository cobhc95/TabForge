using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the standing rule that the instrument panel (fretboard / keyboard) opens and closes ONLY from the user toggle: the piano roll holds it closed
//   while shown, and every way back to the score (take click then tab click, pills, track changes) gives it back exactly as the user had it; a panel the
//   user closed stays closed through every switch; the user choice (InstrumentPanelGate.UserOn) never changes from a view switch.
// Does not own: the pane switch itself (PianoRollPaneSwitchTests, TakeClickAutomaticViewTests) or the dock (DockWorkspaceController tests).
// Tests: InstrumentPanelGate, EditorPaneSwitch.ModeChanged, MainWindow (real take and tab lane clicks).
public sealed class InstrumentPanelRollHoldTests
{
    private const string PanelId = "instrument";

    private static (DockWorkspaceController Dock, InstrumentPanelGate Gate) Gate()
    {
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        var dock = new DockWorkspaceController(new DockWorkspaceView(setup.ContentOf), setup);
        if (!dock.IsPanelVisible(PanelId)) dock.SetPanelVisible(PanelId, true);
        return (dock, new InstrumentPanelGate(dock, _ => { }));
    }

    [AvaloniaFact]
    public void ToggleOnTheRollHidesAndEveryReturnToTheScoreRestoresItTwentyTimes()
    {
        var (dock, gate) = Gate();
        for (var i = 0; i < 20; i++)
        {
            gate.RollChanged(true);
            Assert.False(dock.IsPanelVisible(PanelId));
            Assert.True(gate.UserOn, "the roll hold never changes the user choice");
            gate.RollChanged(false);
            Assert.True(dock.IsPanelVisible(PanelId), $"round {i}");
            Assert.True(gate.UserOn);
        }
    }

    [AvaloniaFact]
    public void AToggleTheUserTurnedOffStaysOffThroughEverySwitch()
    {
        var (dock, gate) = Gate();
        dock.SetPanelVisible(PanelId, false);
        Assert.False(gate.UserOn);
        for (var i = 0; i < 20; i++)
        {
            gate.RollChanged(true);
            Assert.False(dock.IsPanelVisible(PanelId));
            gate.RollChanged(false);
            Assert.False(dock.IsPanelVisible(PanelId), $"round {i}");
            Assert.False(gate.UserOn);
        }
    }

    [AvaloniaFact]
    public void ATogglePressedWhileTheRollIsShownIsTheUserChoiceAndHoldsAfterTheScoreReturns()
    {
        var (dock, gate) = Gate();
        gate.RollChanged(true);
        dock.SetPanelVisible(PanelId, true);   // the user opens it over the roll
        Assert.True(gate.UserOn);
        dock.SetPanelVisible(PanelId, false);   // and closes it again: that is their choice now
        Assert.False(gate.UserOn);
        gate.RollChanged(false);
        Assert.False(dock.IsPanelVisible(PanelId));

        dock.SetPanelVisible(PanelId, true);
        gate.RollChanged(true);
        dock.SetPanelVisible(PanelId, true);   // open over the roll
        gate.RollChanged(false);
        Assert.True(dock.IsPanelVisible(PanelId));
        Assert.True(gate.UserOn);
    }

    [AvaloniaFact]
    public void ADockRebuildWhileTheRollHoldsThePanelDoesNotTurnTheUserChoiceOff()
    {
        var (dock, gate) = Gate();
        gate.RollChanged(true);
        dock.SetPanelVisible("timeline", false);   // an unrelated rebuild while held
        dock.SetPanelVisible("timeline", true);
        Assert.True(gate.UserOn);
        gate.RollChanged(false);
        Assert.True(dock.IsPanelVisible(PanelId));
    }

    private sealed record Rig(MainWindow Window, ArrangementView Arrangement, PianoRollView Roll, EditorModePills Pills, AudioClip Clip, SongDocument Document);

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
        Settle(window);
        document.MarkClean();
        return new Rig(window, arrangement, window.GetLogicalDescendants().OfType<PianoRollView>().Single(),
            window.GetVisualDescendants().OfType<EditorModePills>().Single(), clip, document);
    }

    private static void Settle(MainWindow window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static void Click(Rig rig, Point onTimeline)
    {
        var at = rig.Arrangement.Timeline.TranslatePoint(onTimeline, rig.Window) ?? default;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        Settle(rig.Window);
    }

    private static void ClickClip(Rig rig)
    {
        rig.Window.UpdateLayout();
        var box = TimelineAudioClips.ClipBounds(rig.Arrangement.Timeline, rig.Document.Project.Tracks[0], rig.Clip);
        Click(rig, new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2));
    }

    private static void ClickTabLane(Rig rig)
    {
        rig.Window.UpdateLayout();
        Click(rig, new Point(rig.Arrangement.Timeline.BarX(0) + 5, rig.Arrangement.Timeline.RowTop(0) + 10));
    }

    private static void ToggleInstrument(Rig rig)
    {
        rig.Window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == "View.InstrumentPanel").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Settle(rig.Window);
    }

    [AvaloniaFact]
    public void TakeClickThenTabClickKeepsTheFretboardOnTwentyTimesEvenWithTrackChangesBetween()
    {
        var rig = Open();
        Assert.True(rig.Window.DockWorkspace.IsPanelVisible(PanelId));
        for (var i = 0; i < 20; i++)
        {
            ClickClip(rig);
            Assert.True(rig.Roll.IsVisible, $"round {i}");
            Assert.False(rig.Window.DockWorkspace.IsPanelVisible(PanelId), $"round {i}: hidden while the roll is shown");
            if (i % 2 == 1) { rig.Document.TrackIndex = 1; Settle(rig.Window); }   // the roll ends by selection instead of the tab click
            else ClickTabLane(rig);
            Assert.False(rig.Roll.IsVisible, $"round {i}");
            Assert.True(rig.Window.DockWorkspace.IsPanelVisible(PanelId), $"round {i}: back with the score");
            rig.Document.TrackIndex = 0;
            Settle(rig.Window);
        }
        rig.Document.MarkClean();
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void AFretboardTheUserClosedStaysClosedThroughTakeClicksTabClicksAndPills()
    {
        var rig = Open();
        ToggleInstrument(rig);
        Assert.False(rig.Window.DockWorkspace.IsPanelVisible(PanelId));
        for (var i = 0; i < 5; i++)
        {
            ClickClip(rig);
            ClickTabLane(rig);
            rig.Pills.Click(1);
            rig.Pills.Click(93);
            rig.Document.TrackIndex = 1;
            rig.Document.TrackIndex = 0;
            Settle(rig.Window);
            Assert.False(rig.Window.DockWorkspace.IsPanelVisible(PanelId), $"round {i}");
        }
        ToggleInstrument(rig);
        Assert.True(rig.Window.DockWorkspace.IsPanelVisible(PanelId));
        rig.Document.MarkClean();
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ThePillsHideAndRestoreThePanelTwentyTimes()
    {
        var rig = Open();
        for (var i = 0; i < 20; i++)
        {
            rig.Pills.Click(1);
            Assert.False(rig.Window.DockWorkspace.IsPanelVisible(PanelId));
            rig.Pills.Click(93);
            Assert.True(rig.Window.DockWorkspace.IsPanelVisible(PanelId), $"round {i}");
        }
        rig.Document.MarkClean();
        rig.Window.Close();
    }
}
