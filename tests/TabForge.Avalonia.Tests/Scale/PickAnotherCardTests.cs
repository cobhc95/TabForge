using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Services;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the scale popup's Pick another card: pressing it applies the pick of its key and scale drop-downs (even when that equals a detected scale), changing a
//   drop-down selects it and applies at once, the other cards clear, Back to Auto returns, from the status bar and the fretboard popup.
// Does not own: the popup body (StatusBarScaleTests, FretboardScalePopupTests).
// Tests: this class.
public sealed class PickAnotherCardTests
{
    private static T Find<T>(ScalePanel popup, string name) where T : Control =>
        popup.Frame.Rows.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static Window Open(ScalePanel popup)
    {
        var window = new Window { Width = 1200, Height = 700, Content = new Border() };
        window.Show();
        popup.OpenAbove(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PressingTheCardAppliesItsPickAndChangingADropDownAppliesAtOnce(bool fretboard)
    {
        using var settings = MainWindowSettings.Open(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-pickan-" + System.Guid.NewGuid().ToString("N") + ".json"));
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var scale = rig.Status.Scale;
        var popup = new ScalePanel(fretboard ? scale.FretboardHost(true) : scale.BarHost());
        var window = Open(popup);
        Assert.True(scale.Current.IsAuto);
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        Assert.False(scale.Current.IsAuto);   // the pick shown in the drop-downs is applied
        Assert.True(Find<RadioCard>(popup, "ScalePickAnotherCard").IsSelected);
        Assert.False(Find<RadioCard>(popup, "ScaleCandidate0Card").IsSelected);
        if (fretboard) Assert.StartsWith("Manual: ", Find<TextBlock>(popup, "ScaleBadge").Text!);
        Find<ComboBox>(popup, "ScalePickKey").SelectedIndex = 7;
        Assert.Equal(7, scale.Current.Effective.Root);
        Find<ComboBox>(popup, "ScalePickScale").SelectedIndex = MusicTheoryService.Scales.Keys.ToList().IndexOf("Dorian");
        Assert.Equal("Dorian", scale.Current.Effective.Scale);
        Assert.True(Find<RadioCard>(popup, "ScalePickAnotherCard").IsSelected);
        Find<RadioCard>(popup, "ScaleCandidate1Card").Select();
        Assert.False(Find<RadioCard>(popup, "ScalePickAnotherCard").IsSelected);
        Find<RadioCard>(popup, "ScalePickAnotherCard").Select();
        Find<ComboBox>(popup, fretboard ? "ScalePickKey" : "ScalePickKey").SelectedIndex = 2;   // changed while not selected before: selects and applies
        Assert.True(Find<RadioCard>(popup, "ScalePickAnotherCard").IsSelected);
        Assert.Equal(2, scale.Current.Effective.Root);
        Find<Button>(popup, "ScaleBackToAuto").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(scale.Current.IsAuto);
        Assert.False(Find<RadioCard>(popup, "ScalePickAnotherCard").IsSelected);
        popup.Close();
        window.Close();
    }
}
