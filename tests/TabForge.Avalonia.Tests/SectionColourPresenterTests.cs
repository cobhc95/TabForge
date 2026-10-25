using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: characterization of the section colour and section list rules in Core (SectionColourPresenter, SectionListPresenter): family sharing,
/// collision separation, explicit family colours, tame, family names, the row fills and the bar order.
/// Does not own: the WPF list or the Avalonia panel (SectionsPanelViewTests).
/// Tests: this class.
/// </summary>
public class SectionColourPresenterTests
{
    private static readonly Rgba Blue = new(0x2E, 0x74, 0xB5, 255);
    private static readonly Rgba Red = new(0xD2, 0x3C, 0x3C, 255);
    private static readonly Rgba Gold = new(0xE6, 0xB4, 0x3C, 255);

    private static MarkerModel Section(int bar, string title, string colour, bool explicitColour = false) =>
        new() { MeasureIndex = bar, Title = title, ColorHex = colour, ColorIsExplicit = explicitColour };

    [Fact]
    public void FamilyNamesDropNumbersRomanNumeralsAndRepeatMarks()
    {
        Assert.Equal("Verse", SectionColourPresenter.Family("Verse 2"));
        Assert.Equal("Verse", SectionColourPresenter.Family("Verse II"));
        Assert.Equal("Chorus", SectionColourPresenter.Family("Chorus (x2)"));
        Assert.Equal("Intro", SectionColourPresenter.Family("Intro II"));
        Assert.Equal("Intro: Embers", SectionColourPresenter.Family("Intro: Embers"));
        Assert.Equal("", SectionColourPresenter.Family(null));
    }

    [Fact]
    public void FamilyMembersShareTheFirstMembersColour()
    {
        var markers = new List<MarkerModel> { Section(0, "Verse 1", "#2E74B5"), Section(8, "Chorus 1", "#D23C3C"), Section(16, "Verse 2", "#E6B43C") };
        var colours = SectionColourPresenter.Resolve(markers, true, Blue, new[] { Gold });
        Assert.Equal(colours[0], colours[2]);
    }

    [Fact]
    public void WithoutFamilyMatchingEachSectionKeepsItsOwnTamedColour()
    {
        var markers = new List<MarkerModel> { Section(0, "Verse 1", "#2E74B5"), Section(16, "Verse 2", "#E6B43C") };
        var colours = SectionColourPresenter.Resolve(markers, false, Blue, new[] { Gold });
        Assert.Equal(SectionColourPresenter.OwnColour(markers[1], Blue), colours[1]);
        Assert.NotEqual(colours[0], colours[1]);
    }

    [Fact]
    public void LookAlikeFamilyGetsTheNextDistinctColour()
    {
        var markers = new List<MarkerModel> { Section(0, "Intro: Embers", "#2E74B5"), Section(9, "Swell", "#2E74B5") };
        var colours = SectionColourPresenter.Resolve(markers, true, Blue, new[] { Red, Gold });
        Assert.Equal(Blue, colours[0]);
        Assert.Equal(Red, colours[1]);
    }

    [Fact]
    public void ExplicitFamilyColourWinsOverCollisionSeparation()
    {
        var markers = new List<MarkerModel> { Section(0, "Intro", "#2E74B5"), Section(9, "Intro 2", "#E6B43C", explicitColour: true) };
        var colours = SectionColourPresenter.Resolve(markers, true, Blue, new[] { Red });
        Assert.Equal(Gold, colours[0]);
        Assert.Equal(Gold, colours[1]);
    }

    [Fact]
    public void TameLeavesMidTonesAndClampsBrightSaturatedColours()
    {
        Assert.Equal(Blue, SectionColourPresenter.Tame(Blue));
        var toned = SectionColourPresenter.Tame(new Rgba(255, 0, 0, 255));
        Assert.True(toned.R < 255 && toned.R > 0, "Pure red is toned down");
        Assert.Equal(255, toned.A);
    }

    [Fact]
    public void DimmedMatchesTheWhiteAndBlackCases()
    {
        Assert.Equal(new Rgba(204, 204, 204, 255), SectionListPresenter.Dimmed(new Rgba(255, 255, 255, 255)));
        Assert.Equal(new Rgba(0, 0, 0, 128), SectionListPresenter.Dimmed(new Rgba(0, 0, 0, 128)));
    }

    [Fact]
    public void RowFillsUseTheRestHoverAndSelectedAlphas()
    {
        Assert.Equal(SectionListPresenter.RestAlpha, SectionListPresenter.RowFill(Blue, SectionRowState.Rest).A);
        Assert.Equal(SectionListPresenter.HoverAlpha, SectionListPresenter.RowFill(Blue, SectionRowState.Hover).A);
        Assert.Equal(new Rgba(Blue.R, Blue.G, Blue.B, SectionListPresenter.SelectedAlpha), SectionListPresenter.RowFill(Blue, SectionRowState.Selected));
        Assert.Equal(SectionListPresenter.RestOpacity, SectionListPresenter.RowOpacity(SectionRowState.Rest));
        Assert.Equal(1.0, SectionListPresenter.RowOpacity(SectionRowState.Hover));
    }

    [Fact]
    public void RowsFollowBarOrder()
    {
        var markers = new List<MarkerModel> { Section(9, "Swell", "#2E74B5"), Section(0, "Intro", "#2E74B5") };
        Assert.Equal(new[] { "Intro", "Swell" }, SectionListPresenter.InBarOrder(markers).Select(marker => marker.Title));
    }

    [Fact]
    public void DemoSongSectionsResolveWithTheFamilyRule()
    {
        var song = FullDemoSongFactory.Create();
        var sorted = SectionListPresenter.InBarOrder(song.Markers);
        var colours = SectionColourPresenter.Resolve(sorted, true, Blue, new[] { Red, Gold });
        var verseOne = sorted.FindIndex(marker => marker.Title == "Verse 1");
        var verseTwo = sorted.FindIndex(marker => marker.Title == "Verse 2");
        Assert.Equal(colours[verseOne], colours[verseTwo]);
        Assert.Equal(sorted.Count, colours.Length);
    }
}
