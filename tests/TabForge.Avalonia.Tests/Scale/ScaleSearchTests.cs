using System.Linq;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the "Find a scale…" filter: by key and name ("d harm"), by mode name in every key ("dorian"), by alias ("aeolian"), by short names ("minor pent"),
//   by key alone ("c#"), the blank query, no results, and the contains-notes search; and the highlight style / look settings defaults, the old-file load and the opacity clamp.
// Does not own: the popup that shows the list (FretboardScalePopupTests).
// Tests: this class.
public sealed class ScaleSearchTests
{
    private static readonly ScaleSearch Search = new();

    private static string[] Names(string query) => Search.Find(query).Select(h => h.Highlight).ToArray();

    [Fact]
    public void DHarmFindsDHarmonicMinor() => Assert.Equal("D Harmonic Minor", Names("d harm")[0]);

    [Fact]
    public void DorianListsDorianInAllTwelveKeys()
    {
        var hits = Search.Find("dorian");
        Assert.Equal(12, hits.Count(h => h.Scale == "Dorian"));
        Assert.All(hits, h => Assert.Contains("Dorian", h.Scale));
    }

    [Fact]
    public void AeolianFindsTheNaturalMinorScales()
    {
        var hits = Search.Find("aeolian");
        Assert.Equal(12, hits.Count);
        Assert.All(hits, h => Assert.Equal("Natural Minor", h.Scale));
    }

    [Fact]
    public void MinorPentFindsThePentatonicMinors()
    {
        var hits = Search.Find("minor pent");
        Assert.Equal(12, hits.Count);
        Assert.All(hits, h => Assert.Equal("Minor Pentatonic", h.Scale));
    }

    [Fact]
    public void AKeyAloneListsThatKeysScales()
    {
        var hits = Search.Find("c#");
        Assert.Equal(MusicTheoryService.Scales.Count, hits.Count);
        Assert.All(hits, h => Assert.Equal(1, h.Root));
    }

    [Fact]
    public void ABlankQueryListsEveryScaleAndAnUnknownWordNone()
    {
        Assert.Equal(12 * MusicTheoryService.Scales.Count, Search.Find("").Count);
        Assert.Equal(12 * MusicTheoryService.Scales.Count, Search.Find("   ").Count);
        Assert.Empty(Search.Find("zzzz"));
    }

    [Fact]
    public void NoteLettersFindTheScalesThatContainThemAll()
    {
        var hits = Names("D E F G A");
        Assert.Contains("D Natural Minor", hits);
        Assert.Contains("D Dorian", hits);
        Assert.DoesNotContain("D Major", hits);   // D major has F#, not F
    }

    [Fact]
    public void TheFitIsZeroWithoutNotes() =>
        Assert.Equal(0, ScaleSearch.Fit(Search.Find("d harm")[0], new PitchHistogram()));

    [Fact]
    public void ANewSettingsFileDefaultsToDotsAtSixtyPercent()
    {
        var editing = new EditingSettings();
        Assert.Equal(ScaleHighlightStyles.Dots, editing.ScaleHighlightStyle);
        Assert.Equal(60, editing.ScaleHighlightOpacity);
        Assert.True(editing.ScaleEmphasiseRoot);
        Assert.False(editing.ScaleDimOutside);
        Assert.False(editing.ScaleOnlyInView);
        Assert.Equal("Accent", editing.ScaleHighlightColour);
    }

    [Fact]
    public void OldAndOddSettingsLoadAndOpacityIsClamped()
    {
        var old = SettingsValidator.Normalize(new AppSettings { Editing = { ScaleHighlightStyle = "Shaded", ScaleHighlightOpacity = 500 } });
        Assert.Equal(ScaleHighlightStyles.CellTint, old.Editing.ScaleHighlightStyle);
        Assert.Equal(ScaleHighlightStyles.MaxOpacity, old.Editing.ScaleHighlightOpacity);
        var low = SettingsValidator.Normalize(new AppSettings { Editing = { ScaleHighlightStyle = null!, ScaleHighlightOpacity = -3 } });
        Assert.Equal(ScaleHighlightStyles.Dots, low.Editing.ScaleHighlightStyle);
        Assert.Equal(ScaleHighlightStyles.MinOpacity, low.Editing.ScaleHighlightOpacity);
    }

    [Fact]
    public void TheLookRoundTripsThroughTheSettingsFile()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new EditingSettings { ScaleHighlightStyle = ScaleHighlightStyles.Degrees, ScaleHighlightOpacity = 35, ScaleDimOutside = true, ScaleHighlightCustomColour = "#102030" });
        var back = System.Text.Json.JsonSerializer.Deserialize<EditingSettings>(json)!;
        Assert.Equal(ScaleHighlightStyles.Degrees, back.ScaleHighlightStyle);
        Assert.Equal(35, back.ScaleHighlightOpacity);
        Assert.True(back.ScaleDimOutside);
        Assert.Equal("#102030", back.ScaleHighlightCustomColour);
        var missing = System.Text.Json.JsonSerializer.Deserialize<EditingSettings>("{}")!;
        Assert.Equal(ScaleHighlightStyles.Dots, missing.ScaleHighlightStyle);
    }
}
