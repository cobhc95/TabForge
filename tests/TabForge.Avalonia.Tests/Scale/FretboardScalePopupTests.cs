using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the fretboard scale popup: it opens from the Scales button and from the Tools menu command, shares the status bar popup's body and pick, shows Auto or
//   Manual with Back to Auto, has the look controls, and its scale list finds scales (also with no notes in the song) and a choice there is a manual pick.
// Does not own: the filter rules (ScaleSearchTests) or the drawing (ScaleHighlightDrawTests).
// Tests: this class.
public sealed class FretboardScalePopupTests
{
    private static T Find<T>(ScalePanel popup, string name) where T : Control =>
        popup.Frame.Rows.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static string BadgeText(ScalePanel popup) => Find<TextBlock>(popup, "ScaleBadge").Text!;

    private static Window Open(ScalePanel popup)
    {
        var window = new Window { Width = 1200, Height = 700, Content = new Border() };
        window.Show();
        popup.OpenAbove(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void TheFretboardPopupSharesTheBodyAndShowsAutoThenManualWithBackToAuto()
    {
        using var settings = Settings();
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var scale = rig.Status.Scale;
        var popup = new ScalePanel(scale.FretboardHost(true));
        var window = Open(popup);
        Assert.Equal(new[] { "ScaleCandidate0Card", "ScaleCandidate1Card", "ScaleCandidate2Card", "ScalePickAnotherCard" },
            popup.Frame.Rows.GetLogicalDescendants().OfType<RadioCard>().Select(c => c.Name));
        Assert.Equal("Auto", BadgeText(popup));
        Assert.False(Find<Button>(popup, "ScaleBackToAuto").IsVisible);
        Assert.Contains(popup.Frame.Rows.GetLogicalDescendants().OfType<ToggleButton>(), b => b.Name == "ScaleStyleDots" && b.IsChecked == true);
        Find<RadioCard>(popup, "ScaleCandidate1Card").Select();
        Assert.StartsWith("Manual: ", BadgeText(popup));
        Assert.True(Find<Button>(popup, "ScaleBackToAuto").IsVisible);
        Assert.False(scale.Current.IsAuto);
        Find<Button>(popup, "ScaleBackToAuto").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Auto", BadgeText(popup));
        Assert.True(scale.Current.IsAuto);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void APickInTheStatusBarPopupShowsInTheFretboardPopupAndTheOtherWayRound()
    {
        using var settings = Settings();
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var scale = rig.Status.Scale;
        var status = new ScalePanel(scale.BarHost());
        var window = Open(status);
        Find<RadioCard>(status, "ScaleCandidate1Card").Select();
        status.Close();
        var fretboard = new ScalePanel(scale.FretboardHost());
        Assert.StartsWith("Manual: ", BadgeText(fretboard));
        Find<Button>(fretboard, "ScaleBackToAuto").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(scale.Current.IsAuto);
        Assert.EndsWith("auto", rig.Text("StatusScaleText"));
        fretboard.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void TheLookControlsWriteTheEditingSettingsAtOnce()
    {
        using var settings = Settings();
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var popup = new ScalePanel(rig.Status.Scale.FretboardHost(true));
        var window = Open(popup);
        var editing = settings.Settings.Editing;
        Find<ToggleButton>(popup, "ScaleStyleRings").IsChecked = true;
        Assert.Equal(ScaleHighlightStyles.Rings, editing.ScaleHighlightStyle);
        Assert.False(Find<ToggleButton>(popup, "ScaleStyleDots").IsChecked);
        Find<ToggleButton>(popup, "ScaleColourGreen").IsChecked = true;
        Assert.Equal("Green", editing.ScaleHighlightColour);
        Find<ToggleButton>(popup, "ScaleDimOutside").IsChecked = true;
        Assert.True(editing.ScaleDimOutside);
        Find<Slider>(popup, "ScaleOpacity").Value = 35;
        Assert.Equal(35, editing.ScaleHighlightOpacity);
        Find<ToggleButton>(popup, "ScaleShowOn").IsChecked = true;
        Assert.True(editing.ScaleReadout.ShowOnFretboard);
        Find<Button>(popup, "ScaleClearHighlight").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(editing.ScaleHighlight);
        Assert.False(editing.ScaleReadout.ShowOnFretboard);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void TheScaleListFiltersAsYouTypeAndAChoiceIsAManualPickThatLightsTheFretboard()
    {
        using var settings = Settings();
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var scale = rig.Status.Scale;
        var window = new Window { Width = 1200, Height = 700, Content = new Border() };
        window.Show();
        Assert.True(scale.OpenForFretboard(window));
        var popup = scale.Popup!;
        Dispatcher.UIThread.RunJobs();
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        Dispatcher.UIThread.RunJobs();
        popup.SetQuery("d harm");
        Assert.Equal("D Harmonic Minor", popup.Hits[0].Highlight);
        var harmRows = ((System.Collections.IEnumerable)Find<ListBox>(popup, "ScaleSearchResults").ItemsSource!).Cast<object>().Select(o => o.ToString()!).ToList();
        Assert.Equal("Harmonic minor", harmRows[0]);
        Assert.Contains("%", harmRows[1]);
        popup.PickSelected();
        Assert.Equal("D Harmonic Minor", scale.Current.Pick);
        Assert.Equal("Manual: D harmonic minor", BadgeText(popup));
        Assert.Equal("D Harmonic Minor", settings.Settings.Editing.ScaleHighlight);
        Assert.True(settings.Settings.Editing.ScaleReadout.ShowOnFretboard);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void ASongWithoutNotesSaysSoAndTheListStillWorks()
    {
        var song = new SongProject();
        song.Tracks.Add(NewTrackFactory.CreateTrack(song, TrackKind.Guitar));
        using var settings = Settings();
        using var rig = new StatusBarTransportTests.Rig(song: song, settings: settings);
        var popup = new ScalePanel(rig.Status.Scale.FretboardHost());
        var window = Open(popup);
        Assert.Contains(popup.Frame.Rows.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "No notes yet: pick a scale to compose in");
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        popup.SetQuery("dorian");
        Assert.Equal(12, popup.Hits.Count(h => h.Scale == "Dorian"));
        var dorianRows = ((System.Collections.IEnumerable)Find<ListBox>(popup, "ScaleSearchResults").ItemsSource!).Cast<object>().Select(o => o.ToString()!).ToList();
        Assert.Contains("Modes", dorianRows);
        Assert.DoesNotContain("%", dorianRows[1]);
        popup.SetQuery("zzz");
        Assert.Empty(popup.Hits);
        popup.SetQuery("");
        Assert.Equal(12 * settings.Settings.Editing.ScaleReadout.ConsideredScales().Count, popup.Hits.Count);
        var blankRows = ((System.Collections.IEnumerable)Find<ListBox>(popup, "ScaleSearchResults").ItemsSource!).Cast<object>().Select(o => o.ToString()!).ToList();
        Assert.Contains("Major", blankRows);
        Assert.DoesNotContain("Modes", blankRows);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void TheScalesButtonAndTheToolsCommandOpenThePopupAndTheOldWindowIsGone()
    {
        var project = DemoSongFactory.Create();
        var main = new MainWindow(first: new SongDocument(() => null) { Project = project }) { Width = 1500, Height = 960 };
        main.Show();
        for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); main.UpdateLayout(); }
        var scales = main.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ScalesButton");
        Assert.True(scales.IsVisible);
        DialogTestKit.Click(scales);
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.ScaleReadout!.Popup!.Frame.IsOpen);
        Assert.Empty(main.OwnedWindows);
        main.ScaleReadout.Popup.Close();
        Assert.Contains("Tools.ScaleFinder", MainWindowScaleFinder.Commands);
        main.Close();
    }

    private static MainWindowSettings Settings() => MainWindowSettings.Open(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-scalepop-" + System.Guid.NewGuid().ToString("N") + ".json"));
}
