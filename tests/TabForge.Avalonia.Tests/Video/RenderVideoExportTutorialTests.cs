using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the Render, Video export and Tutorial windows (group structure, texts and option lists from the presenters,
/// the disabled and reported buttons, the tutorial's Back, Forward, Previous and Next and its search, PNGs and contrast in both themes).
/// Does not own: the windows or the presenters; expected values come from RenderDialogPresenter, VideoExportPresenter and TutorialPresenter.
/// Tests: this class.
/// </summary>
public class RenderVideoExportTutorialTests
{
    private static readonly string[] RenderGroups = { "Source", "Bounds", "Output", "Options and format" };
    private static readonly string[] VideoGroups = { "Range", "View", "Tracks (the first is the score and instrument; all are shown in the Band view)", "Video" };

    [AvaloniaFact]
    public void RenderWindowShowsWpfGroupsControlsAndPresenterLists()
    {
        var window = MakeRender(new RenderSettings { Source = 0 });
        window.Show();
        Assert.Equal(RenderGroups, GroupNames(window));
        Assert.Equal(RenderDialogPresenter.BoundsNames, window.GetLogicalDescendants().OfType<RadioButton>().Select(r => (string)r.Content!));
        Assert.Equal(RenderDialogPresenter.SourceNames, ItemTexts(window, RenderDialogPresenter.SourceNames[0]));
        Assert.Equal(RenderDialogPresenter.FormatNames, ItemTexts(window, RenderDialogPresenter.FormatNames[0]));
        Assert.Equal(RenderDialogPresenter.ThreadNames, ItemTexts(window, RenderDialogPresenter.ThreadNames[0]));
        Assert.Equal(new[] { "Video (MP4)…", "Render", "Cancel", "All", "None", "Browse…" }, ButtonTexts(window));
        window.Close();
    }

    [AvaloniaFact]
    public void RenderWindowListsTheMasterFileOnlyForTheMasterMix()
    {
        var window = MakeRender(new RenderSettings { Source = 0, Pattern = "$project" });
        window.Show();
        var preview = window.GetLogicalDescendants().OfType<ListBox>().Single();
        Assert.Single(preview.Items);
        Assert.EndsWith(".wav", (string)preview.Items[0]!);
        window.Close();
    }

    [AvaloniaFact]
    public void RenderRenderButtonReportsThatItNeedsTheFullApp()
    {
        var window = MakeRender(new RenderSettings { Source = 0 });
        window.Show();
        Click(ButtonNamed(window, "Render"));
        Assert.Equal(ToolWindowParts.NeedsFullApp("Rendering"), StatusText(window));
        window.Close();
    }

    [AvaloniaFact]
    public void VideoExportShowsPresenterChoicesAndGroups()
    {
        var window = MakeVideo(new VideoSettings());
        window.Show();
        Assert.Equal(VideoGroups, GroupNames(window));
        Assert.Equal(VideoExportPresenter.RangeChoices, ItemTexts(window, VideoExportPresenter.RangeChoices[0]));
        Assert.Equal(VideoExportPresenter.LayoutChoices, ItemTexts(window, VideoExportPresenter.LayoutChoices[0]));
        Assert.Equal(VideoExportPresenter.ResolutionChoices, ItemTexts(window, VideoExportPresenter.ResolutionChoices[0]));
        Assert.Equal(new[] { VideoExportPresenter.ExportButton, VideoExportPresenter.CloseButton }, ButtonTexts(window).Where(t => t is "Export" or "Close"));
        window.Close();
    }

    [AvaloniaFact]
    public void VideoExportWithoutASelectionReportsThePresenterProblem()
    {
        var window = MakeVideo(new VideoSettings());
        window.Show();
        ComboBoxWith(window, VideoExportPresenter.RangeChoices[0]).SelectedIndex = VideoExportPresenter.RangeSelection;
        Click(ButtonNamed(window, VideoExportPresenter.ExportButton));
        Assert.Equal(VideoExportPresenter.SelectSomeBarsProblem, StatusText(window));
        window.Close();
    }

    [AvaloniaFact]
    public void VideoExportReportsThatItNeedsTheFullAppForAWholeSong()
    {
        var window = MakeVideo(new VideoSettings { Range = VideoExportPresenter.RangeWholeSong });
        window.Show();
        Click(ButtonNamed(window, VideoExportPresenter.ExportButton));
        Assert.Equal(ToolWindowParts.NeedsFullApp("Video export"), StatusText(window));
        window.Close();
    }

    [AvaloniaFact]
    public void TutorialWalksBackForwardPreviousAndNext()
    {
        var window = MakeTutorial(ThreeChapters());
        window.Show();
        Assert.Equal(new[] { "Start page" }, ReaderHeadings(window));
        Assert.False(ButtonNamed(window, "Back").IsEnabled);
        Assert.True(ButtonNamed(window, "Next").IsEnabled);

        Click(ButtonNamed(window, "Next"));
        Assert.Equal(new[] { "Notes page" }, ReaderHeadings(window));
        Click(ButtonNamed(window, "Next"));
        Assert.Equal(new[] { "End page" }, ReaderHeadings(window));
        Assert.False(ButtonNamed(window, "Next").IsEnabled);

        Click(ButtonNamed(window, "Back"));
        Assert.Equal(new[] { "Notes page" }, ReaderHeadings(window));
        Assert.True(ButtonNamed(window, "Forward").IsEnabled);
        Click(ButtonNamed(window, "Forward"));
        Assert.Equal(new[] { "End page" }, ReaderHeadings(window));

        Click(ButtonNamed(window, "Previous"));
        Assert.Equal(new[] { "Notes page" }, ReaderHeadings(window));
        window.Close();
    }

    [AvaloniaFact]
    public void TutorialSearchShowsHitsAndThePresenterNoHitsText()
    {
        var window = MakeTutorial(ThreeChapters());
        window.Show();
        var search = window.GetLogicalDescendants().OfType<TextBox>().Single();
        search.Text = "Basics";
        Assert.NotEmpty(window.GetLogicalDescendants().OfType<ListBox>().Single(l => l.Name == "Results").ItemsSource!);
        Assert.False(window.GetLogicalDescendants().OfType<ListBox>().Single(l => l.Name == "Contents").IsVisible);

        search.Text = "zzqx";
        var noResults = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text is not null && t.Text.StartsWith("Nothing matches"));
        Assert.True(noResults.IsVisible);
        Assert.Equal(TutorialPresenter.SearchPanels("zzqx", 0).NoResultsText, noResults.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void TutorialWithoutChaptersShowsTheEmptyMessage()
    {
        var window = MakeTutorial(System.Array.Empty<TutorialChapter>());
        window.Show();
        Assert.True(window.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "EmptyMessage").IsVisible);
        Assert.Equal(TutorialPresenter.GuideTitle(TutorialGuide.Basic), window.Title);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolWindowsRenderPngsInEachTheme(bool light)
    {
        var windows = new (Window Window, string Name)[]
        {
            (MakeRender(new RenderSettings()), "render"),
            (MakeVideo(new VideoSettings()), "video-export"),
            (MakeTutorial(ThreeChapters()), "tutorial"),
        };
        using var theme = UseTheme(light);
        foreach (var (window, name) in windows)
        {
            Assert.True(new FileInfo(SavePng(window, nameof(RenderVideoExportTutorialTests), $"{name}-{(light ? "light" : "dark")}.png")).Length > 0);
            window.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolWindowTextReadsAtLeastFourAndAHalfToOne(bool light)
    {
        foreach (var surface in new[] { "WindowBackground", "PanelBackground" })
            foreach (var text in new[] { "TextPrimary", "TextMuted" })
                Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), AvaloniaPalette.Get(surface, light)) >= 4.5,
                    $"{text} on {surface} (light: {light})");
    }

    private static RenderWindow MakeRender(RenderSettings settings) =>
        new(DemoSongFactory.Create(), settings, hasSelection: false, openVideoExport: null);

    private static VideoExportWindow MakeVideo(VideoSettings settings) => new(DemoSongFactory.Create(), settings, hasSelection: false);

    private static TutorialWindow MakeTutorial(IReadOnlyList<TutorialChapter> chapters) =>
        new(TutorialLibrary.FromChapters(chapters), TutorialLibrary.Empty(), TutorialGuide.Basic);

    private static List<TutorialChapter> ThreeChapters() => new()
    {
        TutorialLibrary.Parse("01-start.md", "# Start page\n\nFirst text.\n\n## Basics\n\nBasics text.\n"),
        TutorialLibrary.Parse("02-notes.md", "# Notes page\n\nSecond text.\n"),
        TutorialLibrary.Parse("03-end.md", "# End page\n\nThird text.\n"),
    };

    private static List<string> GroupNames(Window window) =>
        window.GetLogicalDescendants().OfType<Border>().Select(b => b.Name).Where(n => n is not null).Cast<string>().ToList();

    /// <summary>The texts of a combo box's items, found by its first item's text.</summary>
    private static List<string> ItemTexts(Window window, string firstItem) => ComboBoxWith(window, firstItem).Items
        .Select(item => item is ComboBoxItem entry ? (string)entry.Content! : (string)item!).ToList();

    private static ComboBox ComboBoxWith(Window window, string firstItem) => window.GetLogicalDescendants().OfType<ComboBox>()
        .Single(c => c.Items.Count > 0 && (c.Items[0] as string ?? (c.Items[0] as ComboBoxItem)?.Content as string) == firstItem);

    private static string StatusText(Window window) => window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "StatusText").Text!;

    private static List<string> ReaderHeadings(Window window) =>
        window.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.FontSize == 20).Select(t => t.Text!).ToList();

    private static List<string> ButtonTexts(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b is not ToggleButton && b.Content is string).Select(b => (string)b.Content!).ToList();

    private static Button ButtonNamed(Window window, string text) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text || b.Name == text);
}
