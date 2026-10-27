using System.Text;
using System.Text.RegularExpressions;

namespace TabForge.Services;

// Owns: inline span parsing, plain text and slugs.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public static partial class TutorialMarkdown
{
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
