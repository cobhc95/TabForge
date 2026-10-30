using System.Linq;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the ONE scale pick shared by the status bar, the fretboard and the piano roll (a pick in any of the three shows in the other two at once), the piano roll toolbar's
//   scale button label following a status bar pick, the migration of an old piano roll pick, the piano roll features following the shared effective scale, and the status bar
//   options: the fretboard tick bound to the fretboard look's On / Off, plus the footnote.
// Does not own: the panel sections (ScalePanelHostTests) or the readout maths (StatusBarScaleTests).
// Tests: PianoRollView, StatusScaleReadout, StatusBarScaleHost.
public sealed class PianoRollSharedScaleTests
{
    private static (StatusBarTransportTests.Rig Rig, Window Window, PianoRollView View, EditingSettings Editing) Setup(EditingSettings? editing = null)
    {
        var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        editing ??= new EditingSettings { PianoRollScalePickMigrated = true };
        var view = new PianoRollView(new PianoRollHost { Document = () => rig.Document, Editing = () => editing }) { SharedReadout = () => rig.Status.Scale };
        var window = new Window { Width = 2000, Height = 700, Content = view };
        window.Show();
        var track = rig.Document.Project.FirstNotationTrack;
        rig.Status.Scale.Show(rig.Document.Project, 0, track);
        view.Show(rig.Document, rig.Document.Project.Tracks.IndexOf(track!), newVisit: true);
        window.UpdateLayout();
        return (rig, window, view, editing);
    }

    private static string ButtonText(Window window) => (string)window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PianoRollScaleButton").Content!;

    [AvaloniaFact]
    public void APickIsSharedInAllThreeDirections()
    {
        var (rig, window, view, _) = Setup();
        using (rig)
        try
        {
            var scale = rig.Status.Scale;
            var bar = scale.BarHost();
            var fret = scale.FretboardHost();
            var roll = view.ScaleHost;
            var other = bar.Current.Top[1];
            bar.Pick(other);   // status bar -> fretboard and piano roll
            Assert.Equal(other.Highlight, fret.Current.Pick);
            Assert.Equal(other.Highlight, roll.Current.Pick);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(view.ScaleButtonText, ButtonText(window));
            Assert.StartsWith(ScaleFamilies.NoteName(other.Root, false), ButtonText(window));   // the toolbar button follows the status bar pick
            Assert.Equal(other.Highlight, view.EffectiveScaleHighlight());
            var third = bar.Current.Top[2];
            roll.Pick(third);   // piano roll -> status bar and fretboard
            Assert.Equal(third.Highlight, bar.Current.Pick);
            Assert.Equal(third.Highlight, fret.Current.Pick);
            var first = bar.Current.Top[0];
            fret.Pick(first);   // fretboard -> status bar and piano roll
            Assert.Equal(first.Highlight, bar.Current.Pick);
            Assert.Equal(first.Highlight, roll.Current.Pick);
            roll.UseAuto();   // Back to Auto from the roll returns all three to detection
            Assert.Null(bar.Current.Pick);
            Assert.Null(fret.Current.Pick);
            Assert.True(bar.Current.IsAuto || bar.Current.Pick is null);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PianoRollFeaturesFollowTheSharedEffectiveScale()
    {
        var (rig, window, view, editing) = Setup();
        using (rig)
        try
        {
            editing.PianoRollScaleShown = true;
            editing.PianoRollScaleLock = true;
            rig.Status.Scale.BarHost().Pick(ScaleReadoutEngine.Parse("C Major")!);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 0, 2, 4, 5, 7, 9, 11 }, view.Presenter.Scale!.OrderBy(x => x));
            Assert.NotNull(view.Surface.ScaleClasses);
            Assert.NotNull(view.Surface.LockScale);
            Assert.True(view.Presenter.ScaleLock);
            rig.Status.Scale.BarHost().Pick(ScaleReadoutEngine.Parse("D Natural Minor")!);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(2, view.Presenter.Scale!);   // the roll's highlight, lock set and step filter scale all moved at once
            Assert.Contains(5, view.Surface.StepScale!);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnOldPianoRollPickMigratesOnceIntoTheSharedPickOnlyWhenThereIsNone()
    {
        var old = new EditingSettings { PianoRollScalePick = "F# Natural Minor" };
        var (rig, window, view, _) = Setup(old);
        using (rig)
        try
        {
            Assert.Equal("F# Natural Minor", rig.Status.Scale.BarHost().Current.Pick);
            Assert.True(old.PianoRollScalePickMigrated);
            Assert.Equal("F# Natural Minor", old.PianoRollScalePick);   // readable, no longer written
            rig.Status.Scale.BarHost().UseAuto();
            view.Show(rig.Document, 0, newVisit: true);
            Assert.Null(rig.Status.Scale.BarHost().Current.Pick);   // applied once: not re-applied
        }
        finally { window.Close(); }
        var kept = new EditingSettings { PianoRollScalePick = "F# Natural Minor" };
        using var rig2 = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var track = rig2.Document.Project.FirstNotationTrack;
        rig2.Status.Scale.Show(rig2.Document.Project, 0, track);
        rig2.Status.Scale.BarHost().Pick(ScaleReadoutEngine.Parse("C Major")!);
        var view2 = new PianoRollView(new PianoRollHost { Document = () => rig2.Document, Editing = () => kept }) { SharedReadout = () => rig2.Status.Scale };
        var window2 = new Window { Content = view2 };
        window2.Show();
        try
        {
            view2.Show(rig2.Document, rig2.Document.Project.Tracks.IndexOf(track!), newVisit: true);
            Assert.Equal("C Major", rig2.Status.Scale.BarHost().Current.Pick);   // an existing pick is not overridden
        }
        finally { window2.Close(); }
    }

    [AvaloniaFact]
    public void TheStatusBarFretboardTickAndTheFretboardOnOffAreOneSettingAndTheFootnoteShows()
    {
        using var settings = MainWindowSettings.Open(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-shared-" + System.Guid.NewGuid().ToString("N") + ".json"));
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var scale = rig.Status.Scale;
        scale.Show(rig.Document.Project, 0, rig.Document.Project.FirstNotationTrack);
        var panel = new ScalePanel(scale.BarHost());
        var window = new Window { Width = 1200, Height = 800, Content = new Border() };
        window.Show();
        panel.OpenAbove(window);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tick = panel.Frame.Rows.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Name == "ScaleShowOnFretboard");
            Assert.Equal("Show on the fretboard (scale highlight)", global::Avalonia.Automation.AutomationProperties.GetName(tick));
            Assert.Equal(scale.Settings.ShowOnFretboard, tick.IsChecked);
            tick.IsChecked = !tick.IsChecked;
            Assert.Equal(tick.IsChecked, scale.Settings.ShowOnFretboard);   // the same setting the fretboard look's On / Off reads
            var note = panel.Frame.Rows.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "ScaleOptionsFootnote");
            Assert.Equal("Your pick sticks to this section and track; Auto returns to detection.", note.Text);
            panel.Close();
            var fret = new ScalePanel(scale.FretboardHost());
            fret.OpenAbove(window);
            Dispatcher.UIThread.RunJobs();
            var on = fret.Frame.Rows.GetLogicalDescendants().OfType<Control>().Single(c => c.Name == "ScaleShowOn") as global::Avalonia.Controls.Primitives.ToggleButton;
            Assert.Equal(scale.Settings.ShowOnFretboard, on!.IsChecked);
            fret.Close();
        }
        finally { window.Close(); }
    }
}
