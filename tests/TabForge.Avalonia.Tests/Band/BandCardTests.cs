using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Band view's cards: the fretboard default (on for the first shown card only, off for a card added later), the multi-select of the
/// shown cards, the track colour border of every card, the tab-only staff without its header block, and the single vertical scroll bar.
/// Tests: BandView, BandTrackPanel, BandFretboardIcon.
/// </summary>
public class BandCardTests
{
    private static SongDocument DemoDocument() => new(() => null) { Project = DemoSongFactory.Create() };

    [AvaloniaFact]
    public void FirstShownCardHasItsFretboardOnAndTheOthersOff()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        var view = window.View;
        var first = view.ShownTrackIndices[0];
        Assert.True(view.FretboardOn(first));
        Assert.True(view.PanelFor(first)!.FretboardShown);
        foreach (var index in view.ShownTrackIndices.Skip(1))
        {
            Assert.False(view.FretboardOn(index));
            Assert.False(view.PanelFor(index)!.FretboardShown);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void CardAddedLaterHasItsFretboardOff()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var hidden = Enumerable.Range(0, document.Project.Tracks.Count).First(i => !view.IsShown(i));
        view.ToggleShown(hidden);
        Assert.True(view.IsShown(hidden));
        Assert.False(view.PanelFor(hidden)!.FretboardShown);
        window.Close();
    }

    [AvaloniaFact]
    public void FretboardToggleShowsAndHidesTheCardsFretboard()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        var view = window.View;
        var index = view.ShownTrackIndices[1];
        view.SetFretboardOn(index, true);
        Assert.True(view.PanelFor(index)!.FretboardShown);
        Assert.True(view.PanelFor(index)!.Instrument.IsVisible);
        view.SetFretboardOn(index, false);
        Assert.False(view.PanelFor(index)!.FretboardShown);
        Assert.False(view.PanelFor(index)!.Instrument.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void MultiSelectAddsAndRemovesCardsInTrackOrder()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var changes = 0;
        view.ShownChanged += () => changes++;
        var hidden = Enumerable.Range(0, document.Project.Tracks.Count).Where(i => !view.IsShown(i)).ToList();
        view.ToggleShown(hidden[^1]);
        view.ToggleShown(hidden[0]);
        Assert.Equal(2, changes);
        Assert.Equal(view.ShownTrackIndices.OrderBy(i => i), view.ShownTrackIndices);
        Assert.Contains(hidden[0], view.ShownTrackIndices);
        Assert.Contains(hidden[^1], view.ShownTrackIndices);
        Assert.Equal(view.ShownTrackIndices.Count, view.VisiblePanelCount);
        view.ToggleShown(hidden[0]);
        Assert.DoesNotContain(hidden[0], view.ShownTrackIndices);
        Assert.Equal(view.ShownTrackIndices.Count, view.VisiblePanelCount);
        window.Close();
    }

    [AvaloniaFact]
    public void CardsWithDifferentContentShareEveryBarStartX()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var firstIndex = view.ShownTrackIndices[0];
        var first = view.PanelFor(firstIndex)!.Staff;
        // Cards of identical tracks lay out the same anyway, so the second card is the first track whose own bar widths differ.
        var secondIndex = Enumerable.Range(0, document.Project.Tracks.Count).Where(i => i != firstIndex).FirstOrDefault(index =>
        {
            if (!view.IsShown(index)) view.ToggleShown(index);
            return !view.PanelFor(index)!.Staff.NaturalBarWidths()!.SequenceEqual(first.NaturalBarWidths()!);
        }, -1);
        Assert.NotEqual(-1, secondIndex);
        var second = view.PanelFor(secondIndex)!.Staff;
        var barsOfFirst = first.Probe.Layout.GetLayout(first.Probe.Track!).Systems.SelectMany(s => s.Measures).ToList();
        var barsOfSecond = second.Probe.Layout.GetLayout(second.Probe.Track!).Systems.SelectMany(s => s.Measures).ToList();
        Assert.Equal(barsOfFirst.Count, barsOfSecond.Count);
        for (var bar = 0; bar < barsOfFirst.Count; bar++)
            Assert.Equal(barsOfFirst[bar].X, barsOfSecond[bar].X, 0.01);
        window.Close();
    }

    [AvaloniaFact]
    public void PlayheadLinesUpAtTheSameXInEveryCard()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var timeline = new ScoreTimeline();
        var xs = view.ShownTrackIndices.Select(index =>
        {
            var staff = view.PanelFor(index)!.Staff;
            staff.Probe.Play.Set(timeline, index, 5, 0, 0.5, 0);
            return staff.Probe.Play.PlayheadLine(staff.Probe)!.Value.X;
        }).ToList();
        Assert.True(xs.Count >= 2);
        Assert.All(xs, x => Assert.Equal(xs[0], x, 0.01));
        window.Close();
    }

    [AvaloniaFact]
    public void CardsOfEachSongComeBackAsTheyWereLeft()
    {
        var first = DemoDocument();
        var second = DemoDocument();
        var window = new BandWindow(first);
        window.Show();
        var view = window.View;
        var defaults = view.ShownTrackIndices.ToList();
        var hidden = Enumerable.Range(0, first.Project.Tracks.Count).First(i => !view.IsShown(i));
        view.ToggleShown(hidden);
        view.SetFretboardOn(defaults[0], false);
        view.ShowSong(second);
        Assert.Equal(defaults, view.ShownTrackIndices);
        Assert.True(view.FretboardOn(defaults[0]), "the other song starts with its own defaults");
        view.ShowSong(first);
        Assert.Contains(hidden, view.ShownTrackIndices);
        Assert.False(view.FretboardOn(defaults[0]), "the first song keeps its fretboard toggles");
        window.Close();
    }

    [AvaloniaFact]
    public void StripCapsulesLightEachShownCardAndAClickTogglesIt()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var strip = new CompactTrackStrip();
        strip.IsLit = view.IsShown;
        strip.PillClicked = view.ToggleShown;
        view.ShownChanged += strip.SyncLit;
        strip.SetOn(true, document);
        Assert.Equal(view.ShownTrackIndices, Enumerable.Range(0, strip.Pills.Count).Where(i => strip.Pills[i].IsChecked == true));
        var hidden = Enumerable.Range(0, document.Project.Tracks.Count).First(i => !view.IsShown(i));
        strip.Pills[hidden].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Assert.True(view.IsShown(hidden));
        Assert.True(strip.Pills[hidden].IsChecked);
        Assert.Equal(view.ShownTrackIndices, Enumerable.Range(0, strip.Pills.Count).Where(i => strip.Pills[i].IsChecked == true));
        window.Close();
    }

    [AvaloniaFact]
    public void EveryCardHasARoundedTrackColourBorder()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var unselected = view.ShownTrackIndices.First(i => i != view.SelectedTrackIndex);
        var panel = view.PanelFor(unselected)!;
        var track = document.Project.Tracks[unselected];
        Assert.True(Color.TryParse(track.ColorHex, out var colour));
        Assert.Equal(colour, ((ISolidColorBrush)panel.BorderBrush!).Color);
        Assert.Equal(1.5, panel.BorderThickness.Left);
        Assert.True(panel.CornerRadius.TopLeft > 0);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectedCardKeepsItsTrackColourFrameAndEveryCardHasTheBandBox()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var second = view.ShownTrackIndices[1];
        view.SelectTrack(second);
        var panel = view.PanelFor(second)!;
        Assert.True(panel.IsSelected);
        Assert.True(Color.TryParse(document.Project.Tracks[second].ColorHex, out var colour));
        Assert.Equal(colour, ((ISolidColorBrush)panel.BorderBrush!).Color);
        Assert.Equal(4, panel.CornerRadius.TopLeft);
        Assert.Equal(8, panel.Padding.Left);
        Assert.Equal(6, BandView.CardGap);
        window.Close();
    }

    [AvaloniaFact]
    public void CardStaffIsTabOnlyWithoutItsHeaderBlock()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        var panel = window.View.PanelFor(window.View.ShownTrackIndices[0])!;
        Assert.Equal(NotationMode.TabOnly, panel.Staff.Notation);
        Assert.False(panel.Staff.DrawsHeader);
        // The card's staff reflows to the card width at the zoom its cards fit the view at (never above 100%).
        Assert.InRange(panel.Staff.Zoom, 0.25, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void TheBandViewHasTheOnlyVerticalScrollBar()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        var view = window.View;
        Assert.Equal(ScrollBarVisibility.Auto, view.VerticalScrollBarVisibility);
        var viewers = view.GetLogicalDescendants().OfType<ScrollViewer>().Prepend(view).ToList();
        Assert.Equal(1, viewers.Count(v => v.VerticalScrollBarVisibility == ScrollBarVisibility.Auto));
        // A card's staff viewer scrolls its window of systems with no bar of its own.
        Assert.All(viewers.Where(v => v != view), v => Assert.Contains(v.VerticalScrollBarVisibility, new[] { ScrollBarVisibility.Disabled, ScrollBarVisibility.Hidden }));
        window.Close();
    }

    [AvaloniaFact]
    public void ResetViewPutsTheDefaultCardsAndFretboardBack()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var defaults = view.ShownTrackIndices.ToList();
        view.ToggleShown(Enumerable.Range(0, document.Project.Tracks.Count).First(i => !view.IsShown(i)));
        view.SetFretboardOn(defaults[0], false);
        view.ResetView();
        Assert.Equal(defaults, view.ShownTrackIndices);
        Assert.True(view.PanelFor(defaults[0])!.FretboardShown);
        window.Close();
    }
}
