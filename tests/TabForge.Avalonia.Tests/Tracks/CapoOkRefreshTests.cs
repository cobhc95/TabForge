using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the board refresh after Track properties OK: a committed full or partial capo reaches the
/// instrument pane at once, the cursor stays put, and Cancel leaves the board untouched.
/// Does not own: the capo drawing itself (MainBoardCapoVisualTests), the card placement
/// (CapoPlacementTests), or the OK edit (TrackPropertiesWindowTests).
/// Tests: InstrumentPanelStrip, TrackPropertiesWindow, DocumentEdits.
/// </summary>
public sealed class CapoOkRefreshTests
{
    private static (SongDocument Document, TrackModel Track) Song()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        track.Capo = 0;
        track.PartialCapos = null;
        return (new SongDocument(() => null) { Project = new SongProject { Tracks = new List<TrackModel> { track } }, TrackIndex = 0 }, track);
    }

    // A pane showing the document, laid out wide enough for the fretboard (the headless 1600 px rule).
    private static (Window Host, ScorePreviewView Score, InstrumentPanelStrip Strip) Pane(SongDocument document)
    {
        var score = new ScorePreviewView();
        score.Show(document);
        var strip = new InstrumentPanelStrip(score);
        var host = new Window { Width = 1600, Height = 400, Content = strip };
        host.Show();
        host.UpdateLayout();
        return (host, score, strip);
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static InstrumentFretNumberTests.RecordingDrawContext Draw(InstrumentPanelView panel)
    {
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        panel.Draw(draw, light: false);
        return draw;
    }

    private static bool CoversCentre(InstrumentFretNumberTests.RecordingDrawContext draw, double centreX)
    {
        foreach (var image in draw.Images)
            if (image.X <= centreX && centreX <= image.X + image.Width && image.Width / image.Height < 1.5)
                return true;
        return false;
    }

    // A real pointer click: a ToggleButton only flips its check state through the click gesture.
    private static void Click(Window window, Control control)
    {
        window.UpdateLayout();
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    private static void Discard(TrackPropertiesWindow window)
    {
        DialogTestKit.Click(Named<Button>(window, "CancelButton"));
        Dispatcher.UIThread.RunJobs();
        if (window.IsVisible) DialogTestKit.Click(Named<Button>(window.OwnedWindows.OfType<SavePromptWindow>().Single(), "YesButton"));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void FullCapoOkRefreshesTheBoardAndLeavesTheCursorInPlace()
    {
        var (document, track) = Song();
        var (host, score, strip) = Pane(document);
        var window = new TrackPropertiesWindow(track, document);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var cursor = score.Editor.Cursor;
            var refreshes = 0;
            score.CursorChanged += () => refreshes++;

            CapoPlacementTests.ClickFret(window, 4);
            var before = refreshes;
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Applied);
            Assert.Equal(4, track.Capo);
            Assert.Equal(cursor, score.Editor.Cursor);
            Assert.True(refreshes > before, "the committed capo did not raise the board refresh");

            host.UpdateLayout();
            var draw = Draw(strip.Panel);
            var centre = FretboardGeometryRules.FretX(strip.Panel.FretboardLayout(), 4);
            Assert.True(CoversCentre(draw, centre), "the refreshed board draws no clamp bar over fret 4");
        }
        finally
        {
            if (window.IsVisible) Discard(window);
            host.Close();
        }
    }

    [AvaloniaFact]
    public void PartialCapoOkRefreshesTheBoardClippedToItsStrings()
    {
        var (document, track) = Song();
        var (host, score, strip) = Pane(document);
        var window = new TrackPropertiesWindow(track, document);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var refreshes = 0;
            score.CursorChanged += () => refreshes++;

            CapoPlacementTests.PlacePartial(window, 7);
            // The card's default partial covers the three middle strings; trim it to strings 2 and 3.
            Named<ComboBox>(window, "CapoStringTo").SelectedIndex = 2;
            var before = refreshes;
            DialogTestKit.Click(Named<Button>(window, "OkButton"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Applied);
            var partial = Assert.Single(track.PartialCapos!);
            Assert.Equal((7, 0b000110), (partial.Fret, partial.StringMask));
            Assert.True(refreshes > before, "the committed partial capo did not raise the board refresh");

            host.UpdateLayout();
            var draw = Draw(strip.Panel);
            var layout = strip.Panel.FretboardLayout();
            var centre = FretboardGeometryRules.FretX(layout, 7);
            Assert.True(CoversCentre(draw, centre), "the refreshed board draws no clamp bar over fret 7");
            var wood = FretboardArtist.Wood(layout, bass: false);
            Assert.Contains(draw.Clips, clip =>
                clip.Y >= wood.Y - 0.001 && clip.Y + clip.Height <= wood.Bottom + 0.001 && clip.Height < wood.Height);
        }
        finally
        {
            if (window.IsVisible) Discard(window);
            host.Close();
        }
    }

    [AvaloniaFact]
    public void CancelLeavesTheBoardWithoutTheDiscardedCapo()
    {
        var (document, track) = Song();
        var (host, score, strip) = Pane(document);
        var window = new TrackPropertiesWindow(track, document);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var refreshes = 0;
            score.CursorChanged += () => refreshes++;

            CapoPlacementTests.ClickFret(window, 5);
            var before = refreshes;
            Discard(window);

            Assert.False(window.Applied);
            Assert.Equal(0, track.Capo);
            Assert.Equal(before, refreshes);   // Cancel writes nothing and needs no refresh

            host.UpdateLayout();
            var draw = Draw(strip.Panel);
            var centre = FretboardGeometryRules.FretX(strip.Panel.FretboardLayout(), 5);
            Assert.False(CoversCentre(draw, centre), "a cancelled capo must not reach the board");
        }
        finally
        {
            host.Close();
        }
    }
}
