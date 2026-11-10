using System.Linq;
using TabForge.Features.Help;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Help search engine on small made-up chapters: stemming (play / playing / played), typo tolerance, prefix matching while typing, the intent map
//   (a symptom phrase finds the troubleshooting words), ranking boosts (a chapter title or heading beats a body mention, a Problem beats a paragraph),
//   quoted phrases, stop words, and the one-result-per-section rule.
// Does not own: the chapter parser (HelpDocumentTests), the embedded chapters and the real search (HelpContentTests), the window (HelpWindowTests).
// Tests: this class.
public sealed class HelpSearchTests
{
    private static HelpSearchIndex Small(string intents = "")
    {
        var chapters = new[]
        {
            HelpDocument.Parse("# Playing songs\n\n## Start playback\n\nPress Space to play the song. Playing stops at the end.\n\n## Metronome\n\nThe click keeps time while you practise.\n"),
            HelpDocument.Parse("# Troubleshooting\n\n## No sound\n\nProblem: I hear nothing from the speakers.\nFix: Choose the output device in the audio settings and check the mute button.\n\n## Slow window\n\nA very large song can make the window feel slow.\n"),
            HelpDocument.Parse("# Saving\n\n## Save a copy\n\nChoose Save as to keep the original. Exporting makes a MIDI file.\n"),
        };
        return HelpSearchIndex.Build(chapters, HelpIntentMap.Parse(intents));
    }

    [Theory]
    [InlineData("play", "playing"), InlineData("play", "played"), InlineData("play", "plays"), InlineData("save", "saving"), InlineData("save", "saved"),
     InlineData("delete", "deleting"), InlineData("export", "exported"), InlineData("note", "notes"), InlineData("tune", "tuning"), InlineData("tuning", "tunings")]
    public void WordFormsShareOneStem(string a, string b) => Assert.Equal(HelpText.Stem(a), HelpText.Stem(b));

    [Fact]
    public void StopWordsAreDroppedAndApostrophesJoin()
    {
        Assert.Equal(new[] { "cant", "hear", "sound" }, HelpText.Tokenize("Can’t hear the sound").Where(t => !HelpText.IsStopWord(t.Word)).Select(t => t.Word));
        Assert.Empty(HelpText.Stems("how do I"));
    }

    [Fact]
    public void WordFormsMatchInASearch()
    {
        var index = Small();
        Assert.Contains(index.Search("played"), h => h.ChapterTitle == "Playing songs");
        Assert.Contains(index.Search("exports"), h => h.ChapterTitle == "Saving");
    }

    [Fact]
    public void TypingAPrefixFindsTheWord()
    {
        var index = Small();
        Assert.Contains(index.Search("metro"), h => h.SectionHeading == "Metronome");
        Assert.Contains(index.Search("playi"), h => h.ChapterTitle == "Playing songs");
    }

    [Fact]
    public void ALongerWordToleratesOneOrTwoTypos()
    {
        var index = Small();
        Assert.Contains(index.Search("metronone "), h => h.SectionHeading == "Metronome");
        Assert.Contains(index.Search("speekers "), h => h.ChapterTitle == "Troubleshooting");
        Assert.Contains(index.Search("exprting "), h => h.ChapterTitle == "Saving");
        Assert.Empty(index.Search("qzxv "));
    }

    [Fact]
    public void ADistanceCountsASwapAsOneEdit()
    {
        Assert.Equal(1, HelpText.Distance("sound", "sonud", 2));
        Assert.Equal(3, HelpText.Distance("abcdef", "uvwxyz", 2));
    }

    [Fact]
    public void TheIntentMapAddsTheWordsTheChaptersUse()
    {
        const string intents = "no sound | cant hear anything | silent => audio device output mute !problem\n# a comment\nlag | freeze => slow performance !problem\n";
        var without = Small();
        var with = Small(intents);
        Assert.DoesNotContain(without.Search("silent "), h => h.ChapterTitle == "Troubleshooting");
        Assert.Contains(with.Search("silent "), h => h.ChapterTitle == "Troubleshooting");
        Assert.Contains(with.Search("cant hear anything "), h => h.ChapterTitle == "Troubleshooting" && h.Kind == HelpEntryKind.Problem);
        Assert.Contains(with.Search("freeze "), h => h.SectionHeading == "Slow window");
        Assert.Equal(5, HelpIntentMap.Parse(intents).Count);
    }

    [Fact]
    public void SymptomPhrasesExpandToTheWordsTheChaptersUse()
    {
        const string intents = "doesnt work => play stop !problem\nwont play => playback play transport stop !problem\n";
        var without = Small();
        var with = Small(intents);
        Assert.Empty(without.Search("doesnt work "));
        Assert.Contains(with.Search("doesnt work "), h => h.ChapterTitle == "Playing songs");
        Assert.Contains(with.Search("wont play "), h => h.ChapterTitle == "Playing songs");
    }

    [Fact]
    public void AnApostropheAndItsOpenFormMeetInAnIntentPhrase()
    {
        var map = HelpIntentMap.Parse("wont play => playback stop !problem\n");
        var expansion = map.Expand(HelpText.Stems("won't play"));
        Assert.True(expansion.Symptom);
        Assert.Contains("stop", expansion.Terms);
    }

    [Fact]
    public void ATitleOrHeadingOutranksABodyMention()
    {
        var chapters = new[]
        {
            HelpDocument.Parse("# Other\n\n## Notes\n\nThe loop button is described elsewhere in this chapter.\n"),
            HelpDocument.Parse("# Loop\n\n## Overview\n\nA short text.\n"),
            HelpDocument.Parse("# Third\n\n## Looping a part\n\nSome words here.\n"),
        };
        var hits = HelpSearchIndex.Build(chapters, new HelpIntentMap()).Search("loop ");
        Assert.Equal(HelpEntryKind.Chapter, hits[0].Kind);
        Assert.Equal("Loop", hits[0].ChapterTitle);
        Assert.Equal("Looping a part", hits[1].SectionHeading);
        Assert.Equal("Notes", hits[2].SectionHeading);
    }

    [Fact]
    public void AProblemLineOutranksAParagraphMention()
    {
        var chapter = HelpDocument.Parse("# T\n\n## A\n\nThe crackling noise is described in many forums.\n\n## B\n\nProblem: Crackling when I play.\nFix: Raise the buffer.\n");
        var hits = HelpSearchIndex.Build(new[] { chapter }, new HelpIntentMap()).Search("crackling ");
        Assert.Equal(HelpEntryKind.Problem, hits[0].Kind);
    }

    [Fact]
    public void AQuotedPhraseMustOccurAsWritten()
    {
        var chapters = new[]
        {
            HelpDocument.Parse("# A\n\n## One\n\nPress the output device button to see the list.\n\n## Two\n\nThe device for output is chosen in settings.\n"),
        };
        var index = HelpSearchIndex.Build(chapters, new HelpIntentMap());
        var plain = index.Search("output device ");
        var quoted = index.Search("\"output device\"");
        Assert.Equal(2, plain.Count);
        Assert.Single(quoted);
        Assert.Equal("One", quoted[0].SectionHeading);
    }

    [Fact]
    public void ResultsHoldOneHitPerSectionAndMarkTheMatchedWords()
    {
        var index = Small();
        var hits = index.Search("play ");
        Assert.Equal(hits.Count, hits.Select(h => (h.Chapter, h.Section)).Distinct().Count());
        var hit = hits.First(h => h.ChapterTitle == "Playing songs" && h.Kind == HelpEntryKind.Paragraph);
        Assert.NotEmpty(hit.SnippetMarks);
        foreach (var mark in hit.SnippetMarks) Assert.StartsWith("play", hit.Snippet.Substring(mark.Start, mark.Length), System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyAndStopWordOnlyQueriesFindNothing()
    {
        var index = Small();
        Assert.Empty(index.Search(""));
        Assert.Empty(index.Search("   "));
        Assert.Empty(index.Search("how to "));
    }
}
