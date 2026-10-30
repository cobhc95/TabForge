using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll scale popup's pick behaviour: changing the key or the scale drop-down selects Pick another and applies the pick at once, the
//   candidates and Auto still apply, a pick that is no candidate opens with Pick another selected and its key and scale shown, and the popup has no
//   "Use in piano roll" button.
// Does not own: the detection maths (PianoRollScaleDetectTests) or the drum-track guard (DrumLiveAndNoScaleTests).
// Tests: PianoRollScalePopup.
public sealed class PianoRollScalePopupTests
{
    private const string Leading = "ScaleCandidate0Card";
    private const string FirstCandidate = "ScaleCandidate0Card";
    private const string AnotherCard = "ScalePickAnotherCard";
    private const string KeyBox = "ScalePickKey";
    private const string ScaleBox = "ScalePickScale";

    // Shows track 0 (melodic) of the demo song with the given saved pick; the popup is opened by each test.
    private static (Window Window, PianoRollView View, EditingSettings Editing) Show(string? pick)
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var editing = new EditingSettings { PianoRollScalePickMigrated = true };
        var view = new PianoRollView(new PianoRollHost { Document = () => document, Editing = () => editing });
        var window = new Window { Width = 2000, Height = 700, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        if (pick is not null && pick != PianoRollScale.Auto && ScaleReadoutEngine.Parse(pick) is { } match) view.ScaleHost.Pick(match);   // a pick of the shared store
        return (window, view, editing);
    }

    // The shared pick (the status bar's and the fretboard's store) as the roll's panel host reads it: null while Auto.
    private static string? Pick(PianoRollView view) => view.ScaleHost.Current.Pick;

    private static TransportPopupFrame Open(PianoRollView view)
    {
        var frame = view.OpenScalePopup()!;
        Dispatcher.UIThread.RunJobs();
        return frame;
    }

    private static T Find<T>(TransportPopupFrame frame, string name) where T : Control =>
        frame.Panel.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    // The card that is selected while the pick is Auto: the first candidate, or the Auto card when no notes are detected.
    private static RadioCard Leader(TransportPopupFrame frame) =>
        frame.Panel.GetVisualDescendants().OfType<RadioCard>().First(c => c.Name is Leading or FirstCandidate);

    [AvaloniaFact]
    public void ChangingTheKeyDropDownSelectsPickAnotherAndAppliesIt()
    {
        var (window, view, _) = Show(PianoRollScale.Auto);
        try
        {
            var frame = Open(view);
            var another = Find<RadioCard>(frame, AnotherCard);
            Assert.False(another.IsSelected);
            Assert.True(Leader(frame).IsSelected);

            var key = Find<ComboBox>(frame, KeyBox);
            var scale = Find<ComboBox>(frame, ScaleBox);
            var root = (key.SelectedIndex + 2) % 12;
            key.SelectedIndex = root;
            Dispatcher.UIThread.RunJobs();

            Assert.True(another.IsSelected);
            Assert.False(Leader(frame).IsSelected);
            Assert.Equal($"{ScaleFamilies.NoteName(root, false)} {scale.SelectedItem}", Pick(view));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChangingTheScaleDropDownSelectsPickAnotherAndAppliesIt()
    {
        var (window, view, _) = Show(PianoRollScale.Auto);
        try
        {
            var frame = Open(view);
            var another = Find<RadioCard>(frame, AnotherCard);
            var key = Find<ComboBox>(frame, KeyBox);
            var scale = Find<ComboBox>(frame, ScaleBox);
            scale.SelectedIndex = (scale.SelectedIndex + 1) % scale.ItemCount;
            Dispatcher.UIThread.RunJobs();

            Assert.True(another.IsSelected);
            Assert.False(Leader(frame).IsSelected);
            Assert.Equal($"{ScaleFamilies.NoteName(key.SelectedIndex, false)} {scale.SelectedItem}", Pick(view));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CandidatesAndAutoStillApplyFromTheirCards()
    {
        var (window, view, _) = Show(PianoRollScale.Auto);
        try
        {
            var detected = view.ScaleHost.Current.Top;
            Assert.True(detected.Count > 1);
            var frame = Open(view);
            var key = Find<ComboBox>(frame, KeyBox);

            key.SelectedIndex = (key.SelectedIndex + 1) % 12;
            Dispatcher.UIThread.RunJobs();
            Find<RadioCard>(frame, "ScaleCandidate1Card").Select();
            Assert.Equal(detected[1].Highlight, Pick(view));
            Assert.False(Find<RadioCard>(frame, AnotherCard).IsSelected);

            Find<RadioCard>(frame, FirstCandidate).Select();
            Assert.Equal(detected[0].Highlight, Pick(view));   // a card is a manual pick
            Find<Button>(frame, "ScaleBackToAuto").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Null(Pick(view));   // back to Auto: the shared pick is forgotten
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void APickThatIsNoCandidateOpensWithPickAnotherShowingIt()
    {
        var (window, view, _) = Show(null);
        try
        {
            var detected = view.ScaleHost.Current.Top;
            var (root, scaleName, highlight) = Enumerable.Range(0, 12)
                .SelectMany(pc => MusicTheoryService.Scales.Keys.Select(name => (Root: pc, Scale: name, Highlight: $"{ScaleFamilies.NoteName(pc, false)} {name}")))
                .First(p => detected.All(m => m.Highlight != p.Highlight));
            view.ScaleHost.Pick(ScaleReadoutEngine.Parse(highlight)!);

            var frame = Open(view);

            Assert.True(Find<RadioCard>(frame, AnotherCard).IsSelected);
            Assert.Equal(root, Find<ComboBox>(frame, KeyBox).SelectedIndex);
            Assert.Equal(scaleName, Find<ComboBox>(frame, ScaleBox).SelectedItem);
            Assert.Equal(highlight, Pick(view));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePopupHasNoUseButtonAndSaysWhenAManualPickStays()
    {
        var (window, view, _) = Show(PianoRollScale.Auto);
        try
        {
            var frame = Open(view);

            Assert.DoesNotContain(frame.Panel.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Use in piano roll");
            Assert.False(Find<Button>(frame, "ScaleBackToAuto").IsVisible);   // Auto: no way back is offered
        }
        finally { window.Close(); }
    }
}
