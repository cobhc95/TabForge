using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the section menu's rows map to handled command ids; Delete section removes the section's bars; Add section here, Rename / recolour and
// Paste section (title and colour back) change the song.
// Does not own: the menu's contents (TimelineMenus) or the bar moves (SectionReorderService).
// Tests: this file.
public sealed class TimelineSectionMenuTests
{
    [Fact]
    public void SectionRowsRunTheirHandledCommands()
    {
        foreach (var command in new[] { TimelineCommand.CopySectionMenu, TimelineCommand.CutSection, TimelineCommand.PasteSectionAfter, TimelineCommand.DuplicateSection, TimelineCommand.DeleteSection,
            TimelineCommand.AddSectionHere, TimelineCommand.RenameSection })
            Assert.Contains(ContextMenuRows.CommandIdOf(new MenuSpec { Command = command }), MainWindowTimelineSections.Commands);
    }

    [Fact]
    public void DeleteSectionRemovesItsBarsFromEveryTrack()
    {
        var project = new SongProject();
        var track = new TrackModel();
        for (var bar = 0; bar < 4; bar++) track.Measures.Add(new MeasureModel { Number = bar + 1 });
        project.Tracks.Add(track);
        var second = new MarkerModel { MeasureIndex = 2, Title = "B" };
        project.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "A" });
        project.Markers.Add(second);

        Assert.True(SectionMenuEdits.Delete(project, second));
        Assert.Equal(2, track.Measures.Count);
        Assert.Single(project.Markers);
    }

    [Fact]
    public void RemoveLabelKeepsBarsAndNotesAndRemovesOnlyThatSection()
    {
        var project = new SongProject();
        var track = new TrackModel();
        for (var bar = 0; bar < 4; bar++) track.Measures.Add(new MeasureModel { Number = bar + 1 });
        project.Tracks.Add(track);
        var second = new MarkerModel { MeasureIndex = 2, Title = "B" };
        var first = new MarkerModel { MeasureIndex = 0, Title = "A" };
        project.Markers.Add(first);
        project.Markers.Add(second);
        Assert.Contains(ContextMenuRows.SectionRemoveLabelId, MainWindowTimelineSections.Commands);

        Assert.True(SectionMenuEdits.RemoveLabel(project, second));
        Assert.Equal(4, track.Measures.Count);
        Assert.Same(first, Assert.Single(project.Markers));
    }

    private static SongProject FourBars()
    {
        var project = new SongProject();
        var track = new TrackModel();
        for (var bar = 0; bar < 4; bar++) track.Measures.Add(new MeasureModel { Number = bar + 1 });
        project.Tracks.Add(track);
        return project;
    }

    [Fact]
    public void AddSectionHereAddsOneSectionPerBar()
    {
        var project = FourBars();
        Assert.True(SectionMenuEdits.AddAt(project, 2, "Chorus", "#FF0000"));
        Assert.False(SectionMenuEdits.AddAt(project, 2, "Again", "#FF0000"));
        Assert.Equal("Chorus", Assert.Single(project.Markers).Title);
        Assert.Equal(2, project.Markers[0].MeasureIndex);
    }

    [Fact]
    public void MovedSectionKeepsItsColourAndMovingBackRestoresIt()
    {
        var project = FourBars();
        Assert.True(SectionMenuEdits.AddAt(project, 0, "Intro", "#2E74B5"));
        Assert.True(SectionMenuEdits.AddAt(project, 2, "Swell", "#2E74B6"));
        var fallback = new Rgba(0x2E, 0x74, 0xB5, 255);
        var palette = new[] { new Rgba(0xD2, 0x3C, 0x3C, 255) };
        Rgba[] ColoursInBarOrder() => SectionColourPresenter.Resolve(SectionListPresenter.InBarOrder(project.Markers), true, fallback, palette);
        string[] TitlesInBarOrder() => SectionListPresenter.InBarOrder(project.Markers).Select(marker => marker.Title).ToArray();
        Dictionary<string, Rgba> ColourByTitle() => TitlesInBarOrder().Zip(ColoursInBarOrder()).ToDictionary(pair => pair.First, pair => pair.Second);
        var before = ColourByTitle();

        Assert.NotNull(SectionReorderService.Move(project, 0, 2));
        Assert.Equal(new[] { "Swell", "Intro" }, TitlesInBarOrder());
        Assert.Equal(before, ColourByTitle());

        Assert.NotNull(SectionReorderService.Move(project, 1, 0));
        Assert.Equal(new[] { "Intro", "Swell" }, TitlesInBarOrder());
        Assert.Equal(before, ColourByTitle());
    }

    [Fact]
    public void DemoSongSectionsKeepTheirColoursWhenOneMovesBeforeAnother()
    {
        var project = TabForge.Presets.FullDemoSongFactory.Create();
        Dictionary<string, Rgba> ColourByTitle() =>
            SectionListPresenter.InBarOrder(project.Markers).Select((marker, i) => (marker.Title, i))
                .ToDictionary(pair => pair.Title, pair => TabForge.Avalonia.Platform.SectionDisplayColours.Resolve(SectionListPresenter.InBarOrder(project.Markers))[pair.i]);
        var before = ColourByTitle();
        var sorted = SectionListPresenter.InBarOrder(project.Markers);
        // Verse 1 already sits directly before Pre-chorus 1 in the demo, so the real reorder moves Pre-chorus 1 before Verse 1.
        var from = sorted.FindIndex(marker => marker.Title == "Pre-chorus 1");
        var insertBefore = sorted.FindIndex(marker => marker.Title == "Verse 1");
        Assert.True(from >= 0 && insertBefore >= 0);

        TabForge.Avalonia.Platform.SectionDisplayColours.FreezeDisplayedColours(project);
        Assert.NotNull(SectionReorderService.MoveWithClips(project, from, insertBefore));

        Assert.Equal(before, ColourByTitle());
    }

    [Fact]
    public void RecolourSpreadsToTheSectionFamilyAndRenameAloneKeepsColours()
    {
        var project = FourBars();
        var first = new MarkerModel { MeasureIndex = 0, Title = "Verse 1", ColorHex = "#111111" };
        var second = new MarkerModel { MeasureIndex = 2, Title = "Verse 2", ColorHex = "#111111" };
        project.Markers.Add(first);
        project.Markers.Add(second);

        Assert.False(SectionMenuEdits.Rename(project.Markers, first, "Verse 1", "#111111", matchSimilar: true));
        Assert.True(SectionMenuEdits.Rename(project.Markers, first, "Verse 1", "#00FF00", matchSimilar: true));
        Assert.Equal("#00FF00", second.ColorHex);
        Assert.True(second.ColorIsExplicit);
        Assert.True(SectionMenuEdits.Rename(project.Markers, second, "Bridge", "#00FF00", matchSimilar: true));
        Assert.Equal("Bridge", second.Title);
    }

    [Fact]
    public void PasteSectionBringsBackTitleAndColour()
    {
        var project = FourBars();
        var verse = new MarkerModel { MeasureIndex = 0, Title = "Verse", ColorHex = "#123456", ColorIsExplicit = true };
        project.Markers.Add(verse);
        project.Markers.Add(new MarkerModel { MeasureIndex = 2, Title = "Chorus" });
        var clip = SectionMenuEdits.Copy(project, verse);

        Assert.NotNull(SectionMenuEdits.PasteAt(project, 4, clip, 0, verse));
        Assert.Equal(6, project.Tracks[0].Measures.Count);
        var pasted = Assert.Single(project.Markers, marker => marker.MeasureIndex == 4);
        Assert.Equal(("Verse", "#123456"), (pasted.Title, pasted.ColorHex));
    }

    [Fact]
    public void CopiedSectionMatchesOnlyItsOwnClip()
    {
        var clip = SectionMenuEdits.Copy(FourBarsWithSection(out var marker), marker);
        var copied = new MainWindowTimelineSections.CopiedSection { Clip = clip, Marker = marker };
        Assert.Same(marker, copied.For(clip));
        Assert.Null(copied.For(null));
    }

    private static SongProject FourBarsWithSection(out MarkerModel marker)
    {
        var project = FourBars();
        marker = new MarkerModel { MeasureIndex = 0, Title = "A" };
        project.Markers.Add(marker);
        return project;
    }
}
