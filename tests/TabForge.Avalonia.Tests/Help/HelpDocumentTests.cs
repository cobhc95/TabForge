using System.Linq;
using TabForge.Features.Help;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Help chapter parser: title, sections, paragraphs joined across lines, bullets, numbered steps, the three callouts, Problem / Fix and Question / Answer pairs,
//   the Related line (titles with commas stay whole) and the inline spans (bold, code, an unclosed marker).
// Does not own: the search (HelpSearchTests), the embedded chapters (HelpContentTests), drawing (HelpWindowTests).
// Tests: this class.
public sealed class HelpDocumentTests
{
    private const string Sample = "# Title\n\nLead text\ncontinues here.\n\n## First\n\n- one **bold** item\n- two with `Ctrl+N`\n\n1. step a\n2. step b\n\nTip: a tip\nNote: a note\nWarning: take care\n\nProblem: it broke\nFix: mend it\n\nQuestion: why?\nAnswer: because.\n\n## Second\n\nBody.\n\nRelated: Settings, themes and hotkeys, Interface tour.\n";

    [Fact]
    public void ParsesSectionsAndBlockKinds()
    {
        var chapter = HelpDocument.Parse(Sample, new[] { "Interface tour", "Settings, themes and hotkeys" });
        Assert.Equal("Title", chapter.Title);
        Assert.Equal(new string?[] { null, "First", "Second" }, chapter.Sections.Select(s => s.Heading));
        Assert.Equal("Lead text continues here.", chapter.Sections[0].Blocks.Single().Text);
        Assert.Equal(new[]
        {
            HelpBlockKind.Bullet, HelpBlockKind.Bullet, HelpBlockKind.Step, HelpBlockKind.Step, HelpBlockKind.Tip, HelpBlockKind.Note, HelpBlockKind.Warning,
            HelpBlockKind.Problem, HelpBlockKind.Question,
        }, chapter.Sections[1].Blocks.Select(b => b.Kind));
        Assert.Equal(("it broke", "mend it"), (chapter.Sections[1].Blocks[7].Text, chapter.Sections[1].Blocks[7].Second));
        Assert.Equal(("why?", "because."), (chapter.Sections[1].Blocks[8].Text, chapter.Sections[1].Blocks[8].Second));
        Assert.Equal(2, chapter.Sections[1].Blocks[3].Number);
    }

    [Fact]
    public void TheRelatedLineKeepsTitlesWithCommasWhole()
    {
        var chapter = HelpDocument.Parse(Sample, new[] { "Interface tour", "Settings, themes and hotkeys" });
        Assert.Equal(new[] { "Settings, themes and hotkeys", "Interface tour" }, chapter.Related);
        Assert.Equal(new[] { "A", "B" }, HelpDocument.Parse("# T\n\nRelated: A, B\n").Related);
    }

    [Fact]
    public void SpansSplitBoldCodeAndPlain()
    {
        var spans = HelpDocument.Spans("Press `Ctrl+N` in **File** menu `x");
        Assert.Equal(new[] { HelpSpanStyle.Plain, HelpSpanStyle.Code, HelpSpanStyle.Plain, HelpSpanStyle.Bold, HelpSpanStyle.Plain }, spans.Select(s => s.Style));
        Assert.Equal("Press Ctrl+N in File menu `x", HelpDocument.Plain("Press `Ctrl+N` in **File** menu `x"));
    }
}
