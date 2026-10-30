using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the scale list inside the Pick another card: it opens inline (no More scales button, no back page), filters, a row is a manual pick and the list stays,
//   the percentage matches the detected cards, the fit is secondary text, rows sort by confidence and Best matches starts with the Auto choice.
// Does not own: the card's press and drop-down behaviour (PickAnotherCardTests).
// Tests: this class.
public sealed class PickAnotherListTests
{
    private static T Find<T>(ScalePanel popup, string name) where T : Control =>
        popup.Frame.Rows.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static bool Has(ScalePanel popup, string name) => popup.Frame.Rows.GetLogicalDescendants().OfType<Control>().Any(c => c.Name == name);

    private static Window Open(ScalePanel popup)
    {
        var window = new Window { Width = 1200, Height = 700, Content = new Border() };
        window.Show();
        popup.OpenAbove(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static System.Collections.Generic.List<string> Rows(ScalePanel popup) =>
        ((System.Collections.IEnumerable)Find<ListBox>(popup, "ScaleSearchResults").ItemsSource!).Cast<object>().Select(o => o.ToString()!).ToList();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PickAnotherOpensTheListInlineAndARowIsAManualPickThatKeepsItOpen(bool fretboard)
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var scale = rig.Status.Scale;
        var popup = new ScalePanel(fretboard ? scale.FretboardHost(true) : scale.BarHost());
        var window = Open(popup);
        Assert.False(Has(popup, "ScaleMore"));
        Assert.False(Has(popup, "ScaleListBack"));
        Assert.False(Has(popup, "ScaleSearchBox"));
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        Assert.True(Has(popup, "ScaleSearchBox"));
        Assert.True(Has(popup, "ScaleSearchResults"));
        popup.SetQuery("d harm");
        Assert.Equal("D Harmonic Minor", popup.Hits[0].Highlight);
        popup.PickSelected();
        Assert.Equal("D Harmonic Minor", scale.Current.Pick);
        Assert.True(Has(popup, "ScaleSearchResults"));   // still open
        Assert.True(Find<RadioCard>(popup, "ScalePickAnotherCard").IsSelected);
        Assert.Equal(2, Find<ComboBox>(popup, "ScalePickKey").SelectedIndex);
        if (fretboard) Assert.StartsWith("Manual: ", Find<TextBlock>(popup, "ScaleBadge").Text!);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void TheListShowsTheCardsConfidenceFirstAndFitSecondAndBestMatchesStartsWithAuto()
    {
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections());
        var scale = rig.Status.Scale;
        var popup = new ScalePanel(scale.BarHost());
        var window = Open(popup);
        var auto = scale.Current.Top[0];
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        popup.SetQuery("");
        Assert.Equal(auto.Highlight, popup.BestMatches[0]);
        Assert.Equal(scale.Current.Top.Select(m => m.Highlight), popup.BestMatches.Take(3));
        var rows = Rows(popup);
        Assert.Equal("Best matches", rows[0]);
        Assert.Contains($"{auto.Confidence:P0}", rows[1]);   // the same number as the card
        Assert.Contains("all notes fit", rows[1]);
        // C major and A natural minor hold the same notes: same fit, but the tonic-weighted one has the higher confidence and sorts first.
        var major = rows.First(r => r.StartsWith("C Major"));
        var minor = rows.First(r => r.StartsWith("A Natural Minor"));
        Assert.Contains("all notes fit", major);
        Assert.Contains("all notes fit", minor);
        Assert.NotEqual(major.Split("  ·  ")[2], minor.Split("  ·  ")[2]);
        Assert.True(auto.Confidence > 0);
        var pcts = rows.Skip(rows.IndexOf("Major") + 1).TakeWhile(r => r.Contains("  ·  ")).Select(r => double.Parse(r.Split("  ·  ")[2].TrimEnd('%'))).ToList();
        Assert.Equal(pcts.OrderByDescending(p => p), pcts);
        popup.Close();
        window.Close();
    }
}
