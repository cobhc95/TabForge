using System;
using System.Collections.Generic;
using System.Linq;

namespace TabForge.Features.Help;

// Owns: the Help full-text index and its ranked search: one entry per chapter title, section heading and block (paragraph, bullet, step, callout, Problem, Question),
//   an inverted index of word stems with title-boosted weights, BM25 scoring, prefix matching for the word being typed, typo tolerance (edit distance 1-2 for longer words),
//   quoted phrases, the intent map's synonym expansion, one result per section (a Problem or Question is its own result), and the result snippets with the matched words marked.
// Does not own: the text analysis (HelpText), the intent rules (HelpIntentMap), the chapter model (HelpDocument), loading and drawing (Avalonia Features/Help).
// Tests: TabForge.Avalonia.Tests.HelpSearchTests.

/// <summary>What a search result is.</summary>
public enum HelpEntryKind { Chapter, Section, Paragraph, Bullet, Step, Tip, Note, Warning, Problem, Question }

/// <summary>A matched span of a result's title or snippet, as a start and a length.</summary>
public readonly record struct HelpMark(int Start, int Length);

/// <summary>One search result: where it is (chapter, section, block), its title line and snippet with the matched words marked, and its score.</summary>
public sealed record HelpHit(int Chapter, int Section, int Block, HelpEntryKind Kind, string ChapterTitle, string? SectionHeading,
    string Title, IReadOnlyList<HelpMark> TitleMarks, string Snippet, IReadOnlyList<HelpMark> SnippetMarks, double Score);

/// <summary>The searchable form of the whole Help. Immutable once built, so any thread may search it.</summary>
public sealed class HelpSearchIndex
{
    private const double K1 = 1.2, B = 0.6;
    private const double ExpansionWeight = 0.7, PrefixWeight = 0.8, FuzzyWeight = 0.5, PhraseBoost = 1.5, SymptomBoost = 1.25;
    private const double ChapterTitleBoost = 6, SectionBoost = 4, PairTitleBoost = 3, ContextBoost = 0.6;
    private const int SnippetLength = 170, SnippetLead = 45, MaxFuzzyTerms = 6;

    private readonly record struct Entry(short Chapter, short Section, short Block, HelpEntryKind Kind, string Title, string Body, string? Heading, int Length);
    private readonly record struct Posting(int Doc, float Weight);

    private readonly Entry[] _entries;
    private readonly string[] _chapterTitles;
    private readonly Dictionary<string, Posting[]> _postings;
    private readonly string[] _vocabulary;
    private readonly double _averageLength;
    private readonly HelpIntentMap _intents;

    /// <summary>How many distinct word stems the index holds.</summary>
    public int TermCount => _vocabulary.Length;

    private HelpSearchIndex(Entry[] entries, string[] chapterTitles, Dictionary<string, Posting[]> postings, double averageLength, HelpIntentMap intents)
    {
        _entries = entries;
        _chapterTitles = chapterTitles;
        _postings = postings;
        _vocabulary = postings.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        _averageLength = averageLength;
        _intents = intents;
    }

    /// <summary>Builds the index from the chapters in catalogue order (the chapter number in a result is the position here).</summary>
    public static HelpSearchIndex Build(IReadOnlyList<HelpChapter> chapters, HelpIntentMap intents)
    {
        var entries = new List<Entry>();
        var weights = new List<Dictionary<string, float>>();

        void Add(Entry entry, params (string Text, double Weight)[] fields)
        {
            var tf = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var (text, weight) in fields)
                foreach (var stem in HelpText.Stems(text)) tf[stem] = tf.GetValueOrDefault(stem) + (float)weight;
            entries.Add(entry with { Length = Math.Max(1, HelpText.Stems(entry.Title + " " + entry.Body).Count) });
            weights.Add(tf);
        }

        for (var c = 0; c < chapters.Count; c++)
        {
            var chapter = chapters[c];
            Add(new Entry((short)c, -1, -1, HelpEntryKind.Chapter, chapter.Title, "", null, 0), (chapter.Title, ChapterTitleBoost));
            for (var s = 0; s < chapter.Sections.Count; s++)
            {
                var section = chapter.Sections[s];
                var heading = section.Heading;
                if (heading is not null) Add(new Entry((short)c, (short)s, -1, HelpEntryKind.Section, heading, "", heading, 0), (heading, SectionBoost));
                for (var b = 0; b < section.Blocks.Count; b++)
                {
                    var block = section.Blocks[b];
                    var first = HelpDocument.Plain(block.Text);
                    var second = block.Second is null ? "" : HelpDocument.Plain(block.Second);
                    var context = heading ?? "";
                    if (block.Kind is HelpBlockKind.Problem or HelpBlockKind.Question)
                    {
                        var kind = block.Kind == HelpBlockKind.Problem ? HelpEntryKind.Problem : HelpEntryKind.Question;
                        Add(new Entry((short)c, (short)s, (short)b, kind, first, second, heading, 0), (first, PairTitleBoost), (second, 1), (context, ContextBoost));
                    }
                    else
                        Add(new Entry((short)c, (short)s, (short)b, ToEntryKind(block.Kind), context, first, heading, 0), (first, 1), (context, ContextBoost));
                }
            }
        }

        var building = new Dictionary<string, List<Posting>>(StringComparer.Ordinal);
        for (var d = 0; d < weights.Count; d++)
            foreach (var (term, weight) in weights[d])
            {
                if (!building.TryGetValue(term, out var list)) building[term] = list = new List<Posting>();
                list.Add(new Posting(d, weight));
            }
        var postings = building.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        var average = entries.Count == 0 ? 1 : entries.Average(e => (double)e.Length);
        return new HelpSearchIndex(entries.ToArray(), chapters.Select(c => c.Title).ToArray(), postings, average, intents);
    }

    private static HelpEntryKind ToEntryKind(HelpBlockKind kind) => kind switch
    {
        HelpBlockKind.Bullet => HelpEntryKind.Bullet,
        HelpBlockKind.Step => HelpEntryKind.Step,
        HelpBlockKind.Tip => HelpEntryKind.Tip,
        HelpBlockKind.Note => HelpEntryKind.Note,
        HelpBlockKind.Warning => HelpEntryKind.Warning,
        _ => HelpEntryKind.Paragraph,
    };

    private sealed class Group
    {
        public readonly List<(string Term, double Weight)> Matches = new();
        public bool Original = true;
    }

    /// <summary>Runs a query: ranked results, best first, at most <paramref name="max"/>. A quoted part must occur as written; the word being typed also matches as a prefix.</summary>
    public IReadOnlyList<HelpHit> Search(string query, int max = 40)
    {
        if (string.IsNullOrWhiteSpace(query) || _entries.Length == 0) return Array.Empty<HelpHit>();
        ParseQuery(query, out var free, out var phrases);
        var typing = !char.IsWhiteSpace(query[^1]) && query[^1] != '"' && query.Count(c => c == '"') % 2 == 0;

        // The words typed, as groups of vocabulary terms that count as the same word.
        var tokens = HelpText.Tokenize(free).Where(t => !HelpText.IsStopWord(t.Word)).ToList();
        var groups = new List<Group>();
        var marked = new HashSet<string>(StringComparer.Ordinal);
        var prefixes = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var beingTyped = typing && i == tokens.Count - 1;
            var group = BuildGroup(tokens[i].Word, beingTyped);
            groups.Add(group);
            foreach (var (term, _) in group.Matches) marked.Add(term);
            if (beingTyped) prefixes.Add(tokens[i].Word);
        }
        foreach (var phrase in phrases)
        {
            var phraseGroup = new Group();
            foreach (var stem in phrase) if (_postings.ContainsKey(stem)) { phraseGroup.Matches.Add((stem, PhraseBoost)); marked.Add(stem); }
            groups.Add(phraseGroup);
        }

        var allStems = HelpText.Stems(free).Concat(phrases.SelectMany(p => p)).ToList();
        var expansion = _intents.Expand(allStems);
        foreach (var word in expansion.Terms)
        {
            var stem = HelpText.Stem(word);
            if (!_postings.ContainsKey(stem) || allStems.Contains(stem)) continue;
            var group = new Group { Original = false };
            group.Matches.Add((stem, ExpansionWeight));
            groups.Add(group);
        }
        if (groups.Count(g => g.Original) == 0 || groups.All(g => g.Matches.Count == 0)) return Array.Empty<HelpHit>();

        // Score every entry that holds a word of any group; one group counts once per entry (its best match).
        var scores = new Dictionary<int, (double Score, int Covered)>();
        var wanted = groups.Count(g => g.Original);
        foreach (var group in groups)
        {
            var best = new Dictionary<int, double>();
            foreach (var (term, weight) in group.Matches)
            {
                var list = _postings[term];
                var idf = Math.Log(1 + (_entries.Length - list.Length + 0.5) / (list.Length + 0.5));
                foreach (var posting in list)
                {
                    var length = _entries[posting.Doc].Length;
                    var value = weight * idf * (posting.Weight * (K1 + 1)) / (posting.Weight + K1 * (1 - B + B * length / _averageLength));
                    if (value > best.GetValueOrDefault(posting.Doc)) best[posting.Doc] = value;
                }
            }
            foreach (var (doc, value) in best)
            {
                var (score, covered) = scores.GetValueOrDefault(doc);
                scores[doc] = (score + value, covered + (group.Original ? 1 : 0));
            }
        }

        var ranked = new List<(int Doc, double Score)>();
        foreach (var (doc, (score, covered)) in scores)
        {
            var entry = _entries[doc];
            if (phrases.Count > 0 && !phrases.All(p => ContainsPhrase(entry, p))) continue;
            var coverage = wanted == 0 ? 0 : Math.Min(1.0, (double)covered / wanted);
            var final = score * (0.35 + 0.65 * coverage);
            if (expansion.Symptom && entry.Kind is HelpEntryKind.Problem or HelpEntryKind.Question) final *= SymptomBoost;
            ranked.Add((doc, final));
        }
        ranked.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.Doc.CompareTo(b.Doc));

        // One result per section (a Problem or a Question stands alone), best first.
        var hits = new List<HelpHit>();
        var seen = new HashSet<(int, int)>();
        foreach (var (doc, score) in ranked)
        {
            var entry = _entries[doc];
            if (entry.Kind is not (HelpEntryKind.Problem or HelpEntryKind.Question) && entry.Kind != HelpEntryKind.Chapter && !seen.Add((entry.Chapter, entry.Section))) continue;
            hits.Add(MakeHit(entry, score, marked, prefixes));
            if (hits.Count >= max) break;
        }
        return hits;
    }

    private Group BuildGroup(string word, bool prefix)
    {
        var group = new Group();
        var stem = HelpText.Stem(word);
        var exact = _postings.ContainsKey(stem);
        if (exact) group.Matches.Add((stem, 1.0));
        if (prefix && word.Length >= 2)
        {
            foreach (var term in WithPrefix(word)) if (term != stem) group.Matches.Add((term, PrefixWeight));
            if (stem != word) foreach (var term in WithPrefix(stem)) if (term != stem && !group.Matches.Any(m => m.Term == term)) group.Matches.Add((term, PrefixWeight));
            // A longer form being typed ("playi" for "play"): a stem the typed text starts with, up to three letters short of it.
            foreach (var term in _vocabulary)
                if (term.Length >= 3 && word.Length > term.Length && word.Length <= term.Length + 3 && word.StartsWith(term, StringComparison.Ordinal) && !group.Matches.Any(m => m.Term == term))
                    group.Matches.Add((term, PrefixWeight));
        }
        if (group.Matches.Count == 0 && stem.Length >= 4)
        {
            var allowed = stem.Length >= 7 ? 2 : 1;
            var near = new List<(string Term, int Distance)>();
            foreach (var term in _vocabulary)
            {
                if (Math.Abs(term.Length - stem.Length) > allowed) continue;
                var distance = HelpText.Distance(stem, term, allowed);
                if (distance <= allowed) near.Add((term, distance));
            }
            foreach (var (term, distance) in near.OrderBy(n => n.Distance).ThenByDescending(n => _postings[n.Term].Length).Take(MaxFuzzyTerms))
                group.Matches.Add((term, FuzzyWeight / distance));
        }
        return group;
    }

    private IEnumerable<string> WithPrefix(string prefix)
    {
        var lo = 0; var hi = _vocabulary.Length;
        while (lo < hi) { var mid = (lo + hi) / 2; if (string.CompareOrdinal(_vocabulary[mid], prefix) < 0) lo = mid + 1; else hi = mid; }
        for (var i = lo; i < _vocabulary.Length && _vocabulary[i].StartsWith(prefix, StringComparison.Ordinal); i++) yield return _vocabulary[i];
    }

    // Quoted parts become phrases (as stem lists); the rest is the free text. An unclosed quote counts to the end.
    private static void ParseQuery(string query, out string free, out List<List<string>> phrases)
    {
        phrases = new List<List<string>>();
        var rest = new System.Text.StringBuilder();
        var parts = query.Split('"');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0) { rest.Append(parts[i]).Append(' '); continue; }
            var stems = HelpText.Stems(parts[i]);
            if (stems.Count > 0) phrases.Add(stems);
        }
        free = rest.ToString();
    }

    private static bool ContainsPhrase(Entry entry, List<string> phrase)
    {
        var stems = HelpText.Stems(entry.Title + " . " + entry.Body);
        for (var start = 0; start + phrase.Count <= stems.Count; start++)
        {
            var all = true;
            for (var k = 0; k < phrase.Count && all; k++) all = stems[start + k] == phrase[k];
            if (all) return true;
        }
        return false;
    }

    private HelpHit MakeHit(Entry entry, double score, HashSet<string> marked, List<string> prefixes)
    {
        var chapterTitle = _chapterTitles[entry.Chapter];
        string title, body;
        switch (entry.Kind)
        {
            case HelpEntryKind.Problem or HelpEntryKind.Question:
                title = entry.Title; body = (entry.Kind == HelpEntryKind.Problem ? "Fix: " : "") + entry.Body; break;
            case HelpEntryKind.Chapter: title = entry.Title; body = ""; break;
            case HelpEntryKind.Section: title = entry.Title; body = ""; break;
            default: title = entry.Heading ?? chapterTitle; body = entry.Body; break;
        }
        var titleMarks = Marks(title, marked, prefixes);
        var snippet = Snippet(body, marked, prefixes, out var snippetMarks);
        return new HelpHit(entry.Chapter, entry.Section, entry.Block, entry.Kind, chapterTitle, entry.Heading, title, titleMarks, snippet, snippetMarks, score);
    }

    private static List<HelpMark> Marks(string text, HashSet<string> marked, List<string> prefixes)
    {
        var marks = new List<HelpMark>();
        foreach (var token in HelpText.Tokenize(text))
        {
            if (HelpText.IsStopWord(token.Word)) continue;
            if (marked.Contains(HelpText.Stem(token.Word)) || prefixes.Any(p => token.Word.StartsWith(p, StringComparison.Ordinal))) marks.Add(new HelpMark(token.Start, token.Length));
        }
        return marks;
    }

    // A window of the text around its first match (the start when nothing matches), cut at word edges, with the marks moved into the window.
    private static string Snippet(string text, HashSet<string> marked, List<string> prefixes, out List<HelpMark> marks)
    {
        marks = new List<HelpMark>();
        if (text.Length == 0) return "";
        var all = Marks(text, marked, prefixes);
        var from = all.Count > 0 ? Math.Max(0, all[0].Start - SnippetLead) : 0;
        if (from > 0) { var space = text.IndexOf(' ', from); from = space < 0 || space > all[0].Start ? from : space + 1; }
        var length = Math.Min(SnippetLength, text.Length - from);
        var to = from + length;
        if (to < text.Length) { var space = text.LastIndexOf(' ', to - 1, Math.Min(to - 1 - from, 25)); if (space > from) to = space; }
        var prefix = from > 0 ? "…" : "";
        var suffix = to < text.Length ? "…" : "";
        var cut = text[from..to];
        foreach (var mark in all)
            if (mark.Start >= from && mark.Start + mark.Length <= to) marks.Add(new HelpMark(mark.Start - from + prefix.Length, mark.Length));
        return prefix + cut + suffix;
    }
}
