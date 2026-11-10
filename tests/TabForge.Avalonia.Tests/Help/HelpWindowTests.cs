using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Features.Help;
using TabForge.Features.Help;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Help window: it shows Getting started first with all fourteen chapters in four groups, opens another chapter from the list, builds a long chapter
//   section by section until complete, scrolls to a section, shows search results for a typed query and the no-results page with suggestions, shows the preparing
//   notice before the index is ready, closes on Esc, is one window however often F1 runs, remembers its size, and draws in both themes.
// Does not own: the engine (HelpSearchTests), the content rules (HelpContentTests).
// Tests: this class.
public sealed class HelpWindowTests
{
    private static HelpFeature Feature = null!;
    private static HelpFeature New() => Feature = new HelpFeature(Owner(), null, null);

    private static Window Owner()
    {
        var owner = new Window();
        owner.Show();
        return owner;
    }

    private static string Text(TextBlock block) => block.Text ?? string.Concat(block.Inlines?.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? Enumerable.Empty<string?>());

    private static void Settle() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void OpensOnGettingStartedWithEveryChapterListed()
    {
        var watch = Stopwatch.StartNew();
        var window = New().Open();
        watch.Stop();
        try
        {
            Assert.Equal(1, window.ChapterIndex);
            var rows = window.GetVisualDescendants().OfType<HelpNavRow>().ToList();
            Assert.Equal(HelpChapterCatalog.All.Count, rows.Count);
            Assert.True(rows[1].Selected);
            Assert.Equal(1, rows.Count(r => r.Selected));
            Assert.Equal(HelpChapterCatalog.Groups.Count(), window.GetVisualDescendants().OfType<TextBlock>().Count(t => Text(t) is "START HERE" or "WRITING MUSIC" or "PLAYING AND TOOLS" or "HELP"));
            Assert.NotNull(window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "TroubleshootingLink"));
            System.Console.WriteLine("help window open ms: " + watch.Elapsed.TotalMilliseconds.ToString("F1"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ALongChapterIsBuiltSectionBySectionUntilComplete()
    {
        var window = New().Open();
        try
        {
            window.Open(13, null);
            var total = Feature.Content.Chapter(13).Sections.Count;
            Assert.True(window.Reader.RealisedSections < total);
            Assert.False(window.Reader.Complete);
            for (var i = 0; i < total + 5 && !window.Reader.Complete; i++) Settle();
            Assert.True(window.Reader.Complete);
            Assert.Equal(total, window.Reader.RealisedSections);
            Assert.Equal(13, window.ChapterIndex);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OpeningAtASectionBuildsUpToItAndScrollsThere()
    {
        var window = New().Open();
        try
        {
            window.Open(13, 6);
            Assert.True(window.Reader.RealisedSections >= 7);
            Assert.True(window.Reader.Offset.Y > 0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SearchShowsRankedResultsAndANoResultsPageOffersChapters()
    {
        var feature = New();
        feature.Content.Index().Wait();
        var window = feature.Open();
        try
        {
            window.SearchBox.Text = "no sound";
            window.RunSearch();
            Assert.True(window.ShowingResults);
            Assert.True(window.Results.RowCount > 0);
            window.SearchBox.Text = "qzxvqzxv";
            window.RunSearch();
            Assert.Equal(0, window.Results.RowCount);
            Assert.StartsWith("No results", window.Results.Notice);
            Assert.True(window.Results.GetLogicalDescendants().OfType<TextBlock>().Count(t => HelpChapterCatalog.Titles.Contains(Text(t))) >= HelpChapterCatalog.Suggested.Count);
            window.SearchBox.Text = "";
            window.RunSearch();
            Assert.False(window.ShowingResults);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EscCloses()
    {
        var window = New().Open();
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(window.IsVisible);
        Assert.Null(Feature.Current);
    }

    [AvaloniaFact]
    public void OpeningAgainFocusesTheSameWindow()
    {
        var owner = Owner();
        var feature = new HelpFeature(owner, null, null);
        var first = feature.Open();
        try { Assert.Same(first, feature.Open()); }
        finally { first.Close(); }
        Assert.NotSame(first, feature.Open());
        feature.Current?.Close();
    }

    [AvaloniaFact]
    public void ClosingRemembersTheSize()
    {
        var settings = new AppSettings();
        var saved = 0;
        var window = new HelpFeature(Owner(), settings, () => saved++).Open();
        window.Width = ChromeMetricsHelp.Wider;
        window.Height = ChromeMetricsHelp.Taller;
        Settle();
        window.Close();
        Assert.Equal(1, saved);
        Assert.True(settings.HelpWindowWidth > 0);
    }

    [AvaloniaTheory]
    [InlineData("Dark"), InlineData("Light")]
    public void DrawsInBothThemes(string theme)
    {
        var owner = Owner();
        Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var window = new HelpFeature(owner, null, null).Open();
        try
        {
            window.Open(5, null);
            for (var i = 0; i < 40 && !window.Reader.Complete; i++) Settle();
            Assert.True(window.Reader.Complete);
            window.GetVisualDescendants().OfType<TextBlock>().ToList().ForEach(t => Assert.NotNull(t.Foreground));
        }
        finally { window.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Default; }
    }

    [AvaloniaFact]
    public void TheIndexBuildsQuicklyAndOnlyOnce()
    {
        var content = new HelpContent();
        var first = content.Index();
        first.Wait();
        Assert.Same(first, content.Index());
        Assert.True(content.IndexReady);
        System.Console.WriteLine("help index build ms: " + content.LastIndexBuildMs.ToString("F1"));
        Assert.True(content.LastIndexBuildMs < 2000);
    }
}

internal static class ChromeMetricsHelp
{
    internal const double Wider = 1000, Taller = 700;
}
