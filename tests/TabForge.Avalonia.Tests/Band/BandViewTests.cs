using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Band view's track cards (one per shown track), the card toggles, the panel staff that draws only its
/// track, and the master lane in the timeline and its header cell, and the section label ink contrast. The card rules are in BandCardTests.
/// Renders go to %TEMP%\tf-avalonia-tests\BandViewTests.
/// Tests: BandView, BandTrackPanel, BandWindow, MasterLaneStrip, TimelineView.LabelInk, TrackHeaderColumn master cell.
/// </summary>
public class BandViewTests
{
    private static void Shot(Window w, string name)
    {
        var dir = DialogTestKit.RenderDirectory(nameof(BandViewTests));
        using var frame = w.CaptureRenderedFrame();
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

    private static SongDocument DemoDocument() => new(() => null) { Project = DemoSongFactory.Create() };

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BandLayoutShowsOnePanelPerShownTrack(bool light)
    {
        var document = DemoDocument();
        var window = new BandWindow(document) { RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        var view = window.View;
        Assert.NotEmpty(view.ShownTrackIndices);
        Assert.Equal(view.ShownTrackIndices.Count, view.VisiblePanelCount);
        foreach (var index in view.ShownTrackIndices) Assert.Same(document.Project.Tracks[index], view.PanelFor(index)!.Staff.Probe.Track);
        Shot(window, light ? "band-panels-light" : "band-panels-dark");
        window.Close();
    }

    [AvaloniaFact]
    public void ARenameOnlyUndoGivesTheCardsProbeTheCurrentTrack()
    {
        var document = DemoDocument();
        document.MarkClean();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        DocumentEdits.Run(document, project => { project.Tracks[0].Name = "Renamed"; return true; });
        Assert.NotNull(DocumentEdits.Undo(document));
        Assert.Same(document.Project.Tracks[0], view.PanelFor(0)!.Staff.Probe.Track);
        Assert.NotEqual("Renamed", view.PanelFor(0)!.Staff.Probe.Track!.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void PillTogglesHideAndShowAPanel()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var hidden = Enumerable.Range(0, document.Project.Tracks.Count).First(i => !view.ShownTrackIndices.Contains(i));
        view.ToggleShown(hidden);
        Assert.Contains(hidden, view.ShownTrackIndices);
        Assert.Equal(view.ShownTrackIndices.Count, view.VisiblePanelCount);
        view.ToggleShown(hidden);
        Assert.DoesNotContain(hidden, view.ShownTrackIndices);
        Assert.Equal(view.ShownTrackIndices.Count, view.VisiblePanelCount);
        window.Close();
    }

    [AvaloniaFact]
    public void PanelStaffDrawsOnlyItsOwnTrackNotes()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var index = view.ShownTrackIndices[0];
        var panel = view.PanelFor(index)!;
        Assert.Same(document.Project.Tracks[index], panel.Staff.Probe.Track);
        Assert.Equal(index, panel.Staff.Probe.TrackIndex);
        var other = Enumerable.Range(0, document.Project.Tracks.Count).First(i => i != index);
        Assert.NotSame(document.Project.Tracks[other], panel.Staff.Probe.Track);
        view.SelectTrack(index);
        Assert.Equal(index, view.SelectedTrackIndex);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MasterLaneTakesTheRowsFromMasterLaneRules(bool light)
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(doc.Project, doc) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 420, Content = view, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        var top = view.Timeline.RowTop(0);
        var heightBefore = view.Timeline.ContentHeight;
        Assert.Equal(0, view.Timeline.MasterLaneHeight);
        view.ShowMasterLane(true);
        var lane = MasterLaneRules.Height(MasterLaneRules.Rows(true, false), TimelineView.DefaultRowHeight);
        Assert.Equal(lane, view.Timeline.MasterLaneHeight);
        Assert.Equal(top + lane, view.Timeline.RowTop(0));
        // The lane is one more row above track 1; the track rows keep their base height, so the content grows by the lane.
        Assert.Equal(heightBefore + lane, view.Timeline.ContentHeight, 6);
        Assert.Equal(-1, view.Timeline.RowAtY(top + lane - 1));
        Assert.Equal(0, view.Timeline.RowAtY(top + lane + 1));
        Assert.True(MasterLaneRules.OnStrip(lane - 1, view.Timeline.MasterLaneHeight));
        var track = doc.Project.Tracks[0];
        Assert.Contains(Enumerable.Range(0, track.Measures.Count), b => MasterLaneStrip.Plays(track, b));
        Shot(window, light ? "master-light" : "master-dark");
        view.ShowMasterLane(false);
        Assert.Equal(top, view.Timeline.RowTop(0));
        window.Close();
    }

    [Theory]
    [InlineData("#FFE8E8E8", false)]
    [InlineData("#FF202020", false)]
    [InlineData("#FF808080", false)]
    [InlineData("#FF4C9AFF", false)]
    [InlineData("#FFE8E8E8", true)]
    [InlineData("#FF202020", true)]
    [InlineData("#FF808080", true)]
    [InlineData("#FFF2B138", true)]
    public void SectionLabelInkReadsOnTheSectionFill(string hex, bool light)
    {
        var c = Color.Parse(hex);
        var section = new UiColor(c.A, c.R, c.G, c.B);
        var background = AvaloniaPalette.Get("TimelineRow", light);
        Assert.Equal(TimelineView.SectionLabelWhite, TimelineView.LabelInk(section, background));   // always white; the hairline outline carries the contrast
    }

    // The window draws section labels white, and the taming keeps the well-separated palette colours dark enough for that. A chip the person
    // picked exactly skips the taming, and these mid-tone ones (teal, gold, purple, blue) read better with the near-black ink on the dark theme's
    // fill, which is what turned the label black in a dark window: the light theme's WCAG pick, whose dark ink is right on its lighter fills, must
    // not be reached here.
    [Theory]
    [InlineData("#FF4FD1C5")]   // teal
    [InlineData("#FFF2B138")]   // gold
    [InlineData("#FFD0B0F5")]   // purple
    [InlineData("#FF7FB3F5")]   // blue
    public void SectionLabelInkStaysLightOnMidToneSectionFillsInTheDarkTheme(string hex)
    {
        var c = Color.Parse(hex);
        var section = new UiColor(c.A, c.R, c.G, c.B);
        var background = AvaloniaPalette.Get("InstrumentBackground", false);
        Assert.Equal(TimelineView.SectionLabelWhite, TimelineView.LabelInk(section, background));
    }
}
