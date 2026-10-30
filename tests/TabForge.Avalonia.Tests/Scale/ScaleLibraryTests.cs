using System.Linq;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the extended scale library and its families (Jazz, World, Exotic, Favourites): interval spot checks, one family per scale, the default ticked families, old saved
//   choices, favourites being considered, the blues riff that needs the Blues family, the search names, and drums never being read.
// Does not own: the popup (PickAnotherListTests, StatusBarScaleTests).
// Tests: this class.
public sealed class ScaleLibraryTests
{
    private static string Notes(string scale, int root = 0) => string.Join(" ", ScaleFamilies.NotesOf(root, scale));

    [Fact]
    public void TheNewScalesHaveTheRightNotes()
    {
        Assert.Equal("C C# D# E F# G# A# ", Notes("Altered") + " ");
        Assert.Equal("C D E F G A A# B", Notes("Bebop Dominant"));
        Assert.Equal("A C D D# E G", Notes("Blues", 9));
        Assert.Equal(new[] { 0, 2, 3, 7, 8 }, MusicTheoryService.Scales["Hirajoshi"]);
        Assert.Equal("E F G# A B C D", Notes("Phrygian Dominant", 4));
        Assert.Equal(new[] { 0, 1, 3, 4, 6, 8, 10 }, MusicTheoryService.Scales["Altered"]);
    }

    [Fact]
    public void EveryScaleBelongsToExactlyOneFamilyAndNoneIsLost()
    {
        foreach (var scale in MusicTheoryService.Scales.Keys)
            Assert.Equal(1, ScaleFamilies.All.Count(f => ScaleFamilies.ScalesOf(f).Contains(scale, System.StringComparer.OrdinalIgnoreCase)));
        foreach (var old in new[] { "Major", "Natural Minor", "Harmonic Minor", "Melodic Minor", "Major Pentatonic", "Minor Pentatonic", "Blues", "Dorian", "Phrygian", "Lydian", "Mixolydian", "Locrian", "Diminished", "Whole Tone" })
            Assert.True(MusicTheoryService.Scales.ContainsKey(old));
        Assert.Equal("Jazz", ScaleFamilies.FamilyOf("Whole Tone"));
        Assert.Equal("World", ScaleFamilies.FamilyOf("Persian"));
    }

    [Fact]
    public void TheDefaultFamiliesAndOldSavedChoicesLoad()
    {
        Assert.Equal(new[] { "Major", "Minor", "Pentatonic", "Blues", "Jazz", "Harmonic minor", "Melodic minor", "Favourites" }, ScaleFamilies.Normalize(new ScaleReadoutSettings().Families));
        var old = new ScaleReadoutSettings { Families = new() { "Major", "Modes" } };   // saved before Jazz, World and Favourites existed
        Assert.Equal(new[] { "Major", "Jazz", "Modes", "Favourites" }, ScaleFamilies.Normalize(old.Families));
        old.Families = old.Families.Where(f => f != "Jazz").ToList();   // the user unticks Jazz later: it stays off
        Assert.DoesNotContain("Jazz", old.Families);
    }

    [Fact]
    public void FavouritesAreConsideredWhenFavouritesIsTickedAndAnyKey()
    {
        var settings = new ScaleReadoutSettings { Families = new() { "Major" }, SeenFamilies = ScaleFamilies.AddedLater.ToList() };
        Assert.DoesNotContain("Hirajoshi", settings.ConsideredScales());
        settings.Favourites.Add("Hirajoshi");
        Assert.DoesNotContain("Hirajoshi", settings.ConsideredScales());   // Favourites chip is off here
        settings.Families = new() { "Major", "Favourites" };
        Assert.Contains("Hirajoshi", settings.ConsideredScales());
    }

    [Fact]
    public void ABluesRiffNeedsTheBluesFamily()
    {
        var histogram = new PitchHistogram();
        foreach (var (pitch, weight) in new[] { (57, 3.0), (60, 1.5), (62, 1.0), (63, 1.0), (64, 1.0), (67, 1.5) }) histogram.Add(pitch, weight);
        var on = new ScaleReadoutSettings { Families = new() { "Major", "Minor", "Pentatonic", "Blues" } };
        var off = new ScaleReadoutSettings { Families = new() { "Major", "Minor", "Pentatonic" } };
        Assert.Equal("Blues", SectionScaleDetector.Rank(histogram, on.ConsideredScales(), false)[0].Scale);
        Assert.NotEqual("Blues", SectionScaleDetector.Rank(histogram, off.ConsideredScales(), false)[0].Scale);
    }

    [Theory]
    [InlineData("altered", "Altered")]
    [InlineData("bebop", "Bebop Dominant")]
    [InlineData("phrygian dominant", "Phrygian Dominant")]
    [InlineData("spanish gypsy", "Phrygian Dominant")]
    [InlineData("hungarian", "Hungarian Minor")]
    [InlineData("minor blues", "Blues")]
    [InlineData("arabic", "Double Harmonic")]
    public void SearchFindsTheNewNamesAndAliases(string query, string scale) =>
        Assert.Contains(new ScaleSearch().Find(query), h => h.Scale == scale);

    [Fact]
    public void DrumTracksNeverChangeWhatAllPitchedTracksRead()
    {
        var song = SectionScaleDetectorTests.TwoSections();
        var engine = new ScaleReadoutEngine();
        var pitched = new ScaleReadoutSettings { ListenTo = ScaleListenTo.Pitched };
        var lead = engine.Compute(song, 5, song.Tracks[0], new ScaleReadoutSettings { ListenTo = ScaleListenTo.Track });
        song.Tracks[1].MidiChannel = 9;   // the bass line is now a percussion channel
        Assert.Equal(lead.Text, engine.Compute(song, 5, song.Tracks[0], pitched).Text);
        Assert.Single(engine.Compute(song, 5, song.Tracks[0], pitched).Context.Tracks);
    }
}
