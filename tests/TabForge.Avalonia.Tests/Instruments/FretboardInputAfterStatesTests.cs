using System;
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
using TabForge.Avalonia.Views.Docking;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard's pointer input in the real main window after each state a session goes through (compact mode, Band view, a new tab and a
/// switch back, the × close and the reopen, a window resize): a real pointer move over string 5, fret 3 sets the hover cell, and a click there
/// raises the note pick. Also the × close, which stays closed through a layout, a resize and a tab switch.
/// Does not own: the note edit itself (FretboardNoteEntry, ScoreEditCommands) or the fit of the pane height (FretboardNewTabHeightTests).
/// Tests: TabForge.Avalonia.Tests.FretboardInputAfterStatesTests.
/// </summary>
public sealed class FretboardInputAfterStatesTests
{
    /// <summary>The cell the checks point at: string 5 (index 4) at fret 3, on the standard six-string guitar.</summary>
    private const int CheckString = 4;
    private const int CheckFret = 3;

    private static MainWindow Open()
    {
        var main = new MainWindow { Width = 1500, Height = 960 };
        main.Show();
        Settle(main);
        return main;
    }

    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }

    private static InstrumentPanelStrip Strip(MainWindow main) => main.GetVisualDescendants().OfType<InstrumentPanelStrip>().Single();

    private static void Pick(MainWindow main, string commandId)
    {
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == commandId).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A real pointer move onto the check cell, then a left click there: the hover cell is the check cell, and the click raises its pick.</summary>
    private static void AssertFretboardInput(MainWindow main, string step)
    {
        var panel = Strip(main).Panel;
        var local = panel.FretCentre(CheckString, CheckFret);
        var at = panel.TranslatePoint(new Point(local.X, local.Y), main)!.Value;
        main.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.Hovered == (CheckString, CheckFret), $"{step}: hover cell is {panel.Hovered}, not string {CheckString + 1} fret {CheckFret}");

        var picks = new List<InstrumentNotePick>();
        void Record(InstrumentNotePick pick) => picks.Add(pick);
        panel.NotePicked += Record;
        main.MouseDown(at, MouseButton.Left);
        main.MouseUp(at, MouseButton.Left);
        panel.NotePicked -= Record;
        Assert.True(picks.Count == 1 && picks[0].StringIndex == CheckString && picks[0].Fret == CheckFret, $"{step}: the click picked {picks.Count} notes");
    }

    [AvaloniaFact]
    public void FretboardHoverAndClickWorkAfterEachStateOfASession()
    {
        var main = Open();
        AssertFretboardInput(main, "launch");

        Pick(main, "View.CompactMode");
        Settle(main);
        AssertFretboardInput(main, "compact on");
        Pick(main, "View.CompactMode");
        Settle(main);
        AssertFretboardInput(main, "compact off");

        Pick(main, "View.BandView");
        Settle(main);
        Pick(main, "View.BandView");
        Settle(main);
        AssertFretboardInput(main, "band view on and off");

        main.Songs.New();
        Settle(main);
        main.Songs.Switch(0);
        Settle(main);
        AssertFretboardInput(main, "new tab and switch back");

        main.Height = 800;
        Settle(main);
        AssertFretboardInput(main, "window resized");
        main.Songs.Active.MarkClean();   // the clicks above edited the song; a dirty song would open the save prompt on close
        main.Close();
    }

    [AvaloniaFact]
    public void ClosedFretboardStaysClosedThroughALayoutAResizeAndATabSwitch()
    {
        var main = Open();
        var strip = Strip(main);
        strip.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "InstrumentHideButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(main);
        main.DockWorkspace.FlushLayoutChanged();   // the debounced layout save runs now, as it does a moment after the close
        Settle(main);
        Assert.False(main.DockWorkspace.IsPanelVisible("instrument"));
        Assert.Empty(main.GetVisualDescendants().OfType<InstrumentPanelStrip>());

        main.Height = 800;
        Settle(main);
        main.Songs.New();
        Settle(main);
        main.Songs.Switch(0);
        Settle(main);
        Assert.False(main.DockWorkspace.IsPanelVisible("instrument"));
        Assert.Empty(main.GetVisualDescendants().OfType<InstrumentPanelStrip>());

        Pick(main, "View.InstrumentPanel");
        Settle(main);
        Assert.True(main.DockWorkspace.IsPanelVisible("instrument"), "the View menu shows the fretboard again");
        AssertFretboardInput(main, "reopened from the View menu");
        main.Songs.Active.MarkClean();   // the clicks above edited the song; a dirty song would open the save prompt on close
        main.Close();
    }
}
