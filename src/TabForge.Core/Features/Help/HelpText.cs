using System;
using System.Collections.Generic;

namespace TabForge.Features.Help;

// Owns: the text analysis the Help search shares between indexing and querying: splitting text into lower-case word tokens (with their positions),
//   the stop-word list, the simple stemmer (play, plays, played and playing share one stem) and the bounded edit distance used for typo tolerance.
// Does not own: the index and scoring (HelpSearchIndex), the synonym map (HelpIntentMap), the chapter text.
// Tests: TabForge.Avalonia.Tests.HelpSearchTests.
public static class HelpText
{
    /// <summary>A word in a text: where it starts, how long it is as written, and its lower-case text.</summary>
    public readonly record struct Token(int Start, int Length, string Word);

    public static bool IsStopWord(string word) => word is "a" or "an" or "and" or "are" or "as" or "at" or "be" or "by" or "can" or "do" or "does" or "for" or "from" or "how" or "i" or "if" or "in" or "into" or "is" or "it" or "its" or "me" or "my" or "of" or "on" or "or" or "so" or "that" or "the" or "then" or "this" or "to" or "was" or "what" or "when" or "where" or "which" or "with" or "you" or "your";

    /// <summary>Splits text into word tokens: letters and digits (an apostrophe inside a word stays out: "cant" and "can't" meet), lower-cased. Punctuation separates.</summary>
    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetterOrDigit(text[i])) { i++; continue; }
            var start = i;
            var word = new System.Text.StringBuilder();
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || ((text[i] == '\'' || text[i] == '’') && i + 1 < text.Length && char.IsLetter(text[i + 1]) && i > start)))
            {
                if (char.IsLetterOrDigit(text[i])) word.Append(char.ToLowerInvariant(text[i]));
                i++;
            }
            tokens.Add(new Token(start, i - start, word.ToString()));
        }
        return tokens;
    }

    /// <summary>The searchable stems of a text: stop words dropped, the rest stemmed, in order.</summary>
    public static List<string> Stems(string text)
    {
        var stems = new List<string>();
        foreach (var token in Tokenize(text))
            if (!IsStopWord(token.Word)) stems.Add(Stem(token.Word));
        return stems;
    }

    /// <summary>A light suffix stripper: plurals, -ed, -ing and a silent final e, so related word forms share a stem. Words of three letters or fewer stay as they are.</summary>
    public static string Stem(string word)
    {
        if (word.Length <= 3 || !char.IsLetter(word[0])) return word;
        var w = word;
        if (w.EndsWith("ies", StringComparison.Ordinal) && w.Length > 4) w = w[..^3] + "y";
        else if (w.EndsWith("sses", StringComparison.Ordinal)) w = w[..^2];
        else if (w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal) && !w.EndsWith("is", StringComparison.Ordinal)) w = w[..^1];
        if (w.EndsWith("ing", StringComparison.Ordinal) && w.Length > 5) w = Undouble(w[..^3]);
        else if (w.EndsWith("ed", StringComparison.Ordinal) && w.Length > 4) w = Undouble(w[..^2]);
        if (w.Length > 3 && w.EndsWith('e')) w = w[..^1];
        if (w.EndsWith("ly", StringComparison.Ordinal) && w.Length > 5) w = w[..^2];
        return w;
    }

    // "running" -> "runn" -> "run"; "pull" and "pass" keep their double letter.
    private static string Undouble(string stem) =>
        stem.Length > 3 && stem[^1] == stem[^2] && "lsz".IndexOf(stem[^1]) < 0 ? stem[..^1] : stem;

    /// <summary>The edit distance (insert, delete, replace, or swap two neighbours) between two words, or <c>max + 1</c> as soon as it is known to exceed <paramref name="max"/>.</summary>
    public static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var previous2 = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var best = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) value = Math.Min(value, previous2[j - 2] + 1);
                current[j] = value;
                if (value < best) best = value;
            }
            if (best > max) return max + 1;
            (previous2, previous, current) = (previous, current, previous2);
        }
        return previous[b.Length];
    }
}
