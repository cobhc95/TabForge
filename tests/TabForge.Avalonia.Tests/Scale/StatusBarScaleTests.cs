using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the status bar scale readout: its text and context while the playhead moves between sections, the popup (candidate cards, pick another, segmented buttons,
//   chips, options), a pick that sticks and Auto that returns, a kept pick saved in the song, the fretboard scale highlight following the readout, and the settings rows.
// Does not own: the detection itself (SectionScaleDetectorTests).
// Tests: this class.
public sealed class StatusBarScaleTests
{
    private static string ScaleText(StatusBarTransportTests.Rig rig) => rig.Text("StatusScaleText");

    private static T Find<T>(ScaleReadoutPopup popup, string name) where T : Control =>
        popup.Frame.Rows.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static Window Open(ScaleReadoutPopup popup)
    {
        var window = new Window { Width = 900, Height = 600, Content = new Border() };
        window.Show();
        popup.OpenAbove(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void TheReadoutShowsTheSectionScaleAndFollowsThePlayheadIntoTheNextSection()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        Assert.Equal("C major ▾ auto", ScaleText(rig));
        rig.Play();
        rig.At(rig.Timeline.Bars[1].StartMs + 100);
        Assert.Equal("C major ▾ auto", ScaleText(rig));
        rig.At(rig.Timeline.Bars[5].StartMs + 100);   // bar 6 is in the Chorus
        Assert.Equal("A minor ▾ auto", ScaleText(rig));
        rig.Session.Stop();
        Assert.Equal("C major ▾ auto", ScaleText(rig));   // stopped: the cursor bar again
    }

    [AvaloniaFact]
    public void ThePopupListsTheThreeCandidatesAndPickAnotherAndAPickSticksUntilAuto()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var scale = rig.Status.Scale;
        var popup = new ScaleReadoutPopup(scale);
        var window = Open(popup);
        var cards = popup.Frame.Rows.GetLogicalDescendants().OfType<RadioCard>().ToList();
        Assert.Equal(new[] { "ScaleCandidate0Card", "ScaleCandidate1Card", "ScaleCandidate2Card", "ScalePickAnotherCard" }, cards.Select(c => c.Name));
        Assert.True(cards[0].IsSelected);
        Assert.Contains(popup.Frame.Rows.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Verse  ·  Lead  ·  bars 1–4");
        Assert.False(Find<Button>(popup, "ScaleAuto").IsEnabled);
        cards[1].Select();   // the second candidate
        Assert.False(scale.Current.IsAuto);
        Assert.EndsWith("▾", ScaleText(rig));
        Assert.True(Find<Button>(popup, "ScaleAuto").IsEnabled);
        var picked = ScaleText(rig);
        rig.Status.Refresh();
        Assert.Equal(picked, ScaleText(rig));   // a refresh keeps the pick
        Find<Button>(popup, "ScaleAuto").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("C major ▾ auto", ScaleText(rig));
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void PickAnotherUsesTheKeyAndScaleDropDownsAndTheLibraryHasEveryScale()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var popup = new ScaleReadoutPopup(rig.Status.Scale);
        var window = Open(popup);
        Assert.Equal(12, Find<ComboBox>(popup, "ScalePickKey").ItemCount);
        Assert.Equal(MusicTheoryService.Scales.Count, Find<ComboBox>(popup, "ScalePickScale").ItemCount);
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        var scale = Find<ComboBox>(popup, "ScalePickScale");
        scale.SelectedIndex = MusicTheoryService.Scales.Keys.ToList().IndexOf("Dorian");
        Assert.Contains("dorian", ScaleText(rig));
        Assert.DoesNotContain("auto", ScaleText(rig));
        Assert.True(Find<RadioCard>(popup, "ScalePickAnotherCard").IsSelected);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void ASessionPickBelongsToItsSectionAndAKeptPickIsSavedInTheSongAsOneUndoStep()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var scale = rig.Status.Scale;
        var popup = new ScaleReadoutPopup(scale);
        var window = Open(popup);
        var song = rig.Document.Project;
        Assert.Null(song.Markers[0].KeptScales);
        Find<CheckBox>(popup, "ScaleKeepChoice").IsChecked = true;
        Assert.True(scale.KeepChoice);
        Find<RadioCard>(popup, "ScaleCandidate1Card").Select();
        Assert.Single(song.Markers[0].KeptScales!);   // saved in the Verse
        Assert.Null(song.Markers[1].KeptScales);
        var kept = ScaleText(rig);
        rig.Play();
        rig.At(rig.Timeline.Bars[5].StartMs + 100);
        Assert.Equal("A minor ▾ auto", ScaleText(rig));   // the Chorus is untouched
        rig.At(rig.Timeline.Bars[1].StartMs + 100);
        Assert.Equal(kept, ScaleText(rig));   // back in the Verse the kept pick returns
        Find<Button>(popup, "ScaleAuto").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(song.Markers[0].KeptScales);
        Assert.Equal("C major ▾ auto", ScaleText(rig));
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void TheSegmentedButtonsAndChipsWriteTheScaleSettingsAndReadAgain()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var scale = rig.Status.Scale;
        var popup = new ScaleReadoutPopup(scale);
        var window = Open(popup);
        Assert.True(Find<ToggleButton>(popup, "ReadFromSection").IsChecked);
        Assert.True(Find<ToggleButton>(popup, "ListenToTrack").IsChecked);
        Find<ToggleButton>(popup, "ReadFromSong").IsChecked = true;
        Assert.Equal(ScaleReadFrom.Song, scale.Settings.ReadFrom);
        Assert.Contains("Whole song", scale.Current.Context.Text);
        Find<ToggleButton>(popup, "ReadFromKey").IsChecked = true;
        Assert.Equal("C major ▾", ScaleText(rig));   // the song key signature, no detection
        Find<ToggleButton>(popup, "ReadFromSection").IsChecked = true;
        Find<ToggleButton>(popup, "ListenToPitched").IsChecked = true;
        Assert.Contains("All pitched tracks", scale.Current.Context.Text);
        var chips = popup.Frame.Rows.GetLogicalDescendants().OfType<ToggleButton>().Where(b => b.Name?.StartsWith("Family", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(ScaleFamilies.All.Count, chips.Count);
        Assert.Equal(ScaleFamilies.DefaultOn.Count, chips.Count(c => c.IsChecked == true));
        Assert.False(Find<ToggleButton>(popup, "FamilyModes").IsChecked);
        Find<ToggleButton>(popup, "FamilyModes").IsChecked = true;
        Assert.Contains(ScaleFamilies.Modes, scale.Settings.Families);
        Find<CheckBox>(popup, "ScalePreferCommon").IsChecked = false;
        Assert.False(scale.Settings.PreferCommon);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void TheFretboardHighlightFollowsTheReadoutUnlessTheLinkIsOff()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-scale-" + Guid.NewGuid().ToString("N") + ".json");
        using var settings = MainWindowSettings.Open(path);
        try
        {
            Assert.False(settings.Settings.Editing.ScaleReadout.ShowOnFretboard);   // off by default: the fretboard never follows the readout unasked
            settings.Settings.Editing.ScaleReadout.ShowOnFretboard = true;
            using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
            var editing = settings.Settings.Editing;
            Assert.Equal("C Major", editing.ScaleHighlight);
            rig.Play();
            rig.At(rig.Timeline.Bars[5].StartMs + 100);
            Assert.Equal("A Natural Minor", editing.ScaleHighlight);
            editing.ScaleReadout.ShowOnFretboard = false;
            rig.At(rig.Timeline.Bars[1].StartMs + 100);
            Assert.Equal("A Natural Minor", editing.ScaleHighlight);   // the link is off: the highlight stays
            Assert.Equal("C major ▾ auto", ScaleText(rig));
            editing.ScaleReadout.ShowOnFretboard = true;
            rig.Status.Refresh();
            Assert.Equal("C Major", editing.ScaleHighlight);
        }
        finally
        {
            Dispatcher.UIThread.RunJobs();
            File.Delete(path);
        }
    }

    [Fact]
    public void ThePreferencesRowsExistAndSearchFindsThemByScaleAndKey()
    {
        var settings = new AppSettings();
        var rows = SettingsCatalog.Build(settings).Where(r => r.Key.StartsWith("scalereadout.", StringComparison.Ordinal)).ToList();
        Assert.Equal(5 + ScaleFamilies.All.Count, rows.Count);
        var entries = SettingsCatalog.Registry(rows, new HotkeySettings());
        foreach (var query in new[] { "scale", "key", "scale readout", "status bar" })
            Assert.Contains(entries, e => e.Id == "scalereadout.readfrom" && SettingsCatalog.Matches(e, query));
        Assert.Contains(entries, e => e.Id == "scalereadout.family.pentatonic" && SettingsCatalog.Matches(e, "pentatonic"));
        rows.Single(r => r.Key == "scalereadout.family.modes").Set(true);
        Assert.Contains(ScaleFamilies.Modes, settings.Editing.ScaleReadout.Families);
        rows.Single(r => r.Key == "scalereadout.readfrom").Set("Song key only");
        Assert.Equal(ScaleReadFrom.Key, settings.Editing.ScaleReadout.ReadFrom);
        rows.Single(r => r.Key == "scalereadout.listento").Set("All pitched tracks");
        Assert.Equal(ScaleListenTo.Pitched, settings.Editing.ScaleReadout.ListenTo);
    }
}
