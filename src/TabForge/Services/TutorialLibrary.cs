using System.Globalization;
using System.IO;
using System.Text;

namespace TabForge.Services;

/// <summary>The two guides TabForge ships: the Basic Guide (<c>tutorial\</c>) and the Detailed Guide (<c>tutorial\detailed\</c>).</summary>
public enum TutorialGuide { Basic, Detailed }

/// <summary>One chapter of the tutorial (a Markdown file in the tutorial folder).</summary>
public sealed class TutorialChapter
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string Title { get; init; }
    public int Order { get; init; }
    public string Summary { get; init; } = "";
    public string Keywords { get; init; } = "";
    public required TutorialDocument Document { get; init; }

    /// <summary>The chapter's headings below the title (level 2 and 3), in order.</summary>
    public IEnumerable<TutorialHeading> SubHeadings => Document.Blocks.OfType<TutorialHeading>().Where(h => h.Level >= 2);

    /// <summary>The chapter number the text refers to ("Chapter 3"): the front-matter order for 1 to 99; 0 for the welcome chapter and the appendices.</summary>
    public int Number => Order is >= 1 and <= 99 ? Order : 0;

    /// <summary>"Chapter 3", "Welcome" or "Appendix 1": the small label above a chapter title.</summary>
    public string Label
    {
        get
        {
            if (Number > 0) return $"Chapter {Number}";
            var m = System.Text.RegularExpressions.Regex.Match(FileName, @"^[Aa](\d+)-");
            if (m.Success) return $"Appendix {m.Groups[1].Value}";
            return Order <= 0 ? "Welcome" : "Reference";
        }
    }

    /// <summary>The title as listed in the contents: "3. Practice tools" for chapters, the plain title otherwise.</summary>
    public string DisplayTitle => Number > 0 ? $"{Number}. {Title}" : Title;
}

/// <summary>A piece of a chapter that one search hit points at: the text under one heading.</summary>
public sealed record TutorialSearchResult(TutorialChapter Chapter, string? HeadingId, string HeadingText, string Snippet,
    IReadOnlyList<(int Start, int Length)> Highlights, int Score);

// Owns: the tutorial chapters parsed once and their full-text search index.
// Does not own: the Markdown parsing (TutorialMarkdown) and the tutorial window.
// Tests: TestTutorialSearch, TestTutorialWindow.
/// <summary>
/// The tutorial content: every chapter parsed once, plus an in-memory full-text index built at the same time (so a keystroke in the
/// search box never touches a file). <see cref="Load"/> never throws; unreadable files are skipped and counted.
/// </summary>
public sealed class TutorialLibrary
{
    private sealed record Section(TutorialChapter Chapter, string? HeadingId, string Heading, string Body);

    private readonly List<Section> _sections = new();

    public string Folder { get; }
    /// <summary>Which guide this folder is: a folder named "detailed" is the Detailed Guide.</summary>
    public TutorialGuide Guide => string.Equals(Path.GetFileName(Folder.TrimEnd('\\', '/')), DetailedFolderName, StringComparison.OrdinalIgnoreCase) ? TutorialGuide.Detailed : TutorialGuide.Basic;
    public IReadOnlyList<TutorialChapter> Chapters { get; }
    public int SkippedFiles { get; private set; }

    private TutorialLibrary(string folder, IReadOnlyList<TutorialChapter> chapters)
    {
        Folder = folder;
        Chapters = chapters;
        foreach (var chapter in chapters) IndexChapter(chapter);
    }

    /// <summary>An empty library (no tutorial content found).</summary>
    public static TutorialLibrary Empty(string folder = "") => new(folder, Array.Empty<TutorialChapter>());

    /// <summary>
    /// Where the tutorial ships: <c>tutorial\</c> beside the program; when running from a build folder inside the source tree,
    /// the repository's <c>docs\tutorial</c> is used as a fallback.
    /// </summary>
    public static string DefaultFolder()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "tutorial");
        if (HasChapters(shipped)) return shipped;
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "tutorial");
                if (HasChapters(candidate)) return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return shipped;
    }

    public const string DetailedFolderName = "detailed";

    /// <summary>The folder of a guide: the Detailed Guide is the <c>detailed</c> subfolder of the Basic Guide's folder.</summary>
    public static string GuideFolder(TutorialGuide guide) => guide == TutorialGuide.Basic ? DefaultFolder() : Path.Combine(DefaultFolder(), DetailedFolderName);

    /// <summary>True when the folder holds at least one chapter (the Detailed Guide may not be installed or written yet).</summary>
    public static bool FolderHasChapters(string folder) => HasChapters(folder);

    private static bool HasChapters(string folder)
    {
        try { return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.md").Any(IsChapterFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Chapter files are "NN-slug.md" (and "A1-slug.md" for appendices); a read-me or the maintenance notes are not chapters. Only the folder's own files count, so the Basic Guide never picks up the <c>detailed</c> subfolder.</summary>
    private static bool IsChapterFile(string path)
    {
        var name = Path.GetFileName(path);
        return !name.StartsWith("README", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("MAINTENANCE", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads every chapter of a folder (missing or empty folder: an empty library).</summary>
    public static TutorialLibrary Load(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return Empty(folder ?? "");
        var chapters = new List<TutorialChapter>();
        var skipped = 0;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, "*.md").Where(IsChapterFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Empty(folder); }
        foreach (var file in files)
        {
            try
            {
                // a chapter is small text; refuse anything unreasonable rather than parse it
                if (new FileInfo(file).Length > 2_000_000) { skipped++; continue; }
                chapters.Add(Parse(file, File.ReadAllText(file, Encoding.UTF8)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        var ordered = chapters.OrderBy(c => c.Order).ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        return new TutorialLibrary(folder, ordered) { SkippedFiles = skipped };
    }

    /// <summary>Builds a chapter from a file name and its text (also used by tests that never touch the disk).</summary>
    public static TutorialChapter Parse(string fileName, string markdown)
    {
        var name = Path.GetFileName(fileName);
        var stem = Path.GetFileNameWithoutExtension(name);
        var doc = TutorialMarkdown.Parse(markdown);
        var title = doc.FrontMatter.TryGetValue("title", out var t) && t.Length > 0 ? t
            : doc.Blocks.OfType<TutorialHeading>().FirstOrDefault(h => h.Level == 1)?.Text ?? stem;
        var order = doc.FrontMatter.TryGetValue("order", out var o) && int.TryParse(o, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n
            : LeadingNumber(stem);
        var id = doc.FrontMatter.TryGetValue("id", out var i) && i.Length > 0 ? i : stem;
        return new TutorialChapter
        {
            Id = id, FileName = name, Title = title, Order = order, Document = doc,
            Summary = doc.FrontMatter.GetValueOrDefault("summary", ""), Keywords = doc.FrontMatter.GetValueOrDefault("keywords", "")
        };
    }

    /// <summary>Builds a library from chapters already in memory (tests, synthetic content).</summary>
    public static TutorialLibrary FromChapters(IEnumerable<TutorialChapter> chapters, string folder = "")
    {
        var ordered = chapters.OrderBy(c => c.Order).ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        return new TutorialLibrary(folder, ordered);
    }

    private static int LeadingNumber(string stem)
    {
        var digits = new string(stem.TakeWhile(char.IsDigit).ToArray());
        return digits.Length > 0 && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 9999;
    }

    public TutorialChapter? Find(string? idOrFile)
    {
        if (string.IsNullOrWhiteSpace(idOrFile)) return null;
        var name = idOrFile.Trim();
        var stem = name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
        return Chapters.FirstOrDefault(c => c.Id.Equals(name, StringComparison.OrdinalIgnoreCase)
            || c.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(c.FileName).Equals(stem, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a Markdown link ("03-slug.md#heading", "#heading" inside the current chapter). Returns false for web links and
    /// anything that is not a chapter of this tutorial.
    /// </summary>
    public bool TryResolveLink(string? href, TutorialChapter? current, out TutorialChapter chapter, out string? headingId)
    {
        chapter = null!; headingId = null;
        if (string.IsNullOrWhiteSpace(href) || href.Contains("://", StringComparison.Ordinal) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return false;
        var hash = href.IndexOf('#');
        var file = hash >= 0 ? href[..hash] : href;
        headingId = hash >= 0 && hash + 1 < href.Length ? href[(hash + 1)..] : null;
        var target = file.Length == 0 ? current : Find(Uri.UnescapeDataString(file.Replace('\\', '/').Split('/')[^1]));
        if (target is null) return false;
        chapter = target;
        return true;
    }

    private static readonly System.Text.RegularExpressions.Regex ChapterReference = new(@"^Chapter\s+(\d+)\s*:\s*(.+?)\s*$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// A bold "Chapter 9: Shaping the sound" in the text is a cross-reference (the Markdown subset has no links): returns the chapter it
    /// names, or null when the text is anything else or names no chapter of this tutorial.
    /// </summary>
    public TutorialChapter? ResolveChapterReference(string? boldText)
    {
        if (string.IsNullOrEmpty(boldText)) return null;
        var m = ChapterReference.Match(boldText);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return null;
        return Chapters.FirstOrDefault(c => c.Number == number && c.Title.Equals(m.Groups[2].Value.TrimEnd('.'), StringComparison.OrdinalIgnoreCase));
    }

    // ---- search ----

    private void IndexChapter(TutorialChapter chapter)
    {
        string? heading = null;
        string? headingId = null;
        var body = new StringBuilder();
        void Close()
        {
            if (heading is null && body.Length == 0) return;
            _sections.Add(new Section(chapter, headingId, heading ?? chapter.Title, body.ToString().Trim()));
            body.Clear();
        }
        // front-matter summary and keywords belong to the chapter's opening section
        body.Append(chapter.Summary).Append(' ');
        foreach (var block in chapter.Document.Blocks)
        {
            if (block is TutorialHeading { Level: >= 2 } h)
            {
                Close();
                heading = h.Text; headingId = h.Id;
                continue;
            }
            if (block is TutorialHeading) continue;
            body.Append(TutorialMarkdown.PlainText(block)).Append(' ');
        }
        Close();
    }

    /// <summary>
    /// Case-insensitive full-text search over the index built at load time. Every word of the query must occur in the chapter title,
    /// keywords, heading or text of a section; hits in headings, titles and keywords rank first.
    /// </summary>
    public IReadOnlyList<TutorialSearchResult> Search(string? query, int limit = 60)
    {
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        if (terms.Count == 0) return Array.Empty<TutorialSearchResult>();
        var results = new List<TutorialSearchResult>();
        foreach (var section in _sections)
        {
            var score = 0;
            var all = true;
            foreach (var term in terms)
            {
                var inHeading = section.Heading.Contains(term, StringComparison.OrdinalIgnoreCase);
                var inTitle = section.Chapter.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
                var inKeywords = section.Chapter.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase);
                var bodyHits = CountOccurrences(section.Body, term, 5);
                if (!inHeading && !inTitle && !inKeywords && bodyHits == 0) { all = false; break; }
                score += (inHeading ? 10 : 0) + (inTitle ? 4 : 0) + (inKeywords ? 3 : 0) + bodyHits;
            }
            if (!all) continue;
            var (snippet, highlights) = MakeSnippet(section.Body.Length > 0 ? section.Body : section.Chapter.Summary, terms);
            results.Add(new TutorialSearchResult(section.Chapter, section.HeadingId, section.Heading, snippet, highlights, score));
        }
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Chapter.Order).Take(Math.Max(1, limit)).ToList();
    }

    private static int CountOccurrences(string text, string term, int cap)
    {
        var count = 0;
        for (var at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); at >= 0 && count < cap; at = text.IndexOf(term, at + term.Length, StringComparison.OrdinalIgnoreCase))
            count++;
        return count;
    }

    /// <summary>About 150 characters of text around the first hit, with the positions of every query word inside it.</summary>
    internal static (string Snippet, IReadOnlyList<(int Start, int Length)> Highlights) MakeSnippet(string text, IReadOnlyList<string> terms)
    {
        const int Window = 150;
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));   // one line, single spaces
        var first = -1;
        foreach (var term in terms)
        {
            var at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (first < 0 || at < first)) first = at;
        }
        var start = Math.Max(0, first < 0 ? 0 : first - 50);
        if (start > 0) { var space = text.IndexOf(' ', start); if (space >= 0 && space < first) start = space + 1; }
        var length = Math.Min(Window, text.Length - start);
        var end = start + length;
        if (end < text.Length) { var space = text.LastIndexOf(' ', end - 1, Math.Min(30, length)); if (space > start) end = space; }
        var snippet = (start > 0 ? "… " : "") + text[start..end] + (end < text.Length ? " …" : "");
        var offset = start > 0 ? 2 : 0;
        var highlights = new List<(int, int)>();
        foreach (var term in terms)
            for (var at = snippet.IndexOf(term, offset, StringComparison.OrdinalIgnoreCase); at >= 0; at = snippet.IndexOf(term, at + term.Length, StringComparison.OrdinalIgnoreCase))
                highlights.Add((at, term.Length));
        // overlapping hits (two query words inside one another) merge so a snippet is never cut twice
        highlights.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        var merged = new List<(int Start, int Length)>();
        foreach (var (s, l) in highlights)
        {
            if (merged.Count > 0 && s <= merged[^1].Start + merged[^1].Length)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].Length, s + l - merged[^1].Start));
            else merged.Add((s, l));
        }
        return (snippet, merged);
    }
}
