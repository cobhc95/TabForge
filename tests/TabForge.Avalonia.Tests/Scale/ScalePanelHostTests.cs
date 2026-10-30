using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the master scale panel across its three hosts: the same shared sections, extras only in their own host, the muted sections with their links (and their
//   disabled state), the one shared pick of the status bar, the fretboard and the piano roll, the piano roll host editing the fold, the highlight and the lock the toolbar shows,
//   Keep my choice only on the status bar, equal fits ranking by confidence, and no second implementation of the old popups left in the source.
// Does not own: the individual sections (StatusBarScaleTests, PickAnotherListTests).
// Tests: this class.
public sealed class ScalePanelHostTests
{
    private static T Find<T>(ScalePanel panel, string name) where T : Control =>
        panel.Frame.Rows.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static bool Has(ScalePanel panel, string name) => panel.Frame.Rows.GetLogicalDescendants().OfType<Control>().Any(c => c.Name == name);

    private static Window Open(ScalePanel panel)
    {
        var window = new Window { Width = 1200, Height = 700, Content = new Border() };
        window.Show();
        panel.OpenAbove(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static (Window Window, PianoRollView View, EditingSettings Editing, SongDocument Document) Roll()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var editing = new EditingSettings();
        var view = new PianoRollView(new PianoRollHost { Document = () => document, Editing = () => editing });
        var window = new Window { Width = 2000, Height = 700, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (window, view, editing, document);
    }

    [AvaloniaFact]
    public void TheThreeHostsBuildTheSameSharedSectionsAndOnlyTheirOwnExtras()
    {
        using var settings = MainWindowSettings.Open(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-panelhosts-" + System.Guid.NewGuid().ToString("N") + ".json"));
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var scale = rig.Status.Scale;
        var bar = new ScalePanel(scale.BarHost());
        var fret = new ScalePanel(scale.FretboardHost(true));
        var (window, view, _, _) = Roll();
        var roll = new ScalePanel(view.ScaleHost);
        try
        {
            Assert.Equal(ScalePanelModel.SharedSections, bar.SectionIds);
            Assert.Equal(bar.SectionIds, fret.SectionIds);
            Assert.Equal(bar.SectionIds, roll.SectionIds);
            Assert.Equal(new[] { "OPTIONS" }, bar.ExtraTitles);
            Assert.Equal(new[] { "FRETBOARD LOOK" }, fret.ExtraTitles);
            Assert.Equal(new[] { "PIANO ROLL" }, roll.ExtraTitles);
            Assert.Equal(new[] { "FRETBOARD LOOK", "PIANO ROLL OPTIONS" }, bar.GreyedTitles);
            Assert.Equal(new[] { "PIANO ROLL OPTIONS" }, fret.GreyedTitles);
            Assert.Equal(new[] { "FRETBOARD LOOK" }, roll.GreyedTitles);
            Assert.True(Has(bar, "ScaleKeepChoice"));
            Assert.False(Has(fret, "ScaleKeepChoice"));
            Assert.False(Has(roll, "ScaleKeepChoice"));
            Assert.True(Has(fret, "ScaleDone"));
            Assert.False(Has(bar, "ScaleDone"));
            Assert.True(Has(roll, "PianoRollFoldScale"));
            Assert.False(Has(bar, "PianoRollFoldScale"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AGreyedSectionLinksToTheOtherHostAndIsDisabledWhenThatAreaIsNotOnScreen()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var scale = rig.Status.Scale;
        var panel = new ScalePanel(scale.BarHost());
        var window = Open(panel);
        var link = Find<Button>(panel, "ScaleOpenFretboard");
        Assert.False(link.IsEnabled);
        Assert.Equal("Open the fretboard first", link.Content);
        Assert.False(Find<Button>(panel, "ScaleOpenPianoRoll").IsEnabled);
        panel.Close();
        var opened = 0;
        scale.Links.FretboardAvailable = () => true;
        scale.Links.OpenFretboard = () => opened++;
        var again = new ScalePanel(scale.BarHost());
        var second = Open(again);
        var enabled = Find<Button>(again, "ScaleOpenFretboard");
        Assert.True(enabled.IsEnabled);
        Assert.Equal("Open the fretboard look", enabled.Content);
        enabled.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, opened);
        second.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void ThePickIsSharedByTheStatusBarAndTheFretboardHosts()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var scale = rig.Status.Scale;
        var bar = scale.BarHost();
        var fret = scale.FretboardHost();
        var match = bar.Current.Top[1];
        fret.Pick(match);
        Assert.Equal(match.Highlight, bar.Current.Pick);
        Assert.Equal(match.Highlight, fret.Current.Pick);
        bar.UseAuto();
        Assert.Null(fret.Current.Pick);
    }

    [AvaloniaFact]
    public void ThePianoRollHostEditsTheFoldTheHighlightAndTheLockTheToolbarShows()
    {
        var (window, view, editing, document) = Roll();
        var panel = new ScalePanel(view.ScaleHost);
        var open = Open(panel);
        try
        {
            Find<ToggleButton>(panel, "PianoRollFoldScale").IsChecked = true;
            Assert.Equal(PianoRollScale.FoldScale, PianoRollScale.RowsOf(document.Project));   // SHOW ROWS is saved in the song
            Assert.DoesNotContain(view.GetLogicalDescendants().OfType<ComboBox>(), c => ReferenceEquals(c.ItemsSource, PianoRollScale.Folds));   // the bar has no fold dropdown any more
            Assert.True(Find<ToggleButton>(panel, "PianoRollFoldScale").IsChecked);   // the panel was rebuilt and shows it
            Find<CheckBox>(panel, "PianoRollScaleLock").IsChecked = true;
            Assert.True(editing.PianoRollScaleLock);
            Assert.True(view.GetLogicalDescendants().OfType<ToggleButton>().First(b => b.Name == "PianoRollLockScale").IsChecked);
            Find<CheckBox>(panel, "PianoRollScaleShown").IsChecked = true;
            Assert.True(editing.PianoRollScaleShown);
        }
        finally { open.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public void ADMinorAndAFMajorShowTheSameFitButDMinorHasTheHigherConfidenceAndIsFirstAndEqualsAuto()
    {
        var notes = new List<PianoRollNote>();
        var beat = 0.0;
        foreach (var pitch in new[] { 62, 64, 65, 67, 69, 70, 72, 69, 65, 62, 69, 62 })   // D E F G A Bb C, centred on D and A, starting and ending on D
        {
            notes.Add(new PianoRollNote(beat, 1, pitch, 100));
            beat++;
        }
        var editing = new EditingSettings();
        var host = new NotesHost(editing, notes);
        var result = host.Current;
        Assert.Equal(2, result.Top[0].Root);
        Assert.Equal("Natural Minor", result.Top[0].Scale);
        var histogram = host.Histogram!;
        var hits = new ScaleSearch().Find("");
        var dMinor = hits.First(h => h.Root == 2 && h.Scale == "Natural Minor");
        var fMajor = hits.First(h => h.Root == 5 && h.Scale == "Major");
        Assert.Equal(ScaleSearch.Fit(dMinor, histogram), ScaleSearch.Fit(fMajor, histogram), 6);   // the same seven notes
        var ranked = SectionScaleDetector.Rank(histogram, editing.ScaleReadout.ConsideredScales(), editing.ScaleReadout.PreferCommon).ToList();
        var d = ranked.First(m => m.Root == 2 && m.Scale == "Natural Minor");
        var f = ranked.First(m => m.Root == 5 && m.Scale == "Major");
        Assert.True(d.Confidence > f.Confidence);
        Assert.True(ranked.IndexOf(d) < ranked.IndexOf(f));
        Assert.Equal(result.Top[0], result.Effective);   // Auto is D minor
        var panel = new ScalePanel(host);
        var window = Open(panel);
        Find<RadioCard>(panel, "ScalePickAnotherCard").Select();
        var rows = ((System.Collections.IEnumerable)Find<ListBox>(panel, "ScaleSearchResults").ItemsSource!).Cast<object>().Select(o => o.ToString()!).ToList();
        var dRow = rows.First(r => r.StartsWith("D Natural Minor  ·"));
        var fRow = rows.First(r => r.StartsWith("F Major  ·"));
        Assert.Equal(dRow.Split("  ·  ").Last(), fRow.Split("  ·  ").Last());   // the same fit text
        Assert.True(rows.IndexOf(dRow) < rows.IndexOf(fRow));
        panel.Close();
        window.Close();
    }

    // A host over a plain list of notes (no window), for the ranking test.
    private sealed class NotesHost : IScalePanelHost
    {
        private readonly EditingSettings _editing;
        private readonly List<PianoRollNote> _notes;
        public NotesHost(EditingSettings editing, List<PianoRollNote> notes) => (_editing, _notes) = (editing, notes);
        private string? _pick;
        public ScaleReadoutResult Current => ScalePanelModel.PianoRollReadout(_notes, _pick, null, Settings, "Test");
        public string ContextLine => "Test";
        public ScaleReadoutSettings Settings => _editing.ScaleReadout;
        public bool Flats => false;
        public void Pick(ScaleMatch scale) { _pick = scale.Highlight; Refreshed?.Invoke(); }
        public void UseAuto() { _pick = PianoRollScale.Auto; Refreshed?.Invoke(); }
        public void SettingChanged() => Refreshed?.Invoke();
        public event System.Action? Refreshed;
        public PitchHistogram? Histogram => PianoRollScale.HistogramOf(_notes);
        public bool HasSelection => false;
        public bool TwoColumns => false;
        public IReadOnlyList<ScaleExtraSection> Extras => System.Array.Empty<ScaleExtraSection>();
        public IReadOnlyList<ScaleGreyedSection> Greyed => System.Array.Empty<ScaleGreyedSection>();
    }

    [AvaloniaFact]
    public void TheOldPopupClassesAreGoneAndTheDetectedAndPickAnotherCardsAreBuiltOnlyInTheMasterPanel()
    {
        var root = System.IO.Path.Combine(TestRepo.CheckoutRoot(), "Source", "src");
        var files = System.IO.Directory.EnumerateFiles(root, "*.cs", System.IO.SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("/obj/") && !f.Contains("\\bin\\"))
            .ToList();
        var old = new[] { "ScaleReadoutPopup", "IScalePopupHost", "PianoRollScalePopup", "PianoRollScaleCandidate", "PianoRollScalePickAnother" };
        Assert.DoesNotContain(files, f => old.Any(name => System.IO.File.ReadAllText(f).Contains(name)));
        var builders = files.Where(f => System.IO.File.ReadAllText(f).Contains("\"ScalePickAnother\"") || System.IO.File.ReadAllText(f).Contains("\"ScaleCandidates\""))
            .Select(System.IO.Path.GetFileName).ToList();
        Assert.Equal(new[] { "ScalePanel.cs" }, builders);
    }
}
