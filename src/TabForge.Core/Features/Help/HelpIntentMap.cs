using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TabForge.Features.Help;

// Owns: the Help search's intent map: the plain-text rules (HelpIntents.txt, one 'phrase | phrase => words !flag' line each) that turn everyday words and symptom
//   phrases into the words the chapters use ('no sound' also looks for audio, device, output, mute), and the loader for the embedded data file.
// Does not own: the scoring that uses the expansions (HelpSearchIndex), the text analysis (HelpText), the rules themselves (the data file, meant to be extended by hand).
// Tests: TabForge.Avalonia.Tests.HelpSearchTests.

/// <summary>What the intent map adds to a query: extra words to look for and whether the query reads as a symptom (so Problem entries rank higher).</summary>
public sealed record HelpExpansion(IReadOnlyList<string> Terms, bool Symptom)
{
    public static readonly HelpExpansion None = new(Array.Empty<string>(), false);
}

/// <summary>The parsed intent rules.</summary>
public sealed class HelpIntentMap
{
    private sealed record Rule(string[] Phrase, string[] Terms, bool Symptom);

    private readonly List<Rule> _rules = new();

    /// <summary>The number of trigger phrases loaded.</summary>
    public int Count => _rules.Count;

    /// <summary>Parses the rules. '#' starts a comment line; a line is 'phrase | phrase => word word !problem'. Lines without '=>' are skipped.</summary>
    public static HelpIntentMap Parse(string text)
    {
        var map = new HelpIntentMap();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var arrow = line.IndexOf("=>", StringComparison.Ordinal);
            if (arrow < 0) continue;
            var words = line[(arrow + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var symptom = words.Contains("!problem");
            var terms = words.Where(w => !w.StartsWith('!')).ToArray();
            foreach (var phrase in line[..arrow].Split('|'))
            {
                var stems = HelpText.Stems(phrase).ToArray();
                if (stems.Length > 0) map._rules.Add(new Rule(stems, terms, symptom));
            }
        }
        return map;
    }

    /// <summary>The map shipped inside the app (empty when the resource is missing).</summary>
    public static HelpIntentMap LoadEmbedded()
    {
        var assembly = typeof(HelpIntentMap).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".HelpIntents.txt", StringComparison.Ordinal));
        if (name is null) return new HelpIntentMap();
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>What the rules add for a query given as its stems in order: every rule whose trigger phrase occurs in the query adds its words.</summary>
    public HelpExpansion Expand(IReadOnlyList<string> queryStems)
    {
        List<string>? terms = null;
        var symptom = false;
        foreach (var rule in _rules)
        {
            if (!Contains(queryStems, rule.Phrase)) continue;
            terms ??= new List<string>();
            foreach (var term in rule.Terms) if (!terms.Contains(term)) terms.Add(term);
            symptom |= rule.Symptom;
        }
        return terms is null ? HelpExpansion.None : new HelpExpansion(terms, symptom);
    }

    private static bool Contains(IReadOnlyList<string> query, string[] phrase)
    {
        for (var start = 0; start + phrase.Length <= query.Count; start++)
        {
            var all = true;
            for (var k = 0; k < phrase.Length && all; k++) all = query[start + k] == phrase[k];
            if (all) return true;
        }
        return false;
    }
}
