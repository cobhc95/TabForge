using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Avalonia.Platform.Controls;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the scale finish items: Selection read-from only with a selection and limited to it, the status bar More link, swatch colours that persist,
//   only-in-view using the visible frets, grouped family headers that respect ticked families with search across all, and no stale Likely-scales docs.
// Does not own: detection (SectionScaleDetectorTests) or drawing (ScaleHighlightDrawTests).
// Tests: this class.
public sealed class ScaleFinishTests
{
    private static T Find<T>(ScalePanel popup, string name) where T : Control =>
        popup.Frame.Rows.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static Window Open(ScalePanel popup)
    {
        var window = new Window { Width = 900, Height = 600, Content = new Border() };
        window.Show();
        popup.OpenAbove(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static MainWindowSettings Settings() => MainWindowSettings.Open(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-scalefin-" + System.Guid.NewGuid().ToString("N") + ".json"));

    [AvaloniaFact]
    public void SelectionReadFromOnlyShowsWithASelectionAndLimitsDetectionToIt()
    {
        using var settings = Settings();
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var scale = rig.Status.Scale;
        var plain = new ScalePanel(scale.BarHost());
        var w0 = Open(plain);
        Assert.Null(plain.Frame.Rows.GetLogicalDescendants().OfType<ToggleButton>().SingleOrDefault(b => b.Name == "ReadFromSelection"));
        plain.Close();
        w0.Close();
        // Select the Chorus bars (4-7) while the cursor stays in the Verse.
        rig.Document.Selection.SetRange(0, 4, 7, SelectionOrigin.Command);
        rig.Status.Refresh();
        Assert.True(scale.HasSelection);
        var popup = new ScalePanel(scale.BarHost());
        var window = Open(popup);
        var selection = popup.Frame.Rows.GetLogicalDescendants().OfType<ToggleButton>().Single(b => b.Name == "ReadFromSelection");
        Assert.Equal("Selection", selection.Content as string);
        selection.IsChecked = true;
        Assert.Equal(ScaleReadFrom.Selection, scale.Settings.ReadFrom);
        Assert.Contains("Selection", scale.Current.Context.Text);
        Assert.Equal("A minor ▾ auto", rig.Text("StatusScaleText"));
        popup.Close();
        window.Close();
    }

    [Fact]
    public void SelectionNarrowsTheEngineToThoseBars()
    {
        var song = SectionScaleDetectorTests.TwoSections();
        var engine = new ScaleReadoutEngine();
        var settings = new ScaleReadoutSettings { ReadFrom = ScaleReadFrom.Selection };
        var verse = engine.Compute(song, 1, song.Tracks[0], settings, (4, 7));
        Assert.Equal("A minor ▾ auto", verse.Text);
        Assert.Contains("Selection", verse.Context.Text);
    }

    [AvaloniaFact]
    public void StatusBarPopupHasTheMoreScalesSearchLink()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var popup = new ScalePanel(rig.Status.Scale.BarHost());
        var window = Open(popup);
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(Find<TextBox>(popup, "ScaleSearchBox"));
        Assert.NotNull(Find<ListBox>(popup, "ScaleSearchResults"));
        popup.SetQuery("d harm");
        popup.PickSelected();
        Assert.Equal("D Harmonic Minor", rig.Status.Scale.Current.Pick);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void SwatchesSelectPersistAndCustomRoundTrips()
    {
        using var settings = Settings();
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var popup = new ScalePanel(rig.Status.Scale.FretboardHost());
        var window = Open(popup);
        var editing = settings.Settings.Editing;
        foreach (var name in new[] { "Accent", "Green", "Amber", "Red", "Grey", "Custom" })
            Assert.NotNull(popup.Frame.Rows.GetLogicalDescendants().OfType<ToggleButton>().Single(b => b.Name == "ScaleColour" + name));
        Find<ToggleButton>(popup, "ScaleColourAmber").IsChecked = true;
        Assert.Equal("Amber", editing.ScaleHighlightColour);
        Find<ToggleButton>(popup, "ScaleColourGreen").IsChecked = true;
        Assert.Equal("Green", editing.ScaleHighlightColour);
        editing.ScaleHighlightColour = "Custom";
        editing.ScaleHighlightCustomColour = "#102030";
        popup.Close();
        window.Close();
        var reopened = new ScalePanel(rig.Status.Scale.FretboardHost());
        var window2 = Open(reopened);
        Assert.True(Find<ToggleButton>(reopened, "ScaleColourCustom").IsChecked);
        reopened.Close();
        window2.Close();
    }

    [AvaloniaFact]
    public void OnlyInViewUsesTheVisibleFrets()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        string Draw(bool onlyInView)
        {
            var view = new TabForge.Avalonia.Platform.InstrumentPanelView { Kind = TabForge.Avalonia.Platform.InstrumentPanelKind.Fretboard };
            view.Show(track, System.Array.Empty<TabForge.Presenters.InstrumentLitNotes.Lit>());
            view.Measure(new Size(1000, 170));
            view.Arrange(new Rect(0, 0, 1000, 170));
            var app = new AppSettings { Editing = { ScaleHighlight = "D Natural Minor", ScaleOnlyInView = onlyInView, FretboardFrets = 12 } };
            view.Look = TabForge.Avalonia.Platform.FretboardSettingsBinding.Look.From(app);
            var d = new InstrumentFretNumberTests.RecordingDrawContext();
            view.Draw(d, light: false);
            return d.Ellipses.Count + "/" + d.Rects.Count;
        }
        // The pane shows all frets, so limiting to the frets in view keeps every mark.
        Assert.Equal(Draw(false), Draw(true));
    }

    [AvaloniaFact]
    public void GroupedListShowsFamilyHeadersAndSearchFindsAcrossFamilies()
    {
        using var settings = Settings();
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var popup = new ScalePanel(rig.Status.Scale.FretboardHost());
        var window = Open(popup);
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        popup.SetQuery("");
        var blank = ((System.Collections.IEnumerable)Find<ListBox>(popup, "ScaleSearchResults").ItemsSource!).Cast<object>().Select(o => o.ToString()!).ToList();
        Assert.Contains("Major", blank);
        Assert.Contains("Harmonic minor", blank);
        Assert.DoesNotContain("Modes", blank);
        popup.SetQuery("dorian");
        Assert.Equal(12, popup.Hits.Count(h => h.Scale == "Dorian"));
        var searched = ((System.Collections.IEnumerable)Find<ListBox>(popup, "ScaleSearchResults").ItemsSource!).Cast<object>().Select(o => o.ToString()!).ToList();
        Assert.Contains("Modes", searched);
        popup.Close();
        window.Close();
    }

    [Fact]
    public void NoCurrentDocStillDescribesTheLikelyScalesTab()
    {
        var repo = ArchitectureScan.RepoRoot();
        foreach (var rel in new[]
        {
            "Source/docs/guides/TOOLS_AND_HOTKEYS.md",
            "Source/docs/tutorial/detailed/04-read-tab.md",
            "Source/docs/README.md",
        })
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(repo, rel));
            Assert.DoesNotContain("Likely scales", text);
            Assert.DoesNotContain("*Show on fretboard*", text);
        }
    }
}
