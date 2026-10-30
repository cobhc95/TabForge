using System;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the table check that each Timeline and Tracks row of the live settings reaches the arrangement panes (every pane, not only the last) through
/// TimelineSettingsBinding.Apply, which MainWindowSettings.SettingsChanged calls with the live AppSettings; plus the row height keeping header and lanes aligned.
/// Does not own: the drawing of each effect or the dock side (the overview pane's visibility is checked through its callback).
/// Tests: TimelineSettingsBinding, TimelineView, TrackHeaderColumn.
/// </summary>
public class TimelineSettingsBindingTests
{
    /// <summary>One settings row: the change and the value of the pane property it drives.</summary>
    private sealed record Row(string Name, Action<AppSettings> Change, Func<ArrangementView, object?> Read);

    private static readonly Row[] Rows =
    {
        new("ShowBarNumbers", s => s.Timeline.ShowBarNumbers = false, p => p.Timeline.ShowBarNumbers),
        new("ShowSectionNames", s => s.Timeline.ShowSectionNames = false, p => p.Timeline.ShowSectionNames),
        new("ShowSectionBrackets", s => s.Timeline.ShowSectionBrackets = false, p => p.Timeline.ShowSectionBrackets),
        new("SectionGlowIntensity", s => s.Follow.SectionGlowIntensity = 0.9, p => p.Timeline.SectionGlowIntensity),
        new("SectionBracketThickness", s => s.Appearance.SectionBracketThickness = 20, p => p.Timeline.SectionBracketThickness),
        new("HideEmptyGrid", s => s.Timeline.HideEmptyGrid = false, p => p.Timeline.HideEmptyGrid),
        new("BarGlow", s => s.Timeline.BarGlow = false, p => p.Timeline.BarGlow),
        new("ShowTrackLines", s => s.Timeline.ShowTrackLines = true, p => p.Timeline.ShowTrackLines),
        new("PlayheadStyle", s => s.Timeline.PlayheadStyle = PlayheadStyles.Both, p => p.Timeline.PlayheadStyle),
        new("ShowAddTrackLane", s => s.Timeline.ShowAddTrackLane = false, p => p.AddTrackBarHeight),
        new("TrackRowHeight", s => s.Timeline.TrackRowHeight = 48, p => p.Timeline.RowHeight),
        new("ShowMasterTrack", s => s.Timeline.ShowMasterTrack = true, p => p.Timeline.MasterLaneHeight),
        new("MasterTrackColours", s => s.Timeline.MasterTrackColours = false, p => p.Timeline.MasterLook),
        new("MasterTrackColour", s => { s.Timeline.MasterTrackColours = false; s.Timeline.MasterTrackColour = "#FF0000"; }, p => p.Timeline.MasterLook),
        new("MasterTrackOpacityPercent", s => s.Timeline.MasterTrackOpacityPercent = 40, p => p.Timeline.MasterLook),
        new("MatchSimilarSectionColours", s => s.Timeline.MatchSimilarSectionColours = false, p => p.Timeline.MatchSimilarSectionColours),
        new("ZoomFactor", s => s.Timeline.ZoomFactor = 2, p => p.Timeline.ZoomFactor),
        new("TrackListWidth", s => s.Timeline.TrackListWidth = 400, p => p.Timeline.TrackListNameWidth),
    };

    private static ArrangementView NewPane()
    {
        var document = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        return new ArrangementView(document.Project, document);
    }

    [AvaloniaFact]
    public void EachTimelineSettingReachesEveryPane()
    {
        foreach (var row in Rows)
        {
            var settings = new AppSettings();
            var binding = new TimelineSettingsBinding(settings);
            var first = NewPane();
            var second = NewPane();
            binding.Attach(first);
            binding.Attach(second);
            var before = row.Read(first);
            row.Change(settings);
            binding.Apply(settings);
            Assert.True(!Equals(before, row.Read(first)), row.Name + " did not change the first pane");
            Assert.True(!Equals(before, row.Read(second)), row.Name + " did not change the second pane");
        }
    }

    [AvaloniaFact]
    public void FreshDefaultsKeepTheMasterLaneHidden()
    {
        Assert.False(new AppSettings().Timeline.ShowMasterTrack);
        var binding = new TimelineSettingsBinding(new AppSettings());
        var pane = NewPane();
        binding.Attach(pane);
        Assert.False(pane.Timeline.MasterLaneShown);
        Assert.Equal(0, pane.Timeline.MasterLaneHeight);
    }

    [AvaloniaFact]
    public void RowHeightMovesHeaderAndLanesTogether()
    {
        var settings = new AppSettings();   // the Master lane is hidden by default; this test reads the track rows' geometry without it
        settings.Appearance.Density = "Comfortable";   // Compact is the default spacing (rows x0.85); this test states the 1.0 geometry its numbers are written for
        var binding = new TimelineSettingsBinding(settings);
        var pane = NewPane();
        binding.Attach(pane);
        settings.Timeline.TrackRowHeight = 48;
        binding.Apply(settings);

        Assert.Equal(TimelineView.RulerHeight + TimelineView.SectionHeight + 48, pane.Timeline.RowTop(1), 6);
        var cell = pane.Header.CellRect(1, TrackRowCells.Cell.Mute);
        Assert.InRange(cell.Y, pane.Timeline.RowTop(1), pane.Timeline.RowTop(2));
        var window = new Window { Width = 1300, Height = 900, Content = pane };
        window.Show();
        window.UpdateLayout();
        Assert.Equal(pane.Timeline.Height, pane.Header.Bounds.Height, 1);
        window.Content = null;
        window.Close();
    }

    [AvaloniaFact]
    public void OverviewAndFitRowsCallTheHostOnlyWhenTheyChange()
    {
        var settings = new AppSettings();
        bool? overview = null;
        var fits = 0;
        var binding = new TimelineSettingsBinding(settings, shown => overview = shown, () => fits++);
        binding.Apply(settings);
        Assert.Null(overview);
        Assert.Equal(0, fits);

        settings.Appearance.ShowArrangementOverview = false;
        settings.Timeline.AutoFitTrackList = false;
        binding.Apply(settings);
        Assert.False(overview);
        Assert.Equal(1, fits);
    }

    [AvaloniaFact]
    public void CellsUseTheTamedTrackColourAndTheWpfMuteAndTintValues()
    {
        var raw = TabForge.Models.ColourHex.TryParse("#FF0000", out var parsed) ? parsed : default;
        var tamed = TabForge.Presenters.SectionColourPresenter.Tame(raw);
        var shown = TimelineView.TamedTrackColour("#FF0000", default);
        Assert.NotEqual(raw.R, tamed.R);
        Assert.Equal((tamed.R, tamed.G, tamed.B), (shown.R, shown.G, shown.B));
        Assert.Equal(shown.WithOpacity(0.30).A, (byte)Math.Round(shown.A * TabForge.Avalonia.Platform.TimelineNoteMarks.NotesCellOpacity));

        Assert.Equal(255, TrackRowTint.MutedAlpha(0));
        Assert.Equal((byte)Math.Round(255 * (1 - 0.9)), TrackRowTint.MutedAlpha(100));
        Assert.Equal(0.16, HoverShade.RowOverlayOpacity);
    }

    [AvaloniaFact]
    public void PlayheadStyleRedrawsTheLayerAndTheBarStyleHidesTheLine()
    {
        var pane = NewPane();
        var layer = pane.Timeline.PlayheadLayer;
        var window = new Window { Width = 1300, Height = 900, Content = pane };
        window.Show();
        pane.Timeline.SetPlayhead(1, 0.5);
        window.UpdateLayout();
        using (window.CaptureRenderedFrame()) { }
        var before = layer.RedrawCount;
        pane.Timeline.PlayheadStyle = PlayheadStyles.BarMarker;
        using (window.CaptureRenderedFrame()) { }
        Assert.True(layer.RedrawCount > before);
        Assert.False(PlayheadStyles.ShowsLine(pane.Timeline.PlayheadStyle));
        window.Content = null;
        window.Close();
    }
}
