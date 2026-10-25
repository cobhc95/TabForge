using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the Colour tracks window: structure and WPF texts, the row names, the range tick, colouring the ticked
/// rows with one undo capture, the renders in both themes and the contrast of its texts.
/// Does not own: the window or the presenter; the palette and the tick rules are read from TrackColoursPresenter.
/// Tests: this class.
/// </summary>
public class TrackColoursWindowTests
{
    [AvaloniaFact]
    public void ControlsMatchWpfTitleTextsAndPalette()
    {
        var window = new TrackColoursWindow(DemoSongFactory.Create().Tracks);
        window.Show();

        Assert.Equal(("Colour tracks", 460d, 540d), (window.Title, window.Width, window.Height));
        Assert.Equal("Tick the tracks to colour (or choose a range), then click a colour. Repeat for other tracks.", Named<TextBlock>(window, "IntroText").Text);
        Assert.Equal(new[] { "Select range", "Close" }, Buttons(window));
        var tips = PaletteButtons(window).Select(b => ToolTip.GetTip(b)).ToList();
        Assert.Equal(TrackColoursPresenter.Palette.Select(p => $"{p.Name}: colour the ticked tracks"), tips);
        Assert.Equal(DemoSongFactory.Create().Tracks.Count, Named<ScrollViewer>(window, "TrackList").GetLogicalDescendants().OfType<CheckBox>().Count());
        Assert.True(Named<Button>(window, "CloseButton").IsCancel);
        window.Close();
    }

    [AvaloniaFact]
    public void TrackRowsShowNumberedNamesFromTheTracks()
    {
        var tracks = DemoSongFactory.Create().Tracks;
        var window = new TrackColoursWindow(tracks);
        window.Show();

        var rows = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => t is { Length: > 0 } && char.IsDigit(t[0])).ToList();
        Assert.Equal(tracks.Select((t, i) => $"{i + 1}. {t.Name}"), rows);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectRangeTicksTheRangeInEitherOrderAndUnticksTheRest()
    {
        var window = new TrackColoursWindow(DemoSongFactory.Create().Tracks);
        window.Show();
        Named<ComboBox>(window, "RangeFrom").SelectedIndex = 2;
        Named<ComboBox>(window, "RangeTo").SelectedIndex = 1;
        DialogTestKit.Click(Named<Button>(window, "SelectRange"));

        var ticked = Boxes(window).Select((box, i) => (Index: i, Ticked: box.IsChecked == true)).Where(r => r.Ticked).Select(r => r.Index).ToList();
        Assert.Equal(new[] { 1, 2 }, ticked);
        window.Close();
    }

    [AvaloniaFact]
    public void ColouringTickedRowsChangesThemAndCapturesUndoOnce()
    {
        var tracks = DemoSongFactory.Create().Tracks;
        var originals = tracks.Select(t => t.ColorHex).ToList();
        var captured = 0;
        var changed = 0;
        var window = new TrackColoursWindow(tracks, () => captured++, () => changed++);
        window.Show();

        Boxes(window)[0].IsChecked = true;
        DialogTestKit.Click(PaletteButtons(window).ElementAt(0));
        Assert.Equal(TrackColoursPresenter.Palette[0].Hex, tracks[0].ColorHex);
        Assert.Equal(originals.Skip(1), tracks.Skip(1).Select(t => t.ColorHex));
        Assert.Equal((1, 1), (captured, changed));

        Boxes(window)[0].IsChecked = false;
        Boxes(window)[1].IsChecked = true;
        DialogTestKit.Click(PaletteButtons(window).ElementAt(2));
        Assert.Equal(TrackColoursPresenter.Palette[2].Hex, tracks[1].ColorHex);
        Assert.Equal(TrackColoursPresenter.Palette[0].Hex, tracks[0].ColorHex);
        Assert.Equal((1, 2), (captured, changed));
        window.Close();
    }

    [AvaloniaFact]
    public void PaletteWithNoTickedRowChangesNothing()
    {
        var tracks = DemoSongFactory.Create().Tracks;
        var originals = tracks.Select(t => t.ColorHex).ToList();
        var captured = 0;
        var changed = 0;
        var window = new TrackColoursWindow(tracks, () => captured++, () => changed++);
        window.Show();
        DialogTestKit.Click(PaletteButtons(window).ElementAt(0));

        Assert.Equal(originals, tracks.Select(t => t.ColorHex));
        Assert.Equal((0, 0), (captured, changed));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColourTracksRendersPngInEachTheme(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new TrackColoursWindow(DemoSongFactory.Create().Tracks);
        var path = DialogTestKit.SavePng(window, nameof(TrackColoursWindowTests), $"track-colours-{(light ? "light" : "dark")}.png");
        window.Close();
        Assert.True(new FileInfo(path).Length > 0);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheWindow(bool light)
    {
        var surface = AvaloniaPalette.Get("WindowBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), surface) >= 4.5, $"{text} on WindowBackground (light: {light})");
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    // The buttons with a text label, in tree order (the palette buttons hold a swatch and are listed by PaletteButtons).
    private static IEnumerable<string> Buttons(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b.Content is string).Select(b => (string)b.Content!).ToList();

    private static IEnumerable<Button> PaletteButtons(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b.Content is Border).ToList();

    private static IReadOnlyList<CheckBox> Boxes(Window window) => window.GetLogicalDescendants().OfType<CheckBox>().ToList();
}
