using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a Band card's fretboard button in real input (a click opens that card's board and nothing else's), the board of a card being its own
/// track's instrument (a fretboard for a guitar, the drum map for a drum track), and the main-mode fretboard menu acting on the card's own track.
/// Does not own: the card's drawing (BandTrackPanel), the menu's rows (FretboardContextMenuTests) or the kind rules (FretboardSettingsBinding).
/// Tests: TabForge.Avalonia.Platform.BandTrackPanel, TabForge.Avalonia.Platform.BandView, TabForge.Avalonia.Platform.FretboardContextMenu.
/// </summary>
public class BandFretboardInteractionTests
{
    private static SongDocument DemoDocument() => new(() => null) { Project = DemoSongFactory.Create() };

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static Point Centre(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>The demo song's drum track.</summary>
    private static int DrumsIndex(SongDocument document) => document.Project.Tracks.ToList().FindIndex(track => track.Kind == TrackKind.Drums);

    [AvaloniaFact]
    public void ClickingACardsFretboardButtonOpensItsBoard()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        Settle(window);
        var view = window.View;
        var index = view.ShownTrackIndices[1];
        var panel = view.PanelFor(index)!;
        Assert.False(panel.FretboardShown);

        var at = Centre(window, panel.FretboardButton);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        Assert.True(view.FretboardOn(index), "the card's fretboard did not open");
        Assert.True(panel.FretboardShown);
        Assert.True(panel.Instrument.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void EachCardsButtonOpensOnlyItsOwnBoard()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        Settle(window);
        var view = window.View;
        Assert.True(view.ShownTrackIndices.Count >= 3);
        var index = view.ShownTrackIndices[2];
        var panel = view.PanelFor(index)!;

        var at = Centre(window, panel.FretboardButton);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        Assert.True(panel.FretboardShown);
        Assert.False(view.PanelFor(view.ShownTrackIndices[1])!.FretboardShown);
        window.Close();
    }

    [AvaloniaFact]
    public void ACardsBoardIsItsOwnTracksInstrument()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        Settle(window);
        var view = window.View;
        var drums = DrumsIndex(document);
        view.ToggleShown(drums);
        view.SetFretboardOn(drums, true);
        Settle(window);

        var drumsPanel = view.PanelFor(drums)!;
        Assert.True(drumsPanel.FretboardShown);
        Assert.Equal(InstrumentPanelKind.Drums, drumsPanel.Instrument.Kind);
        // A guitar card keeps its neck, and a bass track stays a fretboard too.
        Assert.Equal(InstrumentPanelKind.Fretboard, view.PanelFor(view.ShownTrackIndices[0])!.Instrument.Kind);
        window.Close();
    }

    [AvaloniaFact]
    public void TheFretboardMenuChangesTheCardsOwnTrackOnly()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        Settle(window);
        var view = window.View;
        var drums = DrumsIndex(document);
        view.ToggleShown(drums);
        view.SetFretboardOn(drums, true);
        var settings = new AppSettings();
        // The main pane's track is another one: the menu must still name the card's own track.
        var menu = new FretboardContextMenu(() => settings, () => { }, () => { }, () => document.Project.Tracks[0]);
        view.FretboardMenu = menu;
        view.InstrumentLook = FretboardSettingsBinding.Look.From(settings);
        Settle(window);
        var drumsPanel = view.PanelFor(drums)!;
        Assert.Equal(InstrumentPanelKind.Drums, drumsPanel.Instrument.Kind);

        menu.RunFor(document.Project.Tracks[drums], FretboardContextMenu.ViewId, InstrumentViews.Keyboard);
        Settle(window);

        Assert.Equal(InstrumentPanelKind.Keyboard, drumsPanel.Instrument.Kind);
        Assert.Equal(InstrumentPanelKind.Fretboard, view.PanelFor(view.ShownTrackIndices[0])!.Instrument.Kind);
        // The rows of that card mark the view it took, and another track's rows mark the settings' default.
        var drumView = menu.RowsFor(InstrumentPanelKind.Keyboard, document.Project.Tracks[drums]).Single(row => row.Header == "Show this track as");
        Assert.Contains(drumView.Children!, row => row.Arg == InstrumentViews.Keyboard && row.Checked);
        var guitarView = menu.RowsFor(InstrumentPanelKind.Fretboard, document.Project.Tracks[0]).Single(row => row.Header == "Show this track as");
        Assert.Contains(guitarView.Children!, row => row.Arg == InstrumentViews.MatchInstrument && row.Checked);
        window.Close();
    }
}
