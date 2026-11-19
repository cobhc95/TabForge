using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

// Owns: the Mix Table's track tabs: which tracks get a tab (BeatMix.TracksAt), the opened track's tab picked, each tab's own values, and the outcome.
// Does not own: the row controls (MixTableColourChooserEffectValuesTests).
// Tests: this class.
public class MixTableTabsTests
{
    private static string NoMoreLies => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TabForge", "samples", "iron_maiden", "no_more_lies.gp3"));

    private static MixTablePresenter Presenter(MixChange? mix) =>
        new(mix, 30, 100, 64, null, 120, Enumerable.Range(0, 128).Select(p => (p, GeneralMidi.NameOf(p), false)));

    [Fact]
    public void TabsAreTheTracksThatChangePlusTheOpenedTrack()
    {
        AlphaTabWarmUp.EnsureReady();
        if (!File.Exists(NoMoreLies)) return;   // the sample is local only (a copyrighted song, not in the repository)
        var song = GuitarProImporter.Import(NoMoreLies, new ImportContext());
        var bass = song.Tracks.FindIndex(t => t.Name == "Bass");
        var tabs = BeatMix.TracksAt(song, 49, 0, bass);   // bar 50: the three guitars go back to distortion; the bass does not change
        Assert.Equal(new[] { 0, 1, 2, bass }, tabs.Select(t => t.Track));
        Assert.Equal(new[] { true, true, true, false }, tabs.Select(t => t.HasChange));
        Assert.Null(BeatMix.OwnMix(song, bass, 49, tabs[3].Cell));   // the bass tab shows no change, not Guitar 1's
    }

    [Fact]
    public void ATabWithoutAChangeShowsTheInstrumentInForceNotTheStartingOne()
    {
        AlphaTabWarmUp.EnsureReady();
        if (!File.Exists(NoMoreLies)) return;   // the sample is local only (a copyrighted song, not in the repository)
        var song = GuitarProImporter.Import(NoMoreLies, new ImportContext());
        Assert.Equal(26, song.Tracks[1].MidiProgram);                         // Guitar 2 starts clean
        Assert.Equal(30, BeatMix.InForce(song, 1, 124, 0).Program);          // by bar 125 the bar 50 and 58 mix points have made it distorted
        Assert.Equal(26, BeatMix.InForce(song, 1, 30, 0).Program);           // and at bar 31 it is still clean
    }

    [Fact]
    public void Bar1OfNoMoreLiesTicksOnlyRealChanges()
    {
        AlphaTabWarmUp.EnsureReady();
        if (!File.Exists(NoMoreLies)) return;   // the sample is local only (a copyrighted song, not in the repository)
        var song = GuitarProImporter.Import(NoMoreLies, new ImportContext());
        var keys = song.Tracks.FindIndex(t => t.Name == "Keyboards");
        var tabs = BeatMix.TracksAt(song, 0, 0, keys);
        Assert.False(tabs.Single(t => t.Track == keys).HasChange);   // Keyboards' bar 1 table repeats its own Strings: no change
        Assert.All(song.Tracks[keys].Measures[0].Cells.Concat(song.Tracks[keys].Measures[0].Voice2Cells), c => Assert.Null(c.Mix));
    }

    [AvaloniaFact]
    public void TheOpenedTracksTabIsPickedAndEachTabKeepsItsOwnValues()
    {
        var guitar = new MixTableTab(0, 0, "1. Guitar", true, Presenter(new MixChange { Program = 30, Volume = 10 }));
        var bass = new MixTableTab(4, 0, "5. Bass", false, Presenter(null));
        var window = new MixTableWindow(new[] { guitar, bass }, 1, 0);
        window.Show();
        Assert.Equal(1, window.SelectedTab);
        Assert.True(window.IsShownSelected(1));
        Assert.False(window.IsShownSelected(0));
        var outcome = window.Outcome(clearSelected: false);
        Assert.Equal(30, outcome.Mixes[0].Mix?.Program);
        Assert.Null(outcome.Mixes[1].Mix);
        window.Close();
    }

    [AvaloniaFact]
    public void TheSelectedTabIsShadedWithTheTracksOwnColour()
    {
        var drums = new MixTableTab(5, 0, "6. Drums", true, Presenter(new MixChange { Program = 0 }), "#3355CC", IsDrums: true);
        var window = new MixTableWindow(new[] { drums }, 0, 0);
        window.Show();
        var tab = window.TabButtons[0];
        Assert.Equal(global::Avalonia.Media.Color.Parse("#3355CC"), ((global::Avalonia.Media.ISolidColorBrush)tab.Background!).Color);
        Assert.Equal(0, window.Outcome(clearSelected: false).Mixes[0].Mix?.Program);   // the kit is kept, not swapped for a piano
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<ComboBox>().First().Items.Cast<object>(), i => (i as string)?.Contains("Piano") == true);
        window.Close();
    }

    [AvaloniaFact]
    public void ClearRemovesOnlyTheSelectedTracksChange()
    {
        var a = new MixTableTab(0, 0, "1. A", true, Presenter(new MixChange { Volume = 4 }));
        var b = new MixTableTab(1, 0, "2. B", true, Presenter(new MixChange { Volume = 12 }));
        var window = new MixTableWindow(new[] { a, b }, 0, 0);
        window.Show();
        var outcome = window.Outcome(clearSelected: true);
        Assert.Null(outcome.Mixes[0].Mix);
        Assert.Equal(12, outcome.Mixes[1].Mix?.Volume);
        window.Close();
    }

    [AvaloniaFact]
    public void ManyTabsScrollAndTheOpenedTabIsBroughtIntoView()
    {
        var tabs = Enumerable.Range(0, 14).Select(i => new MixTableTab(i, 0, $"{i + 1}. Rhythm guitar {i + 1}", true, Presenter(new MixChange { Volume = 8 }))).ToArray();
        var window = new MixTableWindow(tabs, 12, 0);
        window.Show();
        var strip = window.GetLogicalDescendants().OfType<ScrollViewer>().First();
        Assert.True(strip.Extent.Width > strip.Viewport.Width);
        var opened = window.TabButtons[12];
        Assert.True(opened.Bounds.X >= strip.Offset.X - 0.5 && opened.Bounds.Right <= strip.Offset.X + strip.Viewport.Width + 0.5);
        window.Close();
    }
}
