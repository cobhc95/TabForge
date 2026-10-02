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
// Tests: TestTutorialMarkdown.
/// <summary>
/// The small Markdown subset the tutorial chapters use: headings (#, ##, ###), paragraphs, bold, italic, inline code, ordered
/// and unordered lists (nested by indentation), images, simple pipe tables, block quotes with Tip / Note / Warning callouts,
/// fenced code, horizontal rules, links, and a simple "key: value" front matter between two --- lines. Anything else is kept as
/// plain text; the parser never throws on malformed input.
/// </summary>
public static class TutorialMarkdown
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
            var image = ImageOnly.Match(text);
            if (image.Success)
                blocks.Add(new TutorialImage(image.Groups["alt"].Value, image.Groups["src"].Value, image.Groups["title"].Success ? image.Groups["title"].Value : null));
            else
                blocks.Add(new TutorialParagraph(ParseInline(text)));
        }

        while (i < end)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0) { FlushParagraph(); i++; continue; }

            // HTML comment on its own line(s): dropped.
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                FlushParagraph();
                while (i < end && !lines[i].Contains("-->", StringComparison.Ordinal)) i++;
                i++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                FlushParagraph();
                var fence = trimmed[..3];
                var code = new StringBuilder();
                i++;
                while (i < end && !lines[i].Trim().StartsWith(fence, StringComparison.Ordinal)) { code.Append(lines[i]).Append('\n'); i++; }
                i++;   // closing fence (an unclosed fence runs to the end of the file)
                blocks.Add(new TutorialCode(code.ToString().TrimEnd('\n')));
                continue;
            }

            var imageLine = ImageOnly.Match(trimmed);
            if (imageLine.Success)
            {
                // a picture on its own line; an italic line right after it (one blank line is tolerated for "*Figure: ...*") is its caption
                FlushParagraph();
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
                continue;
            }

            var heading = HeadingLine.Match(trimmed);
            if (heading.Success && line.Length - line.TrimStart().Length < 4)
            {
                FlushParagraph();
                var level = Math.Min(3, heading.Groups[1].Length);   // #### and deeper read as ###
                var spans = ParseInline(heading.Groups[2].Value);
                blocks.Add(new TutorialHeading(level, spans, UniqueId(PlainText(spans), usedIds)));
                i++;
                continue;
            }

            if (RuleLine.IsMatch(line) && paragraph.Count == 0) { blocks.Add(new TutorialRule()); i++; continue; }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var quote = new List<string>();
                while (i < end && lines[i].Trim().StartsWith('>'))
                {
                    var q = lines[i].Trim()[1..];
                    quote.Add(q.StartsWith(' ') ? q[1..] : q);
                    i++;
                }
                blocks.Add(MakeCallout(quote));
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < end && TableSeparator.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
            {
                FlushParagraph();
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
                continue;
            }

            var list = ListLine.Match(line);
            if (list.Success && RuleLine.IsMatch(line) == false)
            {
                FlushParagraph();
                i = ParseList(lines, i, end, blocks);
                continue;
            }

            paragraph.Add(line);
            i++;
        }
        FlushParagraph();
        return blocks;
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

    /// <summary>Parses bold, italic, inline code and links. Unmatched markers stay as literal text.</summary>
    public static IReadOnlyList<TutorialSpan> ParseInline(string? text)
    {
        var spans = new List<TutorialSpan>();
        if (string.IsNullOrEmpty(text)) return spans;
        ParseInlineInto(text, 0, text.Length, false, false, null, spans);
        return Merge(spans);
    }

    private static void ParseInlineInto(string s, int from, int to, bool bold, bool italic, string? href, List<TutorialSpan> spans)
    {
        var buffer = new StringBuilder();
        void Flush()
        {
            if (buffer.Length == 0) return;
            spans.Add(new TutorialSpan(buffer.ToString(), bold, italic, false, href));
            buffer.Clear();
        }
        var i = from;
        while (i < to)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < to && "\\`*_{}[]()#+-.!|<>~".IndexOf(s[i + 1]) >= 0) { buffer.Append(s[i + 1]); i += 2; continue; }
            if (c == '`')
            {
                var close = s.IndexOf('`', i + 1, to - i - 1);
                if (close > i)
                {
                    Flush();
                    spans.Add(new TutorialSpan(s[(i + 1)..close], bold, italic, true, href));
                    i = close + 1;
                    continue;
                }
            }
            if ((c == '!' && i + 1 < to && s[i + 1] == '[') || c == '[')
            {
                var isImage = c == '!';
                var open = isImage ? i + 1 : i;
                if (TryLink(s, open, to, out var textEnd, out var target, out var end))
                {
                    Flush();
                    if (isImage)
                        buffer.Append(s[(open + 1)..textEnd]);   // an image inside a paragraph degrades to its description
                    else if (href is null)
                    {
                        ParseInlineInto(s, open + 1, textEnd, bold, italic, target, spans);
                    }
                    else buffer.Append(s[(open + 1)..textEnd]);
                    i = end;
                    continue;
                }
            }
            if (c == '*' || c == '_')
            {
                var run = 1;
                while (i + run < to && s[i + run] == c) run++;
                if (run >= 2 && CanClose(s, i, 2, c, to, i + 2) is var closeBold && closeBold > 0 && Opens(s, i, c, 2))
                {
                    Flush();
                    ParseInlineInto(s, i + 2, closeBold, true, italic, href, spans);
                    i = closeBold + 2;
                    continue;
                }
                if (Opens(s, i, c, 1) && CanClose(s, i, 1, c, to, i + 1) is var closeItalic && closeItalic > 0)
                {
                    Flush();
                    ParseInlineInto(s, i + 1, closeItalic, bold, true, href, spans);
                    i = closeItalic + 1;
                    continue;
                }
                buffer.Append(s, i, run);
                i += run;
                continue;
            }
            buffer.Append(c);
            i++;
        }
        Flush();
    }

    /// <summary>An opening marker needs a non-space after it; for '_' also a non-word character before it.</summary>
    private static bool Opens(string s, int i, char c, int len)
    {
        if (i + len >= s.Length || char.IsWhiteSpace(s[i + len])) return false;
        if (c == '_' && i > 0 && char.IsLetterOrDigit(s[i - 1])) return false;
        return true;
    }

    /// <summary>Index of the closing marker of <paramref name="len"/> characters, or -1.</summary>
    private static int CanClose(string s, int open, int len, char c, int to, int searchFrom)
    {
        for (var k = searchFrom; k + len <= to; k++)
        {
            if (s[k] == '`') { var close = s.IndexOf('`', k + 1, to - k - 1); if (close > 0) { k = close; continue; } }
            if (s[k] == '\\') { k++; continue; }
            if (s[k] != c) continue;
            var ok = true;
            for (var m = 1; m < len; m++) if (k + m >= to || s[k + m] != c) { ok = false; break; }
            if (!ok) continue;
            if (k == searchFrom || char.IsWhiteSpace(s[k - 1])) continue;                  // the closer follows a non-space
            if (len == 1 && k + 1 < to && s[k + 1] == c) { k++; continue; }                // part of a ** run
            if (c == '_' && k + len < to && char.IsLetterOrDigit(s[k + len])) continue;
            return k;
        }
        return -1;
    }

    private static bool TryLink(string s, int open, int to, out int textEnd, out string target, out int end)
    {
        textEnd = 0; target = ""; end = 0;
        var depth = 0;
        var k = open;
        for (; k < to; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] == '[') depth++;
            else if (s[k] == ']') { depth--; if (depth == 0) break; }
        }
        if (k >= to || k + 1 >= to || s[k + 1] != '(') return false;
        var close = s.IndexOf(')', k + 2, to - k - 2);
        if (close < 0) return false;
        textEnd = k;
        var inside = s[(k + 2)..close].Trim();
        var space = inside.IndexOf(' ');
        target = space > 0 ? inside[..space] : inside;
        end = close + 1;
        return target.Length > 0;
    }

    private static List<TutorialSpan> Merge(List<TutorialSpan> spans)
    {
        var merged = new List<TutorialSpan>();
        foreach (var span in spans)
        {
            if (span.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1] is var prev && prev.Bold == span.Bold && prev.Italic == span.Italic && prev.Code == span.Code && prev.Href == span.Href)
                merged[^1] = prev with { Text = prev.Text + span.Text };
            else merged.Add(span);
        }
        return merged;
    }

    /// <summary>The text of the spans without any formatting.</summary>
    public static string PlainText(IEnumerable<TutorialSpan> spans) => string.Concat(spans.Select(s => s.Text));

    /// <summary>The plain text of a block (callouts, tables and lists included), for searching.</summary>
    public static string PlainText(TutorialBlock block) => block switch
    {
        TutorialHeading h => PlainText(h.Spans),
        TutorialParagraph p => PlainText(p.Spans),
        TutorialListItem l => PlainText(l.Spans),
        TutorialImage img => img.Alt + " " + img.Caption,
        TutorialCallout c => string.Join(" ", c.Blocks.Select(PlainText)),
        TutorialTable t => string.Join(" ", t.Header.Select(PlainText).Concat(t.Rows.SelectMany(r => r.Select(PlainText)))),
        TutorialCode code => code.Text,
        _ => ""
    };

    /// <summary>A lower-case, hyphenated anchor name for a heading ("Your first song!" becomes "your-first-song").</summary>
    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_' && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private static string UniqueId(string text, Dictionary<string, int> used)
    {
        var slug = Slug(text);
        if (slug.Length == 0) slug = "section";
        if (used.TryGetValue(slug, out var count)) { used[slug] = count + 1; return slug + "-" + (count + 1); }
        used[slug] = 1;
        return slug;
    }
}
