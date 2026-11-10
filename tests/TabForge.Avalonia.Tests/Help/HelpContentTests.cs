using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Features.Help;
using TabForge.Features.Help;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the embedded Help chapters as shipped: all fourteen exist, each title matches the catalogue, every chapter has sections and a Related line that resolves,
//   no chapter names other software, the old Keyboard shortcuts dialog or personal data, every key in code style is a real default hotkey, the catalogue holds
//   Help.Open on F1 and the Help menu has its row, and the real search finds the right chapters for everyday phrases (no sound, wrong string, crash, lag, capo).
// Does not own: the parser (HelpDocumentTests), the engine on small texts (HelpSearchTests), the window (HelpWindowTests).
// Tests: this class.
public sealed class HelpContentTests
{
    private static readonly HelpContent Content = new();
    private static readonly string[] Banned = { "guitar pro", "gp5", "tuxguitar", "musescore", "reaper", "ableton", "help > keyboard shortcuts", "c:\\", "@gmail", "@outlook" };

    [Fact]
    public void EveryChapterIsEmbeddedAndItsTitleMatchesTheCatalogue()
    {
        for (var i = 0; i < HelpChapterCatalog.All.Count; i++)
        {
            var chapter = Content.Chapter(i);
            Assert.Equal(HelpChapterCatalog.All[i].Title, chapter.Title);
            Assert.True(chapter.Sections.Count >= 3, HelpChapterCatalog.All[i].Key);
            Assert.NotEmpty(chapter.Related);
            Assert.All(chapter.Related, name => Assert.True(HelpChapterCatalog.IndexOfTitle(name) >= 0, HelpChapterCatalog.All[i].Key + ": " + name));
        }
    }

    [Fact]
    public void NoChapterNamesOtherSoftwareOrPersonalData()
    {
        for (var i = 0; i < HelpChapterCatalog.All.Count; i++)
        {
            var text = HelpContent.ReadText(i).ToLowerInvariant();
            foreach (var word in Banned) Assert.DoesNotContain(word, text);
        }
    }

    [Fact]
    public void EveryKeyInCodeStyleIsARealDefaultHotkey()
    {
        var gestures = HelpHotkeyNames.Defaults();
        var missing = new List<string>();
        for (var i = 0; i < HelpChapterCatalog.All.Count; i++)
            foreach (var section in Content.Chapter(i).Sections)
                foreach (var block in section.Blocks)
                    foreach (var span in HelpDocument.Spans(block.Text).Concat(HelpDocument.Spans(block.Second ?? "")))
                        if (span.Style == HelpSpanStyle.Code && HelpHotkeyNames.LooksLikeKey(span.Text) && !gestures.Contains(span.Text))
                            missing.Add(HelpChapterCatalog.All[i].Key + ": " + span.Text);
        Assert.True(missing.Count == 0, string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void HelpOpenIsCataloguedOnF1AndInTheHelpMenu()
    {
        var action = HotkeyCatalog.ById(HelpFeature.OpenId);
        Assert.NotNull(action);
        Assert.Equal("F1", action!.DefaultGesture);
        var helpRows = TabForge.Presenters.MainMenuTable.CommandRows(TabForge.Presenters.MainMenuTable.Groups.Where(g => g.Menu == "_Help"));
        Assert.Contains(HelpFeature.OpenId, helpRows.Select(r => r.CommandId));
    }

    [Fact]
    public async System.Threading.Tasks.Task RealSearchFindsTheRightChaptersForEverydayPhrases()
    {
        var index = await Content.Index();
        foreach (var query in new[] { "no sound", "cant hear anything", "silent" })
        {
            var hits = index.Search(query);
            Assert.True(hits.Take(8).Any(h => h.Chapter is 1 or HelpChapterCatalog.TroubleshootingIndex && h.Kind is HelpEntryKind.Problem or HelpEntryKind.Question or HelpEntryKind.Section), query);
        }
        Assert.Contains(index.Search("wrong string").Take(8), h => h.Chapter == 5);
        Assert.Contains(index.Search("crash").Take(8), h => h.Chapter == HelpChapterCatalog.TroubleshootingIndex);
        Assert.Contains(index.Search("lag").Take(8), h => h.Chapter == HelpChapterCatalog.TroubleshootingIndex);
        Assert.Contains(index.Search("wont play").Take(8), h => h.Chapter is 9 or HelpChapterCatalog.TroubleshootingIndex);
        Assert.Contains(index.Search("not working").Take(8), h => h.Chapter == HelpChapterCatalog.TroubleshootingIndex);
        Assert.NotEmpty(index.Search("capo"));
        Assert.True(index.TermCount > 500);
    }
}

// The keys a chapter may show in code style: the catalogue's default gestures spelled as a reader sees them (OemComma as a comma, and so on).
internal static class HelpHotkeyNames
{
    internal static HashSet<string> Defaults()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in HotkeyCatalog.All)
            foreach (var gesture in new[] { action.DefaultGesture, action.DefaultGesture2 })
                if (gesture.Length > 0) set.Add(Spell(gesture));
        // Keys the editor handles itself, not through the catalogue.
        foreach (var extra in new[] { "Left", "Right", "Up", "Down", "Ctrl+Left", "Ctrl+Right", "Ctrl+End", "Esc", "Enter", "Tab", "Shift+F10", "Shift+Space", "Home", "End", "Space", "Delete", "Insert" }) set.Add(extra);
        return set;
    }

    internal static bool LooksLikeKey(string text) =>
        text.StartsWith("Ctrl+", StringComparison.Ordinal) || text.StartsWith("Alt+", StringComparison.Ordinal) || text.StartsWith("Shift+", StringComparison.Ordinal)
        || (text.Length >= 2 && text[0] == 'F' && int.TryParse(text.AsSpan(1), out _));

    private static string Spell(string gesture) => gesture
        .Replace("OemComma", ",").Replace("OemPeriod", ".").Replace("OemPlus", "=").Replace("OemMinus", "-").Replace("Escape", "Esc").Replace("D0", "0").Replace("D1", "1");
}
