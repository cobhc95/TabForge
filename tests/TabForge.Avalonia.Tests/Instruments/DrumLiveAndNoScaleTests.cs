using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the drum-track checks: the drum map fills sounding notes with no look-ahead lists, hides the Scales button,
//   keeps the Scale menu rows away, shows a dash with no detection on the status readout, and disables the piano roll
//   scale controls; melodic tracks keep everything.
// Does not own: the views themselves (the instrument, scale and piano roll tests).
// Tests: InstrumentPlaybackLists, InstrumentPanelStrip, FretboardContextMenu, StatusScaleReadout, PianoRollView.
public sealed class DrumLiveAndNoScaleTests
{
    private static NoteEvent Note(double onset, double duration, int midi) =>
        new() { OnsetMs = onset, DurationMs = duration, Midi = midi };

    [Fact]
    public void DrumFillKeepsSoundingNotesWithNoLookAheadLists()
    {
        var notes = new[] { Note(0, 400, 36), Note(1000, 800, 38), Note(2000, 300, 42), Note(3000, 300, 46) };
        var drums = new InstrumentPlaybackSnapshot();
        InstrumentPlaybackLists.Fill(drums, notes, 1200, previewHorizon: -1);
        Assert.Equal(new[] { 38 }, drums.Now.Select(n => n.Midi).ToArray());
        Assert.Empty(drums.Next);
        Assert.Empty(drums.Upcoming);
        Assert.Empty(drums.UpcomingDepth);

        var melodic = new InstrumentPlaybackSnapshot();
        InstrumentPlaybackLists.Fill(melodic, notes, 1200, previewHorizon: 6);
        Assert.Equal(new[] { 38 }, melodic.Now.Select(n => n.Midi).ToArray());
        Assert.Equal(new[] { 42 }, melodic.Next.Select(n => n.Midi).ToArray());
        Assert.Equal(new[] { 46 }, melodic.Upcoming.Select(n => n.Midi).ToArray());
    }

    [AvaloniaFact]
    public void DrumTrackHidesScalesButtonAndScaleMenuWhileMelodicKeepsBoth()
    {
        var song = DemoSongFactory.Create();
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = song });
        var strip = new InstrumentPanelStrip(score);
        var window = new Window { Width = 1100, Height = 400, Content = strip };
        window.Show();
        var scales = strip.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ScalesButton");
        try
        {
            score.ShowTrack(0);
            strip.Refresh();
            Assert.Equal(InstrumentPanelKind.Fretboard, strip.Panel.Kind);
            Assert.True(scales.IsVisible);
            score.ShowTrack(3);
            strip.Refresh();
            Assert.Equal(InstrumentPanelKind.Drums, strip.Panel.Kind);
            Assert.False(scales.IsVisible);
            score.ShowTrack(0);
            strip.Refresh();
            Assert.True(scales.IsVisible);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }

        var menu = new FretboardContextMenu(() => new AppSettings(), () => { }, () => { }, () => null);
        Assert.DoesNotContain(menu.RowsFor(InstrumentPanelKind.Drums).Select(r => r.Header), h => h == "Scale");
        Assert.Contains(menu.RowsFor(InstrumentPanelKind.Fretboard).Select(r => r.Header), h => h == "Scale");
        Assert.Contains(menu.RowsFor(InstrumentPanelKind.Keyboard).Select(r => r.Header), h => h == "Scale");
    }

    [AvaloniaFact]
    public void DrumTrackReadoutShowsADashWithNoDetectionAndMelodicBringsItBack()
    {
        var song = DemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = song };
        var editing = new EditingSettings();
        var view = new StatusBarView();
        var readout = new StatusScaleReadout(view, () => editing, () => document, () => song, () => { });
        string Text() => view.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "StatusScaleText").Text ?? "";

        readout.Show(song, 0, song.Tracks[3]);
        Assert.Equal("—", Text());
        Assert.Equal(0, readout.DetectionRuns);
        readout.Open();
        Assert.Null(readout.Popup);

        readout.Show(song, 0, song.Tracks[0]);
        Assert.Equal(1, readout.DetectionRuns);
        Assert.NotEqual("—", Text());
        readout.Show(song, 0, song.Tracks[3]);
        Assert.Equal("—", Text());
        Assert.Equal(1, readout.DetectionRuns);
    }

    [AvaloniaFact]
    public void DrumTrackDisablesPianoRollScaleControlsAndMelodicReEnablesThem()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new PianoRollView(new PianoRollHost { Document = () => document });
        var window = new Window { Width = 2000, Height = 700, Content = view };
        window.Show();
        try
        {
            view.Show(document, 3, newVisit: true);
            window.UpdateLayout();
            var row = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.Row1Name);
            var drumTips = row.GetVisualDescendants().OfType<Control>()
                .Where(c => (ToolTip.GetTip(c) as string) == "Drum tracks have no scale").ToList();
            Assert.Equal(3, drumTips.Count);   // the scale button, Lock to scale and the step filter
            Assert.All(drumTips, c => Assert.False(c.IsEnabled));
            Assert.Null(view.Presenter.Scale);
            Assert.False(view.Presenter.ScaleLock);
            view.OpenScalePopup();

            view.Show(document, 0, newVisit: true);
            window.UpdateLayout();
            var lockButton = row.GetVisualDescendants().OfType<ToggleButton>()
                .Single(b => (ToolTip.GetTip(b) as string)?.StartsWith("Lock to scale", System.StringComparison.Ordinal) == true);
            Assert.True(lockButton.IsEnabled);
            Assert.DoesNotContain(row.GetVisualDescendants().OfType<Control>(),
                c => (ToolTip.GetTip(c) as string) == "Drum tracks have no scale");
        }
        finally
        {
            window.Close();
        }
    }
}
