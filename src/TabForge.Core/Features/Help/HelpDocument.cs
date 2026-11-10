using System;
using System.Collections.Generic;
using System.Text;

namespace TabForge.Features.Help;

// Owns: the Help chapter model and its parser: one chapter text (a '# Chapter' title, '## Section' headings, paragraphs, '- ' bullets, '1. ' steps,
//   'Tip: ' / 'Note: ' / 'Warning: ' callouts, Problem/Fix and Question/Answer pairs, a final 'Related:' line) into sections of blocks, and the inline
//   spans (**bold**, `code`) of a block's text.
// Does not own: reading the embedded files (Avalonia HelpContent), the chapter list (HelpChapterCatalog), searching (HelpSearchIndex), drawing (Avalonia HelpWindow).
// Tests: TabForge.Avalonia.Tests.HelpDocumentTests.

/// <summary>What one block of a section is.</summary>
public enum HelpBlockKind { Paragraph, Bullet, Step, Tip, Note, Warning, Problem, Question }

/// <summary>A run of text with one style.</summary>
public readonly record struct HelpSpan(string Text, HelpSpanStyle Style);

/// <summary>Plain text, a bold UI name, or a key or other literal in code style.</summary>
public enum HelpSpanStyle { Plain, Bold, Code }

/// <summary>One paragraph, list item, callout or pair. A Problem holds the problem in <see cref="Text"/> and its fix in <see cref="Second"/>; a Question holds the question and its answer.</summary>
public sealed record HelpBlock(HelpBlockKind Kind, string Text, string? Second = null, int Number = 0);

/// <summary>A section: its heading (null for the lead text before the first heading) and its blocks in order.</summary>
public sealed record HelpSection(string? Heading, IReadOnlyList<HelpBlock> Blocks);

/// <summary>A parsed chapter: its title, its sections and the chapter titles its Related line names.</summary>
public sealed record HelpChapter(string Title, IReadOnlyList<HelpSection> Sections, IReadOnlyList<string> Related);

/// <summary>Parses chapter text.</summary>
public static class HelpDocument
{
    private const string RelatedPrefix = "Related:";

    /// <summary>Parses one chapter. <paramref name="knownTitles"/> lets the Related line be split into chapter titles even when a title holds commas; an unknown name is kept as written.</summary>
    public static HelpChapter Parse(string text, IReadOnlyList<string>? knownTitles = null)
    {
        var title = "";
        var sections = new List<HelpSection>();
        var blocks = new List<HelpBlock>();
        string? heading = null;
        var related = new List<string>();
        var paragraph = new StringBuilder();
        string? problem = null, question = null;

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            blocks.Add(new HelpBlock(HelpBlockKind.Paragraph, paragraph.ToString()));
            paragraph.Clear();
        }
        void FlushSection()
        {
            FlushParagraph();
            if (problem is not null) { blocks.Add(new HelpBlock(HelpBlockKind.Problem, problem, "")); problem = null; }
            if (question is not null) { blocks.Add(new HelpBlock(HelpBlockKind.Question, question, "")); question = null; }
            if (heading is not null || blocks.Count > 0) sections.Add(new HelpSection(heading, blocks.ToArray()));
            blocks.Clear();
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').TrimEnd();
            if (line.Length == 0) { FlushParagraph(); continue; }
            if (line.StartsWith("# ", StringComparison.Ordinal)) { title = line[2..].Trim(); continue; }
            if (line.StartsWith("## ", StringComparison.Ordinal)) { FlushSection(); heading = line[3..].Trim(); continue; }
            if (line.StartsWith(RelatedPrefix, StringComparison.Ordinal)) { FlushParagraph(); related.AddRange(SplitRelated(line[RelatedPrefix.Length..], knownTitles)); continue; }
            if (line.StartsWith("Problem: ", StringComparison.Ordinal)) { FlushParagraph(); problem = line[9..].Trim(); continue; }
            if (line.StartsWith("Fix: ", StringComparison.Ordinal) && problem is not null) { blocks.Add(new HelpBlock(HelpBlockKind.Problem, problem, line[5..].Trim())); problem = null; continue; }
            if (line.StartsWith("Question: ", StringComparison.Ordinal)) { FlushParagraph(); question = line[10..].Trim(); continue; }
            if (line.StartsWith("Answer: ", StringComparison.Ordinal) && question is not null) { blocks.Add(new HelpBlock(HelpBlockKind.Question, question, line[8..].Trim())); question = null; continue; }
            if (TryCallout(line, out var callout)) { FlushParagraph(); blocks.Add(callout); continue; }
            if (line.StartsWith("- ", StringComparison.Ordinal)) { FlushParagraph(); blocks.Add(new HelpBlock(HelpBlockKind.Bullet, line[2..].Trim())); continue; }
            if (TryStep(line, out var number, out var step)) { FlushParagraph(); blocks.Add(new HelpBlock(HelpBlockKind.Step, step, null, number)); continue; }
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line.Trim());
        }
        FlushSection();
        return new HelpChapter(title, sections, related);
    }

    private static bool TryCallout(string line, out HelpBlock block)
    {
        block = null!;
        (string Prefix, HelpBlockKind Kind)[] kinds = { ("Tip: ", HelpBlockKind.Tip), ("Note: ", HelpBlockKind.Note), ("Warning: ", HelpBlockKind.Warning) };
        foreach (var (prefix, kind) in kinds)
            if (line.StartsWith(prefix, StringComparison.Ordinal)) { block = new HelpBlock(kind, line[prefix.Length..].Trim()); return true; }
        return false;
    }

    private static bool TryStep(string line, out int number, out string text)
    {
        number = 0; text = "";
        var i = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i])) i++;
        if (i == 0 || i > 3 || i + 1 >= line.Length || line[i] != '.' || line[i + 1] != ' ') return false;
        number = int.Parse(line.AsSpan(0, i));
        text = line[(i + 2)..].Trim();
        return true;
    }

    /// <summary>The chapter titles in a Related line: known titles are matched longest first (so a title with commas stays whole), the rest split at commas.</summary>
    internal static IEnumerable<string> SplitRelated(string text, IReadOnlyList<string>? knownTitles)
    {
        var rest = text.Trim().TrimEnd('.');
        var found = new List<string>();
        while (rest.Length > 0)
        {
            string? match = null;
            if (knownTitles is not null)
                foreach (var known in knownTitles)
                    if (rest.StartsWith(known, StringComparison.OrdinalIgnoreCase) && (match is null || known.Length > match.Length)
                        && (rest.Length == known.Length || rest[known.Length] == ',')) match = known;
            if (match is null)
            {
                var comma = rest.IndexOf(',');
                match = (comma < 0 ? rest : rest[..comma]).Trim();
            }
            if (match.Length > 0) found.Add(match);
            rest = rest.Length > match.Length ? rest[match.Length..].TrimStart().TrimStart(',').TrimStart() : "";
        }
        return found;
    }

    /// <summary>Splits text into plain, bold (**name**) and code (`key`) spans. An unclosed marker stays as plain text.</summary>
    public static IReadOnlyList<HelpSpan> Spans(string text)
    {
        var spans = new List<HelpSpan>();
        var plain = new StringBuilder();
        void Flush() { if (plain.Length > 0) { spans.Add(new HelpSpan(plain.ToString(), HelpSpanStyle.Plain)); plain.Clear(); } }
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '`' && text.IndexOf('`', i + 1) is var close and > 0)
            {
                Flush();
                spans.Add(new HelpSpan(text[(i + 1)..close], HelpSpanStyle.Code));
                i = close + 1;
            }
            else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*' && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var end and > 0)
            {
                Flush();
                spans.Add(new HelpSpan(text[(i + 2)..end], HelpSpanStyle.Bold));
                i = end + 2;
            }
            else plain.Append(text[i++]);
        }
        Flush();
        return spans;
    }

    /// <summary>The text with its markers removed (what a reader sees, and what search indexes).</summary>
    public static string Plain(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var span in Spans(text)) sb.Append(span.Text);
        return sb.ToString();
    }
}
