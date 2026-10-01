using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Help > Tutorial: the Markdown parser, the search index, the window's navigation, the PDF export, the command and the settings.</summary>
public static partial class SelfTest
{
    private const string TutorialChapterOne = """
        ---
        title: Welcome to the Test
        id: welcome
        order: 0
        keywords: hello, intro
        summary: A short synthetic chapter for the tests.
        ---

        # Welcome to the Test

        A paragraph with **bold**, *italic* and `code` text. Second line of the same paragraph.

        ## Lists and steps

        - First bullet
        - Second bullet with **bold**
            - Nested bullet
        - Third bullet

        3. Third step
        4. Fourth step

        ## Picture time

        ![A small blue square.](images/sq.png)
        *Figure: the blue square.*

        > **Tip:** Press `Space` to play.

        > **Note:** A note over
        > two lines.

        > **Warning:** Back up your work.

        | Key | Action |
        |---|---|
        | `Space` | Play or pause |
        | **F12** | Open Preferences |

        ### A sub-section

        See **Chapter 1: Second Chapter** for more.
        """;

    private const string TutorialChapterTwo = """
        ---
        title: Second Chapter
        id: second
        order: 1
        keywords: palm mute, muting
        summary: Where the loop and the metronome live.
        ---

        # Second Chapter

        Use the metronome to keep time. The Metronome button sits beside the loop control.

        ## Loop a section

        Select bars, then press the loop button. LOOPING repeats the range.

        ## Count in

        A count-in plays clicks before the song starts.
        """;

    private static TutorialLibrary SyntheticTutorial() => TutorialLibrary.FromChapters(new[]
    {
        TutorialLibrary.Parse("00-welcome.md", TutorialChapterOne),
        TutorialLibrary.Parse("01-second.md", TutorialChapterTwo),
    });

    private static void TestTutorialMarkdown()
    {
        var doc = TutorialMarkdown.Parse(TutorialChapterOne);
        Check("tutorial markdown: front matter keys are read", doc.FrontMatter["title"] == "Welcome to the Test" && doc.FrontMatter["id"] == "welcome" && doc.FrontMatter["order"] == "0" && doc.FrontMatter["summary"].StartsWith("A short", StringComparison.Ordinal));
        var headings = doc.Blocks.OfType<TutorialHeading>().ToList();
        Check("tutorial markdown: headings keep level and get unique anchors", headings.Count == 4 && headings[0].Level == 1 && headings[1].Id == "lists-and-steps" && headings.Last().Level == 3);
        var para = doc.Blocks.OfType<TutorialParagraph>().First();
        Check("tutorial markdown: bold, italic and inline code, two lines in one paragraph",
            para.Spans.Any(s => s.Bold && s.Text == "bold") && para.Spans.Any(s => s.Italic && s.Text == "italic") && para.Spans.Any(s => s.Code && s.Text == "code")
            && TutorialMarkdown.PlainText(para.Spans).Contains("Second line", StringComparison.Ordinal));
        var items = doc.Blocks.OfType<TutorialListItem>().ToList();
        Check("tutorial markdown: unordered list with a nested item", items.Count(i => !i.Ordered) == 4 && items.Any(i => i.Depth == 1 && TutorialMarkdown.PlainText(i.Spans) == "Nested bullet"));
        var ordered = items.Where(i => i.Ordered).ToList();
        Check("tutorial markdown: an ordered list keeps its starting number", ordered.Count == 2 && ordered[0].Number == 3 && ordered[1].Number == 4);
        var image = doc.Blocks.OfType<TutorialImage>().Single();
        Check("tutorial markdown: an image and the italic line after it as caption", image.Source == "images/sq.png" && image.Alt == "A small blue square." && image.Caption == "Figure: the blue square.");
        var callouts = doc.Blocks.OfType<TutorialCallout>().ToList();
        Check("tutorial markdown: Tip, Note and Warning callouts", callouts.Select(c => c.Kind).SequenceEqual(new[] { "Tip", "Note", "Warning" }));
        Check("tutorial markdown: a callout over two lines is one paragraph", callouts[1].Blocks.Count == 1 && TutorialMarkdown.PlainText(callouts[1].Blocks[0]) == "A note over two lines.");
        var table = doc.Blocks.OfType<TutorialTable>().Single();
        Check("tutorial markdown: a pipe table with a header and rows", table.Header.Count == 2 && table.Rows.Count == 2 && TutorialMarkdown.PlainText(table.Rows[1][1]) == "Open Preferences");

        // unknown constructs degrade to plain text, never to an error
        var odd = TutorialMarkdown.Parse("A <b>tag</b> and ~~strike~~ and [a link](03-x.md#top) and ![inline](x.png) here.\n\n#### Deep heading\n\n- [ ] task item");
        var oddText = string.Join(" ", odd.Blocks.Select(TutorialMarkdown.PlainText));
        Check("tutorial markdown: unknown constructs stay as readable text", oddText.Contains("<b>tag</b>", StringComparison.Ordinal) && oddText.Contains("~~strike~~", StringComparison.Ordinal)
            && oddText.Contains("a link", StringComparison.Ordinal) && oddText.Contains("Deep heading", StringComparison.Ordinal) && oddText.Contains("task item", StringComparison.Ordinal),
            oddText);
        Check("tutorial markdown: a link keeps its target", odd.Blocks.OfType<TutorialParagraph>().First().Spans.Any(s => s.Href == "03-x.md#top" && s.Text == "a link"));

        // malformed input must never throw
        var nasty = new[]
        {
            "", "\0\0\0", "---", "---\nno end", "---\n---\n---", "# ", "#", "**", "**bold", "*", "_", "`", "``", "[", "[](", "[a](", "![", "![a](", "![a]()", "> ", ">", "> **Tip:**", "|", "| a |", "| a |\n|", "|-|-|",
            "```", "```\nunclosed", "<!--", "<!-- x", "- ", "1. ", "    - deep\n        - deeper\n            - deepest\n                - way down", "\\", "a\\", new string('*', 5000), new string('[', 3000),
            string.Concat(Enumerable.Repeat("**a_b*c`d[e](f) ", 400)), "﻿# Bom heading", "\r\n\r\n\r\n", "> > nested quote", "![x](images/a.png)\r\n*Figure: no end"
        };
        var threw = new List<string>();
        foreach (var text in nasty)
            try { var d = TutorialMarkdown.Parse(text); _ = d.Blocks.Select(TutorialMarkdown.PlainText).ToList(); }
            catch (Exception ex) { threw.Add($"{ex.GetType().Name} on '{(text.Length > 20 ? text[..20] : text).Replace("\n", "\\n")}'"); }
        var random = new Random(7);
        const string alphabet = "ab*_`[]()!#>|-. \n\t\\1:";
        for (var n = 0; n < 400; n++)
        {
            var sb = new StringBuilder();
            for (var k = random.Next(1, 120); k > 0; k--) sb.Append(alphabet[random.Next(alphabet.Length)]);
            try { _ = TutorialMarkdown.Parse(sb.ToString()).Blocks.Select(TutorialMarkdown.PlainText).ToList(); }
            catch (Exception ex) { threw.Add($"{ex.GetType().Name} on random '{sb.ToString().Replace("\n", "\\n")}'"); break; }
        }
        Check("tutorial markdown: malformed and random input never throws", threw.Count == 0, string.Join("; ", threw.Take(4)));
        Check("tutorial markdown: front matter that never closes is not front matter", TutorialMarkdown.Parse("---\ntitle: x\n\n# Heading").FrontMatter.Count == 0);
        Check("tutorial markdown: Slug makes lower-case hyphenated anchors", TutorialMarkdown.Slug("Try it: Play Ashen Meridian!") == "try-it-play-ashen-meridian");
    }

    private static void TestTutorialSearch()
    {
        var library = SyntheticTutorial();
        Check("tutorial search: the library lists chapters in order with their numbers", library.Chapters.Count == 2 && library.Chapters[0].Id == "welcome" && library.Chapters[1].Number == 1 && library.Chapters[1].Label == "Chapter 1" && library.Chapters[0].Label == "Welcome");
        var hits = library.Search("metronome");
        Check("tutorial search: finds the chapter that mentions a word", hits.Count >= 1 && hits[0].Chapter.Id == "second", string.Join(",", hits.Select(h => h.Chapter.Id + "/" + h.HeadingText)));
        var upper = library.Search("METRONOME");
        var mixed = library.Search("MeTrOnOmE");
        Check("tutorial search: case-insensitive", upper.Count == hits.Count && mixed.Count == hits.Count && upper[0].Chapter.Id == hits[0].Chapter.Id);
        var heading = library.Search("loop a section");
        Check("tutorial search: a heading hit ranks first and carries its anchor", heading.Count >= 1 && heading[0].HeadingId == "loop-a-section", string.Join(",", heading.Select(h => h.HeadingId)));
        Check("tutorial search: every word must match", library.Search("metronome unicorn").Count == 0 && library.Search("count clicks").Count == 1);
        Check("tutorial search: an empty or blank query returns nothing", library.Search("").Count == 0 && library.Search("   ").Count == 0 && library.Search(null).Count == 0);
        var snippetHit = library.Search("looping").First();
        var highlighted = snippetHit.Highlights.Select(h => snippetHit.Snippet.Substring(h.Start, h.Length)).ToList();
        Check("tutorial search: the snippet marks the match (as typed in the text, any case)", snippetHit.Highlights.Count >= 1 && highlighted.All(h => h.Equals("looping", StringComparison.OrdinalIgnoreCase)), string.Join("|", highlighted) + " in " + snippetHit.Snippet);
        Check("tutorial search: keywords and the summary are searchable", library.Search("palm mute").Any(h => h.Chapter.Id == "second") && library.Search("synthetic chapter").Any(h => h.Chapter.Id == "welcome"));
        var longText = string.Join(" ", Enumerable.Repeat("filler words go here", 120)) + " needle " + string.Join(" ", Enumerable.Repeat("more filler text", 120));
        var (snippet, marks) = TutorialLibrary.MakeSnippet(longText, new[] { "needle" });
        Check("tutorial search: a long section gives a short snippet around the match", snippet.Length < 220 && marks.Count == 1 && snippet.Substring(marks[0].Start, marks[0].Length) == "needle", snippet.Length.ToString());
        Check("tutorial search: unusual input does not throw", Guard2(() => { library.Search(new string('a', 5000)); library.Search("\0 \t ** [ ( \\"); library.Search("a b c d e f g h i j k l m n"); }));
        // The index is built once at load: searching 300 times must not read any file (the library here has no folder at all).
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 300; i++) library.Search("loop");
        Check("tutorial search: repeated searches are served from memory", watch.ElapsedMilliseconds < 2000, watch.ElapsedMilliseconds + " ms");
        var empty = TutorialLibrary.Load(Path.Combine(Path.GetTempPath(), "tabforge-no-such-tutorial-" + Environment.ProcessId));
        Check("tutorial: a missing folder is an empty library, not an error", empty.Chapters.Count == 0 && empty.Search("x").Count == 0);
        Check("tutorial: a bold \"Chapter N: Title\" resolves to that chapter", library.ResolveChapterReference("Chapter 1: Second Chapter")?.Id == "second" && library.ResolveChapterReference("Chapter 9: Nowhere") is null && library.ResolveChapterReference("Bold text") is null);
        Check("tutorial: chapter links resolve by file name and heading", library.TryResolveLink("01-second.md#count-in", library.Chapters[0], out var target, out var anchor) && target.Id == "second" && anchor == "count-in"
            && !library.TryResolveLink("https://example.com", library.Chapters[0], out _, out _) && !library.TryResolveLink("99-none.md", library.Chapters[0], out _, out _));
    }

    private static bool Guard2(Action action)
    {
        try { action(); return true; }
        catch (Exception) { return false; }
    }

    private static string WriteSyntheticTutorialFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tabforge-tutorial-test-{Environment.ProcessId}");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(Path.Combine(folder, "images"));
        File.WriteAllText(Path.Combine(folder, "00-welcome.md"), TutorialChapterOne, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "01-second.md"), TutorialChapterTwo + "\n\n" + string.Join("\n\n", Enumerable.Range(1, 40).Select(i => $"## Extra section {i}\n\nParagraph {i} with enough words to fill a line and make the document long enough to need a second page of text. " + string.Concat(Enumerable.Repeat("More words follow here. ", 12)))), new UTF8Encoding(false));
        // a small picture the chapter refers to
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.SteelBlue, null, new System.Windows.Rect(0, 0, 240, 120));
        var bitmap = new RenderTargetBitmap(240, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(folder, "images", "sq.png"))) encoder.Save(stream);
        return folder;
    }

    private static void TestTutorialWindow()
    {
        var folder = WriteSyntheticTutorialFolder();
        try
        {
            var library = TutorialLibrary.Load(folder);
            Check("tutorial window: chapters load from a folder (front matter order, ids)", library.Chapters.Select(c => c.Id).SequenceEqual(new[] { "welcome", "second" }));
            var window = new TutorialWindow(library, null);
            try
            {
                Check("tutorial window: opens on the first chapter", window.CurrentChapter?.Id == "welcome");
                window.NavigateTo(library.Chapters[1], null, addHistory: true);
                window.NavigateTo(library.Chapters[0], "lists-and-steps", addHistory: true);
                Check("tutorial window: history records each visit", window.HistoryCount == 3);
                window.GoHistory(-1);
                var back = window.CurrentChapter?.Id;
                window.GoHistory(1);
                Check("tutorial window: Back and Forward walk the history", back == "second" && window.CurrentChapter?.Id == "welcome");
                window.OnLink("01-second.md#count-in");
                Check("tutorial window: a link between chapters opens that chapter", window.CurrentChapter?.Id == "second");
                window.SetSearch("loop");
                Check("tutorial window: typing in the search box fills the results", window.ResultCount >= 1);
                window.SetSearch("");
                Check("tutorial window: clearing the search empties the results", window.ResultCount == 0);
                var png = Path.Combine(folder, "window.png");
                TutorialWindow.RenderPng(library, "welcome", null, 900, 600, png);
                Check("tutorial window: renders off-screen to a picture", File.Exists(png) && new FileInfo(png).Length > 5_000);
            }
            finally { window.Close(); }
            var emptyWindow = new TutorialWindow(TutorialLibrary.Empty(), null);
            Check("tutorial window: no content shows the friendly message, not an error", emptyWindow.CurrentChapter is null);
            emptyWindow.Close();
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    private static void TestTutorialPdfExport()
    {
        var folder = WriteSyntheticTutorialFolder();
        var pdf = Path.Combine(folder, "guide.pdf");
        try
        {
            var library = TutorialLibrary.Load(folder);
            var options = new TutorialPdfOptions { VersionText = "Version test", BrandFolder = Path.Combine(folder, "assets", "brand"), ImageRoot = folder };
            var steps = new List<string>();
            // the app exports on a worker thread; the test does the same
            var pages = Task.Run(() => TutorialPdfExporter.Export(library, pdf, options, (d, t, text) => { lock (steps) steps.Add(text); })).GetAwaiter().GetResult();
            Check("tutorial pdf: the file exists", File.Exists(pdf) && new FileInfo(pdf).Length > 3_000);
            var bytes = File.ReadAllBytes(pdf);
            Check("tutorial pdf: starts with a valid %PDF header", bytes.Length > 8 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-");
            Check("tutorial pdf: more than two pages (cover, contents and the chapters)", pages > 2, $"{pages} pages");
            var text = Encoding.Latin1.GetString(bytes);
            Check("tutorial pdf: the page tree agrees with the page count", System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page\b").Count == pages, text.Length.ToString());
            Check("tutorial pdf: text is real text in an embedded Segoe UI subset", text.Contains("/FontFile2", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(text, @"/BaseFont\s*/[A-Z]{6}\+Segoe"));
            Check("tutorial pdf: progress was reported per chapter", steps.Contains("Welcome to the Test") && steps.Contains("Second Chapter") && steps.Contains("Done"));
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var before = new FileInfo(pdf).Length;
            var stopped = false;
            try { TutorialPdfExporter.Export(library, pdf, options, null, cancelled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check("tutorial pdf: cancelling stops the export and leaves the earlier file alone", stopped && new FileInfo(pdf).Length == before);
            var refused = false;
            try { TutorialPdfExporter.Export(TutorialLibrary.Empty(), Path.Combine(folder, "none.pdf"), options); }
            catch (InvalidOperationException) { refused = true; }
            Check("tutorial pdf: no content is refused with a clear error", refused && !File.Exists(Path.Combine(folder, "none.pdf")));
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    private static void TestTutorialCommandAndSettings()
    {
        var action = HotkeyCatalog.ById("Help.Tutorial");
        Check("tutorial: Help.Tutorial is a bindable command", action is not null && action.Name == "Tutorial");
        Check("tutorial: F1 stays with the shortcut reference, so Help.Tutorial has no default key", action?.DefaultGesture == "" && HotkeyCatalog.ById("App.Shortcuts")?.DefaultGesture == "F1");
        var settings = new AppSettings();
        settings.General.TutorialLastChapter = "first-riff";
        settings.General.TutorialWindowWidth = 1100;
        settings.General.TutorialWindowHeight = 700;
        var path = Path.Combine(Path.GetTempPath(), $"tabforge-tutorial-settings-{Environment.ProcessId}.json");
        try
        {
            SettingsFileService.SaveAtomic(path, settings);
            var back = SettingsFileService.Load(path);
            Check("tutorial: the last chapter and window size survive save and load", back.General.TutorialLastChapter == "first-riff" && back.General.TutorialWindowWidth == 1100 && back.General.TutorialWindowHeight == 700);
            var wild = new AppSettings();
            wild.General.TutorialWindowWidth = 9_999_999;
            wild.General.TutorialWindowHeight = -5;
            wild.General.TutorialLastChapter = new string('x', 3_000);
            SettingsFileService.SaveAtomic(path, wild);
            var clamped = SettingsFileService.Load(path);
            Check("tutorial: absurd saved sizes and ids are clamped", clamped.General.TutorialWindowWidth <= 7_680 && clamped.General.TutorialWindowHeight == 0 && clamped.General.TutorialLastChapter.Length <= 80);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static void TestTutorialGuides()
    {
        var folder = WriteSyntheticTutorialFolder();
        var settingsPath = Path.Combine(Path.GetTempPath(), $"tabforge-tutorial-guide-{Environment.ProcessId}.json");
        try
        {
            File.WriteAllText(Path.Combine(folder, "MAINTENANCE.md"), "# Maintenance\n\nNot a chapter.\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "README.md"), "# Read me\n\nNot a chapter.\n", new UTF8Encoding(false));
            var detailedFolder = Path.Combine(folder, "detailed");
            Directory.CreateDirectory(detailedFolder);
            File.WriteAllText(Path.Combine(detailedFolder, "00-detail-one.md"), "---\ntitle: Detail One\nid: detail-one\norder: 1\n---\n\n# Detail One\n\n## Deep topic\n\nThe zebrafish technique in full detail.\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(detailedFolder, "MAINTENANCE.md"), "# Maintenance\n\nNot a chapter.\n", new UTF8Encoding(false));

            var basic = TutorialLibrary.Load(folder);
            var detailed = TutorialLibrary.Load(detailedFolder);
            Check("tutorial guides: the basic library skips detailed\\, MAINTENANCE.md and README", basic.Chapters.Select(c => c.Id).SequenceEqual(new[] { "welcome", "second" }) && basic.Guide == TutorialGuide.Basic);
            Check("tutorial guides: the detailed folder loads as the Detailed Guide without MAINTENANCE.md", detailed.Guide == TutorialGuide.Detailed && detailed.Chapters.Select(c => c.Id).SequenceEqual(new[] { "detail-one" }));
            Check("tutorial guides: search covers only its own guide", basic.Search("zebrafish").Count == 0 && detailed.Search("zebrafish").Count >= 1);

            var window = new TutorialWindow(basic, detailed, null, null);
            try
            {
                Check("tutorial guides: opens on the Basic Guide with the switch enabled", window.Guide == TutorialGuide.Basic && window.DetailedGuideAvailable && window.CurrentChapter?.Id == "welcome");
                window.SwitchGuide(TutorialGuide.Detailed);
                Check("tutorial guides: switching loads the Detailed Guide's contents and reader", window.Guide == TutorialGuide.Detailed && window.Library.Chapters.Count == 1 && window.CurrentChapter?.Id == "detail-one");
                window.SetSearch("zebrafish");
                var inDetailed = window.ResultCount;
                window.SwitchGuide(TutorialGuide.Basic);
                window.SetSearch("zebrafish");
                Check("tutorial guides: the search index follows the guide shown", inDetailed >= 1 && window.ResultCount == 0 && window.CurrentChapter?.Id == "welcome");
            }
            finally { window.Close(); }

            var noDetailed = new TutorialWindow(basic, TutorialLibrary.Empty(Path.Combine(folder, "missing")), null, TutorialGuide.Detailed);
            try
            {
                noDetailed.SwitchGuide(TutorialGuide.Detailed);
                Check("tutorial guides: a missing detailed folder disables the switch and stays on the Basic Guide", !noDetailed.DetailedGuideAvailable && noDetailed.Guide == TutorialGuide.Basic);
            }
            finally { noDetailed.Close(); }

            var options = TutorialPdfOptions.For(detailed, "Version test");
            Check("tutorial guides: the PDF cover title and file name follow the guide",
                options.Title == "TabForge Detailed Guide" && TutorialPdfOptions.For(basic, "").Title == "TabForge Basic Guide"
                && TutorialPdfOptions.DefaultFileName(TutorialGuide.Detailed) == "TabForge Detailed Guide.pdf" && TutorialPdfOptions.DefaultFileName(TutorialGuide.Basic) == "TabForge Basic Guide.pdf");

            using (var store = AppSettingsStore.Open(settingsPath, TimeSpan.FromMilliseconds(50)))
            {
                var w2 = new TutorialWindow(basic, detailed, store, null);
                try { w2.SwitchGuide(TutorialGuide.Detailed); store.Flush(); }
                finally { w2.Close(); }
            }
            using (var store = AppSettingsStore.Open(settingsPath, TimeSpan.FromMilliseconds(50)))
            {
                Check("tutorial guides: the last guide is saved and reloaded", store.Settings.General.TutorialLastGuide == "detailed");
                var w3 = new TutorialWindow(basic, detailed, store, null);
                try { Check("tutorial guides: the window reopens on the last guide", w3.Guide == TutorialGuide.Detailed); }
                finally { w3.Close(); }
            }
            var wild = new AppSettings();
            wild.General.TutorialLastGuide = "bogus";
            SettingsFileService.SaveAtomic(settingsPath, wild);
            Check("tutorial guides: an unknown saved guide falls back to basic", SettingsFileService.Load(settingsPath).General.TutorialLastGuide == "basic");
            var action = HotkeyCatalog.ById("Help.TutorialDetailed");
            Check("tutorial guides: Help.TutorialDetailed is a bindable command, unbound by default", action is not null && action.DefaultGesture == "");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            try { File.Delete(settingsPath); } catch (IOException) { }
        }
    }
}
