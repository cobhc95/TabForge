using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

/// <summary>
/// Owns: the fretboard's Scales button opens the scale finder on the selected track, for a fretboard and for a keyboard panel (the button sits at the
/// top-right there), and a finder's choice is written to the editing settings: "Off" clears the highlight, a scale sets it, a close without a choice
/// changes nothing.
/// Does not own: the finder's search (ScaleFinderPresenter), the window (ScaleFinderWindow) or the highlight's drawing (InstrumentPanelView).
/// Tests: TabForge.Avalonia.Tests.FretboardScaleFinderTests.
/// </summary>
public sealed class FretboardScaleFinderTests
{
    private const int BassIndex = 3;
    private const string ChosenScale = "C Major";

    [AvaloniaFact]
    public void ScalesButtonOpensTheFinderOnTheSelectedTrackAndTheChoiceIsWritten()
    {
        var project = DemoSongFactory.Create();
        while (project.Tracks.Count <= BassIndex) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Guitar));
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Keys));
        var keysIndex = project.Tracks.Count - 1;
        var main = new MainWindow(first: new SongDocument(() => null) { Project = project }) { Width = 1500, Height = 960 };
        main.Show();
        Settle(main);
        var scales = main.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ScalesButton");

        main.Songs.Active.TrackIndex = BassIndex;
        Settle(main);
        Assert.True(scales.IsVisible);
        DialogTestKit.Click(scales);
        Dispatcher.UIThread.RunJobs();
        var finder = Assert.Single(main.OwnedWindows.OfType<ScaleFinderWindow>());
        Assert.Same(project.Tracks[BassIndex], finder.SearchedTrack);
        finder.Close();

        main.Songs.Active.TrackIndex = keysIndex;
        Settle(main);
        Assert.True(scales.IsVisible, "a keyboard panel shows the Scales button too");
        Assert.Equal(InstrumentPanelView.CloseButtonReserve, Canvas.GetRight(scales));
        DialogTestKit.Click(scales);
        Dispatcher.UIThread.RunJobs();
        var keysFinder = Assert.Single(main.OwnedWindows.OfType<ScaleFinderWindow>());
        Assert.Same(project.Tracks[keysIndex], keysFinder.SearchedTrack);
        keysFinder.Close();

        var editing = new EditingSettings();
        Assert.False(MainWindowScaleFinder.ApplyChoice(editing, null), "a close without a choice writes nothing");
        Assert.Null(editing.ScaleHighlight);
        Assert.True(MainWindowScaleFinder.ApplyChoice(editing, ChosenScale));
        Assert.Equal(ChosenScale, editing.ScaleHighlight);
        Assert.True(MainWindowScaleFinder.ApplyChoice(editing, ScaleFinderPresenter.OffHighlight));
        Assert.Null(editing.ScaleHighlight);
        main.Close();
    }

    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }
}
