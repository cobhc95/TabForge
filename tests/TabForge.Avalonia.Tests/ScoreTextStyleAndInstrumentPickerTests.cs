using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the score text style and Choose instrument windows (structure, texts, colour editing through the chooser,
/// search, renders in each theme, contrast) and of the colour chooser's typed result.
/// Does not own: the windows or the presenters; expected values come from ScoreTextStylePresenter and InstrumentPickerPresenter.
/// Tests: this class.
/// </summary>
public class ScoreTextStyleAndInstrumentPickerTests
{
    private static readonly string[] Fonts = { "Arial", "Consolas" };

    [AvaloniaFact]
    public void ScoreTextStyleShowsTheAreasRowsAndButtons()
    {
        var styles = new Dictionary<string, ScoreTextAreaStyle>();
        var window = new ScoreTextStyleWindow(styles, Fonts);
        window.Show();
        Assert.Equal("Score text & fonts", window.Title);
        var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
        Assert.Equal(ScoreTextStylePresenter.Areas.Select(a => a.Label), list.Items.Cast<string>());
        var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.All(new[] { "Font", "Size", "Style", "Colour", "Outline", "100%", "off" }, t => Assert.Contains(t, texts));
        Assert.Equal(new[] { "Inherit", "Reset this area", "OK", "Cancel" }, ButtonTexts(window));
        var font = window.GetLogicalDescendants().OfType<ComboBox>().Single();
        Assert.Equal(new[] { "(inherit)", "Arial", "Consolas" }, font.Items.Cast<string>());
        Assert.True(styles.ContainsKey("Fret"));
        window.Close();
    }

    [AvaloniaFact]
    public void ScoreTextStyleEditsSizeAndColoursThroughTheChooser()
    {
        var styles = new Dictionary<string, ScoreTextAreaStyle>();
        var previews = 0;
        var window = new ScoreTextStyleWindow(styles, Fonts, () => previews++);
        window.PickAsync = (_, _) => Task.FromResult<Rgba?>(new Rgba(0x14, 0xB8, 0xA6));
        window.Show();
        window.GetLogicalDescendants().OfType<Slider>().First().Value = 120;
        Assert.Equal(120, styles["Fret"].SizePercent);
        var swatches = window.GetLogicalDescendants().OfType<TabForge.Avalonia.Platform.Controls.ColourButton>().ToList();
        Click(swatches[1]);
        Assert.Equal("#14B8A6", styles["Fret"].OutlineColour);
        Assert.Equal(1, styles["Fret"].OutlineThickness);
        Click(swatches[0]);
        Assert.Equal("#14B8A6", styles["Fret"].Colour);
        Click(ButtonNamed(window, "Inherit"));
        Assert.Null(styles["Fret"].Colour);
        Assert.True(previews >= 3);
        window.Close();
    }

    [AvaloniaFact]
    public void InstrumentPickerShowsFamiliesTilesAndFilters()
    {
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.SampleEntries, "Distortion Guitar");
        window.Show();
        RunQueuedUiWork();
        Assert.Equal(InstrumentPickerPresenter.Title, window.Title);
        Assert.Equal(InstrumentPickerPresenter.SearchHint, window.GetLogicalDescendants().OfType<TextBox>().Single().Watermark);
        Assert.Equal(new[] { InstrumentPickerPresenter.SelectButton, InstrumentPickerPresenter.CancelButton }, ButtonTexts(window));
        var headers = window.GetLogicalDescendants().OfType<InstrumentFamilyHeader>().ToList();
        Assert.Equal(new[] { "Piano", "Guitar", "Bass", "Drum kits" }, headers.Select(h => h.Family));
        var tiles = window.GetLogicalDescendants().OfType<Border>().Where(b => ToolTip.GetTip(b) is string).ToList();
        Assert.Equal(InstrumentPickerWindow.SampleEntries.Count, tiles.Count);

        window.GetLogicalDescendants().OfType<TextBox>().Single().Text = "bass";
        Assert.Equal(2, tiles.Count(t => t.IsVisible));
        Assert.Equal(new[] { "Bass" }, headers.Where(h => h.IsVisible).Select(h => h.Family));
        window.Close();
    }

    [AvaloniaFact]
    public void InstrumentPickerListsEveryCatalogueEntry()
    {
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), null);
        window.Show();
        RunQueuedUiWork();
        var names =window.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.FontSize == 11).Select(t => t.Text).ToList();
        Assert.Equal(InstrumentCatalog.All.Count, names.Count);
        Assert.Equal(InstrumentCatalog.All.Select(e => e.Name).Order(), names.Order());
        window.Close();
    }

    [AvaloniaFact]
    public async Task InstrumentPickerFocusFamilyOpensOnlyTheCurrentFamilyAndSelectReturnsTheName()
    {
        var owner = new Window();
        owner.Show();
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.SampleEntries, "Fretless Bass", focusFamily: true);
        var pending = window.ShowDialog<string?>(owner);
        var headers = window.GetLogicalDescendants().OfType<InstrumentFamilyHeader>().Select(h => (h.Family, h.IsOpen));
        Assert.Equal(new[] { ("Piano", false), ("Guitar", false), ("Bass", true), ("Drum kits", false) }, headers);
        Click(ButtonNamed(window, InstrumentPickerPresenter.SelectButton));
        Assert.Equal("Fretless Bass", await pending);
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScoreTextStyleAndInstrumentPickerRenderPngsInEachTheme(bool light)
    {
        var windows = new (Window Window, string Name)[]
        {
            (new ScoreTextStyleWindow(new Dictionary<string, ScoreTextAreaStyle>(), Fonts), "score-text-style"),
            (new InstrumentPickerWindow(InstrumentPickerWindow.SampleEntries, "Distortion Guitar"), "instrument-picker"),
        };
        using var theme = UseTheme(light);
        foreach (var (window, name) in windows)
        {
            Assert.True(new FileInfo(SavePng(window, nameof(ScoreTextStyleAndInstrumentPickerTests), $"{name}-{(light ? "light" : "dark")}.png")).Length > 0);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOne(bool light)
    {
        using var theme = UseTheme(light);
        var window = new InstrumentPickerWindow(InstrumentPickerWindow.SampleEntries, "Distortion Guitar");
        window.Show();
        RunQueuedUiWork();
        SavePng(window, nameof(ScoreTextStyleAndInstrumentPickerTests), $"instrument-picker-contrast-{(light ? "light" : "dark")}.png");
        foreach (var tile in window.GetLogicalDescendants().OfType<Border>().Where(b => ToolTip.GetTip(b) is string))
            Assert.True(Contrast(tile.GetLogicalDescendants().OfType<TextBlock>().Single().Foreground, window.Background, "instrument tile") >= 4.5, $"tile (light: {light})");
        Assert.True(Contrast(window.Foreground, window.Background, "window") >= 4.5);
        var header = window.GetLogicalDescendants().OfType<TextBlock>().First(t => t.FontSize == 13);
        Assert.True(Contrast(header.Foreground, window.Background, "family header") >= 4.5);
        window.Close();
        var style = new ScoreTextStyleWindow(new Dictionary<string, ScoreTextAreaStyle>(), Fonts);
        SavePng(style, nameof(ScoreTextStyleAndInstrumentPickerTests), $"score-text-style-contrast-{(light ? "light" : "dark")}.png");
        Assert.True(Contrast(style.Foreground, style.Background, "score text style") >= 4.5);
        style.Close();
    }

    private static List<string> ButtonTexts(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b is not global::Avalonia.Controls.Primitives.ToggleButton && b.Content is string s && s != "—").Select(b => (string)b.Content!).ToList();

    private static Button ButtonNamed(Window window, string text) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);
}
