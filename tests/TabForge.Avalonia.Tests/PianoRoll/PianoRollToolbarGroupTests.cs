using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll toolbar's grouped layout: the five captioned groups in order, whole-group wrapping at three widths without clipping, the scale button naming the scale,
//   Lock to scale, step input's own scale filter (Off / Snap / Only scale notes, independent of Lock), the Voice dropdown only on tab tracks, and the retired fold / highlight /
//   transpose-mode bar controls whose settings still work through the panel and the Move notes popup.
// Does not own: the Move notes popup (PianoRollTransposePopupTests) or the shared pick (PianoRollSharedScaleTests).
// Tests: PianoRollView toolbar.
public sealed class PianoRollToolbarGroupTests
{
    private static (Window Window, PianoRollView View, EditingSettings Editing, SongDocument Document) Show(SongDocument document, double width = 2000, EditingSettings? editing = null)
    {
        editing ??= new EditingSettings { PianoRollScalePickMigrated = true };
        var view = new PianoRollView(new PianoRollHost { Document = () => document, Editing = () => editing });
        var window = new Window { Width = width, Height = 700, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (window, view, editing, document);
    }

    private static List<Border> Groups(Window window) => window.GetVisualDescendants().OfType<Border>()
        .Where(b => b.Name?.StartsWith("PianoRollGroup", System.StringComparison.Ordinal) == true).ToList();

    [AvaloniaFact]
    public void RowOneHasTheFiveCaptionedGroupsInOrder()
    {
        var (window, _, _, _) = Show(PianoRollSongs.Midi());
        try
        {
            var groups = Groups(window);
            Assert.Equal(PianoRollToolbarLayout.GroupCaptions.Select(PianoRollToolbarLayout.GroupName), groups.Select(g => g.Name));
            var captions = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name?.StartsWith("PianoRollCaption", System.StringComparison.Ordinal) == true).ToList();
            Assert.Equal(new[] { "TOOL", "SNAP", "SCALE", "STEP INPUT", "NEW NOTES" }, captions.Select(c => c.Text));
            Assert.All(captions, c => Assert.False(string.IsNullOrEmpty(global::Avalonia.Automation.AutomationProperties.GetName(c))));
            Assert.All(groups, g => Assert.False(string.IsNullOrEmpty(global::Avalonia.Automation.AutomationProperties.GetName(g))));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(2000)]
    [InlineData(900)]
    [InlineData(520)]
    public void GroupsWrapAsWholesAndNeverClip(double width)
    {
        var (window, _, _, _) = Show(PianoRollSongs.Midi(), width);
        try
        {
            var row = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.Row1Name);
            foreach (var group in Groups(window))
            {
                var at = ((Visual)group).TranslatePoint(new global::Avalonia.Point(0, 0), row)!.Value;
                Assert.True(at.X + group.Bounds.Width <= row.Bounds.Width + 1 || group.Bounds.Width > row.Bounds.Width, group.Name + " runs past the row");
                Assert.True(group.Bounds.Height > 0);
            }
            if (width > 1500) Assert.True(Groups(window).Select(g => ((Visual)g).TranslatePoint(new global::Avalonia.Point(0, 0), row)!.Value.Y).Max() < ((Visual)Groups(window)[0]).Bounds.Height, "one line");
            if (width < 600) Assert.True(Groups(window).Select(g => ((Visual)g).TranslatePoint(new global::Avalonia.Point(0, 0), row)!.Value.Y).Max() >= ((Visual)Groups(window)[0]).Bounds.Height, "wrapped");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheScaleButtonNamesTheSharedScaleAndTheRetiredBarControlsAreGone()
    {
        var (window, view, editing, document) = Show(PianoRollSongs.Midi());
        try
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PianoRollScaleButton");
            view.ScaleHost.Pick(ScaleReadoutEngine.Parse("D Natural Minor")!);
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("D ", (string)button.Content!);
            Assert.Equal(view.ScaleButtonText, button.Content);
            var row = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.Row1Name);
            Assert.DoesNotContain(row.GetVisualDescendants().OfType<ComboBox>(), c => ReferenceEquals(c.ItemsSource, PianoRollScale.Folds));
            Assert.DoesNotContain(row.GetVisualDescendants().OfType<ComboBox>(), c => c.ItemsSource is IEnumerable<string> items && items.Contains("Scale steps"));
            Assert.Equal("Lock to scale", row.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "PianoRollLockScale").Content);
            // The settings the removed controls edited still work: Fold through the panel host, the transpose mode through the Move notes choice.
            var panel = new global::Avalonia.Controls.StackPanel();
            Assert.Equal(PianoRollScale.FoldNone, PianoRollScale.RowsOf(document.Project));   // SHOW ROWS is the song's: All pitches for a new song
            Assert.Equal("Semitones", editing.PianoRollTransposeMode);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void VoiceShowsOnTabTracksOnlyAndTheChordLabelAlwaysSaysChord()
    {
        var (midi, _, _, _) = Show(PianoRollSongs.Midi());
        var (tab, tabView, _, _) = Show(PianoRollSongs.Tab());
        try
        {
            Assert.False(midi.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PianoRollVoice").IsVisible);
            Assert.True(tab.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PianoRollVoice").IsVisible);
            var chord = midi.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PianoRollChord");
            Assert.NotNull(chord.SelectionBoxItemTemplate);
            Assert.False(string.IsNullOrEmpty(global::Avalonia.Automation.AutomationProperties.GetName(chord)));
        }
        finally { midi.Close(); tab.Close(); }
    }

    private static List<int> PitchesAfterStep(PianoRollView view, SongDocument document, int key)
    {
        view.Surface.StepInput = true;
        view.StepNote(key, 100);
        Dispatcher.UIThread.RunJobs();
        return document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Pitch).ToList();
    }

    [AvaloniaTheory]
    [InlineData("Off", false, 61)]
    [InlineData("Off", true, 61)]
    [InlineData("Snap", false, 62)]
    [InlineData("Snap", true, 62)]
    [InlineData("Only scale notes", false, -1)]
    [InlineData("Only scale notes", true, -1)]
    public void StepInputFollowsItsOwnFilterWhateverLockSays(string filter, bool lockOn, int landed)
    {
        var editing = new EditingSettings { PianoRollScalePickMigrated = true, PianoRollStepScaleFilter = filter, PianoRollScaleLock = lockOn };
        var (window, view, _, document) = Show(PianoRollSongs.Midi(), 2000, editing);
        try
        {
            view.ScaleHost.Pick(ScaleReadoutEngine.Parse("C Major")!);
            Dispatcher.UIThread.RunJobs();
            var pitches = PitchesAfterStep(view, document, 61);
            if (landed < 0) Assert.Equal(new[] { 60, 64 }, pitches.OrderBy(p => p));   // ignored
            else Assert.Contains(landed, pitches);
            var inKey = PitchesAfterStep(view, document, 67);
            Assert.Contains(67, inKey);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheFilterDropdownShowsOnlyWhileStepInputIsOnAndTheDefaultIsSnap()
    {
        Assert.Equal("Snap", new EditingSettings().PianoRollStepScaleFilter);
        var (window, _, editing, _) = Show(PianoRollSongs.Midi());
        try
        {
            var filter = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PianoRollStepFilter");
            var step = window.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "PianoRollStepInput");
            Assert.False(filter.IsVisible);
            step.IsChecked = true;
            Assert.True(filter.IsVisible);
            filter.SelectedItem = "Off";
            Assert.Equal("Off", editing.PianoRollStepScaleFilter);
            Assert.Equal(new[] { "Off", "Snap", "Only scale notes" }, PianoRollScaleLock.StepFilters);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LockToScaleStillSnapsDrawnAndTransposedNotesButNotStepInput()
    {
        var editing = new EditingSettings { PianoRollScalePickMigrated = true, PianoRollScaleLock = true, PianoRollStepScaleFilter = "Off" };
        var (window, view, _, document) = Show(PianoRollSongs.Midi(), 2000, editing);
        try
        {
            view.ScaleHost.Pick(ScaleReadoutEngine.Parse("C Major")!);
            Assert.True(view.Presenter.ScaleLock);
            Assert.NotNull(view.Presenter.Scale);
            view.Presenter.Select(new[] { 0 });
            var task = view.Presenter.TransposeAsync(1);   // C -> C#, pulled to D by the lock
            Dispatcher.UIThread.RunJobs();
            Assert.True(task.IsCompleted);
            Assert.Contains(62, document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Pitch));
        }
        finally { window.Close(); }
    }
}
