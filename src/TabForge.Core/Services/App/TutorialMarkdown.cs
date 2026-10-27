using System.Text;
using System.Text.RegularExpressions;

namespace TabForge.Services;

/// <summary>One piece of inline text: plain, bold, italic, code, a link, or an image that could not stand alone.</summary>
public sealed record TutorialSpan(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Href = null);

/// <summary>A block of the tutorial Markdown subset (see <see cref="TutorialMarkdown"/>).</summary>
public abstract record TutorialBlock;

public sealed record TutorialHeading(int Level, IReadOnlyList<TutorialSpan> Spans, string Id) : TutorialBlock
{
    public string Text => TutorialMarkdown.PlainText(Spans);
}
public sealed record TutorialParagraph(IReadOnlyList<TutorialSpan> Spans) : TutorialBlock;
/// <summary>One list item; <see cref="Depth"/> is 0 for the top level. Numbered items carry their number.</summary>
public sealed record TutorialListItem(int Depth, bool Ordered, int Number, IReadOnlyList<TutorialSpan> Spans) : TutorialBlock;
public sealed record TutorialImage(string Alt, string Source, string? Title, string? Caption = null) : TutorialBlock;
/// <summary><see cref="Kind"/> is Tip, Note, Warning, Shortcut (a labelled callout) or Quote (a plain block quote).</summary>
public sealed record TutorialCallout(string Kind, IReadOnlyList<TutorialBlock> Blocks) : TutorialBlock;
public sealed record TutorialTable(IReadOnlyList<IReadOnlyList<TutorialSpan>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<TutorialSpan>>> Rows) : TutorialBlock;
public sealed record TutorialCode(string Text) : TutorialBlock;
public sealed record TutorialRule : TutorialBlock;

/// <summary>A parsed chapter file: its front matter and body blocks.</summary>
public sealed record TutorialDocument(IReadOnlyDictionary<string, string> FrontMatter, IReadOnlyList<TutorialBlock> Blocks);

// Owns: the small Markdown subset the tutorial chapters use, parsed into blocks and spans.
// Does not own: the chapter list and the window drawing.
// Tests: no dedicated test class.
/// <summary>
/// The small Markdown subset the tutorial chapters use: headings (#, ##, ###), paragraphs, bold, italic, inline code, ordered
/// and unordered lists (nested by indentation), images, simple pipe tables, block quotes with Tip / Note / Warning callouts,
/// fenced code, horizontal rules, links, and a simple "key: value" front matter between two --- lines. Anything else is kept as
/// plain text; the parser never throws on malformed input.
/// </summary>
public static partial class TutorialMarkdown
{
    private static readonly Regex HeadingLine = new(@"^(#{1,6})\s+(.*?)(?:\s+#+)?\s*$", RegexOptions.Compiled);
    private static readonly Regex ListLine = new(@"^(\s*)([-*+]|\d{1,4}[.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex RuleLine = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex ImageOnly = new(@"^!\[(?<alt>[^\]]*)\]\((?<src>[^)\s]+)(?:\s+""(?<title>[^""]*)"")?\)$", RegexOptions.Compiled);
    private static readonly Regex ItalicOnly = new(@"^(?:\*(?!\*)(.+?)\*|_(?!_)(.+?)_)$", RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new(@"^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex CalloutStart = new(@"^\s*(?:\*\*|__)\s*(Tip|Note|Warning|Shortcut)\s*:?\s*(?:\*\*|__)\s*:?\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Parses a whole chapter file (front matter then body).</summary>
    public static TutorialDocument Parse(string? markdown)
    {
        var lines = SplitLines(markdown);
        var front = ParseFrontMatter(lines, out var bodyStart);
        var blocks = ParseBlocks(lines, bodyStart, lines.Count);
        return new TutorialDocument(front, blocks);
    }

    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();
        if (text[0] == '﻿') text = text[1..];
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
    }

    /// <summary>Reads "key: value" lines between a leading --- and the next --- (or ...). Without that fence there is no front matter.</summary>
    private static Dictionary<string, string> ParseFrontMatter(List<string> lines, out int bodyStart)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bodyStart = 0;
        var first = 0;
        while (first < lines.Count && lines[first].Trim().Length == 0) first++;
        if (first >= lines.Count || lines[first].Trim() != "---") return map;
        var end = -1;
        for (var i = first + 1; i < lines.Count; i++)
            if (lines[i].Trim() is "---" or "...") { end = i; break; }
        if (end < 0) return map;   // never closed: it was a horizontal rule, not front matter
        string? currentKey = null;
        for (var i = first + 1; i < end; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('-'))
            {
                currentKey = line[..colon].Trim().ToLowerInvariant();
                map[currentKey] = Unquote(line[(colon + 1)..].Trim());
            }
            else if (currentKey is not null && line.TrimStart().StartsWith('-'))
            {
                // a YAML list ("keywords:" then "- item" lines) is joined with commas
                var item = Unquote(line.TrimStart()[1..].Trim());
                map[currentKey] = map[currentKey].Length == 0 ? item : map[currentKey] + ", " + item;
            }
        }
        bodyStart = end + 1;
        return map;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')   // keywords: [a, b, c]
            return string.Join(", ", value[1..^1].Split(',').Select(x => Unquote(x.Trim())).Where(x => x.Length > 0));
        return value;
    }

    private static List<TutorialBlock> ParseBlocks(List<string> lines, int start, int end)
    {
        var blocks = new List<TutorialBlock>();
        var usedIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var paragraph = new List<string>();
        var i = start;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var text = string.Join(" ", paragraph.Select(l => l.Trim()));
            paragraph.Clear();
            blocks.Add(ParagraphBlock(text));
        }

        while (i < end)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0) { FlushParagraph(); i++; continue; }

            // HTML comment on its own line(s): dropped.
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal)) { FlushParagraph(); i = SkipHtmlComment(lines, i, end); continue; }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                FlushParagraph();
                i = ParseFencedCode(lines, i, end, trimmed[..3], blocks);
                continue;
            }

            var imageLine = ImageOnly.Match(trimmed);
            if (imageLine.Success)
            {
                FlushParagraph();
                i = ParseImageLine(lines, i, end, imageLine, blocks);
                continue;
            }

            var heading = HeadingLine.Match(trimmed);
            if (heading.Success && line.Length - line.TrimStart().Length < 4)
            {
                FlushParagraph();
                blocks.Add(MakeHeading(heading, usedIds));
                i++;
                continue;
            }

            if (RuleLine.IsMatch(line) && paragraph.Count == 0) { blocks.Add(new TutorialRule()); i++; continue; }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                i = ParseQuote(lines, i, end, blocks);
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < end && TableSeparator.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
            {
                FlushParagraph();
                i = ParseTable(lines, i, end, trimmed, blocks);
                continue;
            }

            if (ListLine.IsMatch(line) && !RuleLine.IsMatch(line)) { FlushParagraph(); i = ParseList(lines, i, end, blocks); continue; }

            paragraph.Add(line);
            i++;
        }
        FlushParagraph();
        return blocks;
    }

    /// <summary>A paragraph of text; a paragraph that is only an image is an image block.</summary>
    private static TutorialBlock ParagraphBlock(string text)
    {
        var image = ImageOnly.Match(text);
        if (image.Success)
            return new TutorialImage(image.Groups["alt"].Value, image.Groups["src"].Value, image.Groups["title"].Success ? image.Groups["title"].Value : null);
        return new TutorialParagraph(ParseInline(text));
    }

    /// <summary>Skips an HTML comment that starts on line <paramref name="i"/>; returns the line after its end.</summary>
    private static int SkipHtmlComment(List<string> lines, int i, int end)
    {
        while (i < end && !lines[i].Contains("-->", StringComparison.Ordinal)) i++;
        return i + 1;
    }

    private static TutorialHeading MakeHeading(Match heading, Dictionary<string, int> usedIds)
    {
        var level = Math.Min(3, heading.Groups[1].Length);   // #### and deeper read as ###
        var spans = ParseInline(heading.Groups[2].Value);
        return new TutorialHeading(level, spans, UniqueId(PlainText(spans), usedIds));
    }

    /// <summary>A fenced code block (up to the closing fence; an unclosed fence runs to the end of the file); returns the line after it.</summary>
    private static int ParseFencedCode(List<string> lines, int i, int end, string fence, List<TutorialBlock> blocks)
    {
        var code = new StringBuilder();
        i++;
        while (i < end && !lines[i].Trim().StartsWith(fence, StringComparison.Ordinal)) { code.Append(lines[i]).Append('\n'); i++; }
        i++;   // closing fence (an unclosed fence runs to the end of the file)
        blocks.Add(new TutorialCode(code.ToString().TrimEnd('\n')));
        return i;
    }

    /// <summary>A picture on its own line, with an italic line right after it as its caption; returns the line after it.</summary>
    private static int ParseImageLine(List<string> lines, int i, int end, Match imageLine, List<TutorialBlock> blocks)
    {
        // a picture on its own line; an italic line right after it (one blank line is tolerated for "*Figure: ...*") is its caption
        string? caption = null;
        var probe = i + 1;
        var skippedBlank = false;
        if (probe < end && lines[probe].Trim().Length == 0 && probe + 1 < end) { probe++; skippedBlank = true; }
        if (probe < end && ItalicOnly.Match(lines[probe].Trim()) is { Success: true } italic
            && (!skippedBlank || (italic.Groups[1].Success ? italic.Groups[1] : italic.Groups[2]).Value.StartsWith("Figure", StringComparison.OrdinalIgnoreCase)))
        {
            caption = PlainText(ParseInline((italic.Groups[1].Success ? italic.Groups[1] : italic.Groups[2]).Value));
            i = probe;
        }
        blocks.Add(new TutorialImage(imageLine.Groups["alt"].Value, imageLine.Groups["src"].Value, imageLine.Groups["title"].Success ? imageLine.Groups["title"].Value : null, caption));
        i++;
        return i;
    }

    private static int ParseQuote(List<string> lines, int i, int end, List<TutorialBlock> blocks)
    {
        var quote = new List<string>();
        while (i < end && lines[i].Trim().StartsWith('>'))
        {
            var q = lines[i].Trim()[1..];
            quote.Add(q.StartsWith(' ') ? q[1..] : q);
            i++;
        }
        blocks.Add(MakeCallout(quote));
        return i;
    }

    private static int ParseTable(List<string> lines, int i, int end, string trimmed, List<TutorialBlock> blocks)
    {
        var header = SplitRow(trimmed).Select(c => (IReadOnlyList<TutorialSpan>)ParseInline(c)).ToList();
        var rows = new List<IReadOnlyList<IReadOnlyList<TutorialSpan>>>();
        i += 2;
        while (i < end && lines[i].Trim().StartsWith('|'))
        {
            var cells = SplitRow(lines[i].Trim()).Select(c => (IReadOnlyList<TutorialSpan>)ParseInline(c)).ToList();
            while (cells.Count < header.Count) cells.Add(Array.Empty<TutorialSpan>());
            rows.Add(cells);
            i++;
        }
        blocks.Add(new TutorialTable(header, rows));
        return i;
    }

    private static TutorialBlock MakeCallout(List<string> quote)
    {
        var kind = "Quote";
        var first = quote.FindIndex(l => l.Trim().Length > 0);
        if (first >= 0)
        {
            var match = CalloutStart.Match(quote[first]);
            if (match.Success)
            {
                kind = char.ToUpperInvariant(match.Groups[1].Value[0]) + match.Groups[1].Value[1..].ToLowerInvariant();
                quote = new List<string>(quote);
                quote[first] = match.Groups[2].Value;
            }
        }
        return new TutorialCallout(kind, ParseBlocks(quote, 0, quote.Count));
    }

    private static int ParseList(List<string> lines, int i, int end, List<TutorialBlock> blocks)
    {
        var indents = new List<int>();   // indent widths of the open nesting levels
        TutorialListItem? last = null;
        var lastText = new StringBuilder();
        var counters = new Dictionary<int, int>();
        var orderedAt = new Dictionary<int, bool>();   // a switch between bullets and numbers at one depth starts a new list

        void FlushItem()
        {
            if (last is null) return;
            blocks.Add(last with { Spans = ParseInline(lastText.ToString().Trim()) });
            last = null;
            lastText.Clear();
        }

        while (i < end)
        {
            var line = lines[i];
            if (line.Trim().Length == 0)
            {
                // a blank line ends the list unless an indented / list line follows
                var next = i + 1;
                while (next < end && lines[next].Trim().Length == 0) next++;
                if (next < end && ListLine.IsMatch(lines[next]) && !RuleLine.IsMatch(lines[next])) { i = next; continue; }
                break;
            }
            var m = ListLine.Match(line);
            if (m.Success && !RuleLine.IsMatch(line))
            {
                FlushItem();
                var indent = m.Groups[1].Value.Replace("\t", "    ").Length;
                while (indents.Count > 0 && indent < indents[^1]) { counters.Remove(indents.Count - 1); orderedAt.Remove(indents.Count - 1); indents.RemoveAt(indents.Count - 1); }
                if (indents.Count == 0 || indent > indents[^1]) indents.Add(indent);
                var depth = Math.Min(indents.Count - 1, 3);
                var ordered = char.IsDigit(m.Groups[2].Value[0]);
                if (orderedAt.TryGetValue(depth, out var wasOrdered) && wasOrdered != ordered) counters.Remove(depth);
                orderedAt[depth] = ordered;
                counters[depth] = counters.TryGetValue(depth, out var n) ? n + 1 : (ordered && int.TryParse(m.Groups[2].Value.TrimEnd('.', ')'), out var first) ? first : 1);
                last = new TutorialListItem(depth, ordered, counters[depth], Array.Empty<TutorialSpan>());
                lastText.Append(m.Groups[3].Value);
                i++;
                continue;
            }
            // lazy continuation of the previous item (an indented or plain follow-on line)
            if (last is not null && !line.TrimStart().StartsWith('#') && !line.TrimStart().StartsWith('>') && !line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                lastText.Append(' ').Append(line.Trim());
                i++;
                continue;
            }
            break;
        }
        FlushItem();
        return i;
    }

    private static List<string> SplitRow(string row)
    {
        row = row.Trim();
        if (row.StartsWith('|')) row = row[1..];
        if (row.EndsWith('|') && !row.EndsWith("\\|", StringComparison.Ordinal)) row = row[..^1];
        var cells = new List<string>();
        var current = new StringBuilder();
        for (var k = 0; k < row.Length; k++)
        {
            if (row[k] == '\\' && k + 1 < row.Length && row[k + 1] == '|') { current.Append('|'); k++; }
            else if (row[k] == '|') { cells.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(row[k]);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    // ---- inline text ----

}
