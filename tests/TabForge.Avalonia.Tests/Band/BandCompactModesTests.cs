using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Band view and compact mode together in the main window: the shared header strip follows the mode entered last (its caption), the menu
/// row stays hidden while either mode is on, each mode's exit leaves the other on, and closing the Band panel by hand ends the Band view's header mode.
/// Does not own: the cards (BandCardTests), the layouts (CompactModeLayoutTests), the tick rows (CompactBandHeaderTests).
/// Tests: MainWindowHeaderModes, MainWindowBandChrome, MainWindowBandView, MainWindowCompactMode.
/// </summary>
public class BandCompactModesTests
{
    /// <summary>A main window showing the full demo song: the tests need its tracks, and the window itself now starts with an empty song.</summary>
    private static MainWindow Open(double width, double height)
    {
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song) { Width = width, Height = height };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    private static MenuItem Row(MainWindow main, string commandId) =>
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == commandId);

    private static void Pick(MainWindow main, string commandId)
    {
        Row(main, commandId).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static string CaptionOf(CompactTrackStrip strip) =>
        strip.MenuButton.GetVisualDescendants().OfType<TextBlock>().First().Text ?? "";

    private static bool MenuRowVisible(MainWindow main) => main.GetLogicalDescendants().OfType<Menu>().All(menu => menu.IsEffectivelyVisible);

    /// <summary>The side panels showing now, sorted, so two states compare as lists.</summary>
    private static List<string> VisibleSidePanels(MainWindow main) =>
        DockPaneTable.SideIds.Where(main.DockWorkspace.IsPanelVisible).OrderBy(id => id).ToList();

    [AvaloniaFact]
    public void CompactExitWhileBandIsOnThenBandExitRestoresEverySidePanel()
    {
        var main = Open(1320, 900);
        var before = VisibleSidePanels(main);
        Pick(main, "View.BandView");
        Pick(main, "View.CompactMode");
        Pick(main, "View.CompactMode");
        Pick(main, "View.BandView");
        Assert.Equal(before, VisibleSidePanels(main));
        Assert.True(MenuRowVisible(main));
        main.Close();
    }

    [AvaloniaFact]
    public void BandExitWhileCompactIsOnKeepsItsSidePanelsHiddenUntilCompactExits()
    {
        var main = Open(1320, 900);
        var before = VisibleSidePanels(main);
        Pick(main, "View.BandView");
        Pick(main, "View.CompactMode");
        Assert.Empty(VisibleSidePanels(main));
        Pick(main, "View.BandView");
        Assert.Empty(VisibleSidePanels(main));
        Assert.False(MenuRowVisible(main));
        Pick(main, "View.CompactMode");
        Assert.Equal(before, VisibleSidePanels(main));
        Assert.True(MenuRowVisible(main));
        main.Close();
    }

    [AvaloniaFact]
    public void BothModesOnShareOneStripAndTheMenuRowReturnsOnlyWhenBothAreOff()
    {
        var main = Open(1320, 900);
        var strip = main.GetVisualDescendants().OfType<CompactTrackStrip>().Single();

        Pick(main, "View.BandView");
        Assert.True(strip.IsVisible);
        Assert.Equal(HeaderModeCaption.BandCaption, CaptionOf(strip));
        Assert.False(MenuRowVisible(main));

        Pick(main, "View.CompactMode");
        Dispatcher.UIThread.RunJobs();
        DialogTestKit.SavePng(main, nameof(BandCompactModesTests), "both-modes-on.png");
        Assert.True(strip.IsVisible);
        Assert.Equal(CompactModeStripPresenter.Caption, CaptionOf(strip));
        Assert.False(MenuRowVisible(main));

        Pick(main, "View.CompactMode");
        Assert.True(strip.IsVisible, "the Band view stays on with its strip");
        Assert.Equal(HeaderModeCaption.BandCaption, CaptionOf(strip));
        Assert.False(MenuRowVisible(main), "the Band view still hides the menu row");

        Pick(main, "View.BandView");
        Assert.False(strip.IsVisible);
        Assert.True(MenuRowVisible(main));
        main.Close();
    }

    [AvaloniaFact]
    public void CompactModeDoesNotChangeWhichPillsTheBandViewLights()
    {
        var main = Open(1320, 900);
        var strip = main.GetVisualDescendants().OfType<CompactTrackStrip>().Single();
        List<bool> Lit() => strip.Pills.Select(p => p.IsChecked == true).ToList();

        Pick(main, "View.BandView");
        var bandAlone = Lit();
        Assert.True(bandAlone.Count(lit => lit) > 1, "the Band view lights every shown card");

        Pick(main, "View.CompactMode");
        Assert.Equal(bandAlone, Lit());
        Pick(main, "View.CompactMode");
        Assert.Equal(bandAlone, Lit());
        main.Close();
    }

    [AvaloniaFact]
    public void SelectingTracksInTheBandViewNeverShowsOrHidesTheInstrumentPanel()
    {
        var main = Open(2000, 1100);
        Pick(main, "View.BandView");
        main.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var dock = main.DockWorkspace;
        var visible = dock.IsPanelVisible("instrument");
        var document = main.GetVisualDescendants().OfType<BandView>().Single().Song!;
        for (var i = 0; i < document.Project.Tracks.Count; i++)
        {
            document.TrackIndex = i;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(visible, dock.IsPanelVisible("instrument"));
            Assert.True(main.GetVisualDescendants().OfType<InstrumentPanelStrip>().Count() <= 1);
        }
        main.Close();
    }

    [AvaloniaFact]
    public void ClosingTheBandPanelByHandEndsTheBandViewsHeaderMode()
    {
        var main = Open(1320, 900);
        var strip = main.GetVisualDescendants().OfType<CompactTrackStrip>().Single();
        Pick(main, "View.BandView");
        Assert.True(strip.IsVisible);

        main.DockWorkspace.SetPanelVisible("band", false);
        main.DockWorkspace.FlushLayoutChanged();   // the dock reports its change after a short delay; this reports it now
        Dispatcher.UIThread.RunJobs();
        Assert.False(strip.IsVisible);
        Assert.True(MenuRowVisible(main));
        main.Close();
    }

    [AvaloniaFact]
    public void BandViewHidesTheFretboardPaneAndTrackListAndAClickedCapsuleTogglesItsCard()
    {
        var main = Open(2000, 1100);
        bool InstrumentShown() => main.GetVisualDescendants().OfType<DockTabSurface>().Any(s => s.Selected == "instrument");
        Assert.True(InstrumentShown());

        Pick(main, "View.BandView");
        main.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.False(InstrumentShown());
        // The Band view alone keeps the normal bottom: the track list column is shown.
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        Assert.True(arrangement.Header.IsVisible);

        // A real click on the fourth capsule (a hidden track) hit-tests to it and adds that track's card, lit.
        var band = main.GetVisualDescendants().OfType<BandView>().Single();
        var strip = main.GetVisualDescendants().OfType<CompactTrackStrip>().Single();
        var pill = strip.Pills[3];
        Assert.False(band.IsShown(3));
        var centre = pill.TranslatePoint(new Point(pill.Bounds.Width / 2, pill.Bounds.Height / 2), main)!.Value;
        main.MouseDown(centre, MouseButton.Left);
        main.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(band.IsShown(3));
        Assert.True(strip.Pills[3].IsChecked);

        Pick(main, "View.BandView");
        main.UpdateLayout();
        Assert.True(InstrumentShown());
        Assert.True(arrangement.Header.IsVisible || !arrangement.IsAttachedToVisualTree());
        main.Close();
    }
}
