using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Features.Help;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Help;

// Owns: the Help reading pane: one chapter as a breadcrumb, a large title and its sections, built section by section (the first sections at once, the rest one
//   per idle turn of the UI thread, so a long chapter never builds one huge tree in a single go), each block drawn by its kind (paragraph, bullet with an accent dot,
//   numbered step with an accent badge, Tip / Note / Warning callouts, Problem / Fix cards, Question / Answer pairs), the Related line and the Previous / Next links,
//   and the scroll to a section.
// Does not own: the chapter text and parsing (HelpContent, Core HelpDocument), the text controls (HelpRichText), the chapter list and the window (HelpWindow).
// Tests: TabForge.Avalonia.Tests.HelpWindowTests.
internal sealed class HelpReader : ScrollViewer
{
    private const int FirstSections = 2;

    private readonly Action<int, int?> _open;
    private readonly StackPanel _page = DialogMetrics.Spaced(new StackPanel { MaxWidth = ChromeMetrics.HelpReadingWidth, HorizontalAlignment = HorizontalAlignment.Left }, ChromeMetrics.HelpPagePadding);
    private readonly HelpContent _content;
    private readonly List<Control> _sectionControls = new();
    private HelpChapter? _chapter;
    private int _index, _generation;

    // A ScrollViewer by look: the theme keys its template to that type.
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    internal HelpReader(HelpContent content, Action<int, int?> open)
    {
        _content = content;
        _open = open;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Content = _page;
    }

    /// <summary>The sections built so far (the rest follow on idle turns).</summary>
    internal int RealisedSections => _sectionControls.Count;

    /// <summary>True once every section and the links at the end are built.</summary>
    internal bool Complete { get; private set; }

    /// <summary>The chapter on show, or -1.</summary>
    internal int ChapterIndex => _chapter is null ? -1 : _index;

    /// <summary>Shows chapter <paramref name="index"/>; with a section index, scrolls to that section, else to the top.</summary>
    internal void Show(int index, int? section)
    {
        _generation++;
        _index = index;
        _chapter = _content.Chapter(index);
        Complete = false;
        _sectionControls.Clear();
        _page.Children.Clear();
        var info = HelpChapterCatalog.All[index];
        _page.Children.Add(HelpRichText.Line("Help  ›  " + info.Group + "  ›  " + info.Title, "TextMuted", ChromeMetrics.TextBody));
        var title = HelpRichText.Line(_chapter.Title, "TextStrong", ChromeMetrics.HelpTitleSize, FontWeight.SemiBold);
        DialogMetrics.Spaced(title, 0, ChromeMetrics.HelpBlockGap, 0, 0);
        _page.Children.Add(title);

        var count = _chapter.Sections.Count;
        var now = Math.Min(count, Math.Max(FirstSections, (section ?? -1) + 1));
        for (var i = 0; i < now; i++) AddSection(i);
        if (now >= count) Finish();
        else
        {
            var generation = _generation;
            Dispatcher.UIThread.Post(() => Continue(generation), DispatcherPriority.Background);
        }
        if (section is { } target && target >= 0 && target < _sectionControls.Count) ScrollTo(_sectionControls[target]);
        else Offset = default;
    }

    // One section per idle turn; a newer chapter makes the pending turn a no-op.
    private void Continue(int generation)
    {
        if (generation != _generation || _chapter is null) return;
        AddSection(_sectionControls.Count);
        if (_sectionControls.Count >= _chapter.Sections.Count) Finish();
        else Dispatcher.UIThread.Post(() => Continue(generation), DispatcherPriority.Background);
    }

    private void AddSection(int i)
    {
        var section = _chapter!.Sections[i];
        var panel = DialogMetrics.Spaced(new StackPanel(), 0, ChromeMetrics.HelpSectionGap, 0, 0);
        if (section.Heading is not null) panel.Children.Add(HelpRichText.Line(section.Heading, "Accent", ChromeMetrics.HelpSectionSize, FontWeight.SemiBold));
        HelpBlockKind? previous = null;
        foreach (var block in section.Blocks)
        {
            var control = Block(block);
            var list = block.Kind is HelpBlockKind.Bullet or HelpBlockKind.Step && previous == block.Kind;
            DialogMetrics.Spaced(control, 0, list ? ChromeMetrics.HelpRowGap : ChromeMetrics.HelpBlockGap, 0, 0);
            panel.Children.Add(control);
            previous = block.Kind;
        }
        _sectionControls.Add(panel);
        _page.Children.Add(panel);
    }

    private void Finish()
    {
        Complete = true;
        var chapter = _chapter!;
        if (chapter.Related.Count > 0)
        {
            var related = DialogMetrics.Spaced(new WrapPanel(), 0, ChromeMetrics.HelpSectionGap, 0, 0);
            related.Children.Add(HelpRichText.Line("Related:  ", "TextMuted", ChromeMetrics.HelpBodySize, FontWeight.SemiBold));
            for (var i = 0; i < chapter.Related.Count; i++)
            {
                var name = chapter.Related[i];
                var target = HelpChapterCatalog.IndexOfTitle(name);
                Control item = target >= 0 ? HelpRichText.Link(name, ChromeMetrics.HelpBodySize, () => _open(target, null)) : HelpRichText.Line(name, "TextPrimary");
                related.Children.Add(item);
                if (i < chapter.Related.Count - 1) related.Children.Add(HelpRichText.Line(",  ", "TextMuted"));
            }
            _page.Children.Add(related);
        }
        _page.Children.Add(Neighbours());
    }

    // Previous and Next chapter links at the bottom, each with its chapter's title.
    private Control Neighbours()
    {
        var grid = DialogMetrics.Spaced(new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") }, 0, ChromeMetrics.HelpSectionGap, 0, ChromeMetrics.HelpPagePadding);
        if (_index > 0) grid.Children.Add(Neighbour("‹  Previous", _index - 1, HorizontalAlignment.Left, 0));
        if (_index < HelpChapterCatalog.All.Count - 1) grid.Children.Add(Neighbour("Next  ›", _index + 1, HorizontalAlignment.Right, 1));
        return grid;
    }

    private Control Neighbour(string caption, int target, HorizontalAlignment side, int column)
    {
        var stack = new StackPanel { HorizontalAlignment = side };
        var small = HelpRichText.Line(caption, "TextMuted", ChromeMetrics.TextBody);
        small.HorizontalAlignment = side;
        stack.Children.Add(small);
        var link = HelpRichText.Link(HelpChapterCatalog.All[target].Title, ChromeMetrics.HelpBodySize, () => _open(target, null), FontWeight.SemiBold);
        link.HorizontalAlignment = side;
        stack.Children.Add(link);
        Grid.SetColumn(stack, column);
        return stack;
    }

    private void ScrollTo(Control section)
    {
        _page.UpdateLayout();
        var at = section.TranslatePoint(default, _page);
        Offset = new Vector(0, Math.Max(0, (at?.Y ?? 0) - ChromeMetrics.HelpBlockGap));
    }

    // ---- blocks ----

    private static Control Block(HelpBlock block) => block.Kind switch
    {
        HelpBlockKind.Bullet => Bullet(block.Text),
        HelpBlockKind.Step => Step(block.Number, block.Text),
        HelpBlockKind.Tip => Callout("Tip", block.Text, "Success"),
        HelpBlockKind.Note => Callout("Note", block.Text, "Accent"),
        HelpBlockKind.Warning => Callout("Warning", block.Text, "Warning"),
        HelpBlockKind.Problem => Problem(block.Text, block.Second ?? ""),
        HelpBlockKind.Question => Question(block.Text, block.Second ?? ""),
        _ => HelpRichText.Line(block.Text, "TextPrimary"),
    };

    private static Control Bullet(string text)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var dot = DialogMetrics.Spaced(new Ellipse { Width = ChromeMetrics.HelpBulletDot, Height = ChromeMetrics.HelpBulletDot, VerticalAlignment = VerticalAlignment.Top },
            ChromeMetrics.HelpRowGap, ChromeMetrics.HelpBulletTop, ChromeMetrics.HelpBlockGap, 0);
        HelpRichText.Bind(dot, Shape.FillProperty, "Accent");
        grid.Children.Add(dot);
        var line = HelpRichText.Line(text, "TextPrimary");
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        return grid;
    }

    private static Control Step(int number, string text)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var digits = new TextBlock { Text = number.ToString(), FontSize = ChromeMetrics.TextSmall, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        HelpRichText.Bind(digits, TextBlock.ForegroundProperty, "OnAccent");
        var badge = DialogMetrics.Spaced(new Border
        {
            Width = ChromeMetrics.HelpBadgeSize, Height = ChromeMetrics.HelpBadgeSize, CornerRadius = new CornerRadius(ChromeMetrics.HelpBadgeCorner),
            VerticalAlignment = VerticalAlignment.Top, Child = digits,
        }, 0, 0, ChromeMetrics.HelpBlockGap, 0);
        HelpRichText.Bind(badge, Border.BackgroundProperty, "Accent");
        grid.Children.Add(badge);
        var line = HelpRichText.Line(text, "TextPrimary");
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        return grid;
    }

    // A tinted box: the callout colour at a low opacity behind the text, with a solid bar at the left edge.
    private static Control Callout(string label, string text, string colourKey)
    {
        var tint = new Border { Opacity = ChromeMetrics.HelpCalloutTint, CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium) };
        HelpRichText.Bind(tint, Border.BackgroundProperty, colourKey);
        var bar = new Border { Width = ChromeMetrics.HelpCalloutBar, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium, 0, 0, ChromeMetrics.RadiusMedium) };
        HelpRichText.Bind(bar, Border.BackgroundProperty, colourKey);
        var content = DialogMetrics.Spaced(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") },
            ChromeMetrics.HelpCalloutBar + ChromeMetrics.HelpCardPadding, ChromeMetrics.HelpCalloutPadV, ChromeMetrics.HelpCardPadding, ChromeMetrics.HelpCalloutPadV);
        var title = HelpRichText.Line(label, colourKey, ChromeMetrics.HelpBodySize, FontWeight.Bold);
        DialogMetrics.Spaced(title, 0, 0, ChromeMetrics.HelpBlockGap, 0);
        content.Children.Add(title);
        var line = HelpRichText.Line(text, "TextPrimary");
        Grid.SetColumn(line, 1);
        content.Children.Add(line);
        var box = new Grid();
        box.Children.Add(tint);
        box.Children.Add(bar);
        box.Children.Add(content);
        return box;
    }

    // A Problem / Fix card on a raised surface: the problem in the danger colour, the fix in the success colour.
    private static Control Problem(string problem, string fix)
    {
        var stack = new StackPanel { Spacing = ChromeMetrics.HelpPairGap };
        stack.Children.Add(HelpRichText.Line(problem, "Danger", ChromeMetrics.HelpBodySize, FontWeight.Normal, "Problem: "));
        if (fix.Length > 0) stack.Children.Add(HelpRichText.Line(fix, "Success", ChromeMetrics.HelpBodySize, FontWeight.Normal, "Fix: "));
        var card = DialogMetrics.Padded(new Border { Child = stack, CornerRadius = new CornerRadius(ChromeMetrics.RadiusLarge) }, ChromeMetrics.HelpCardPadding);
        HelpRichText.Bind(card, Border.BackgroundProperty, "RaisedBackground");
        return card;
    }

    private static Control Question(string question, string answer)
    {
        var stack = new StackPanel { Spacing = ChromeMetrics.HelpPairGap };
        stack.Children.Add(HelpRichText.Line(question, "TextStrong", ChromeMetrics.HelpBodySize, FontWeight.SemiBold));
        if (answer.Length > 0)
        {
            var reply = HelpRichText.Line(answer, "TextPrimary");
            DialogMetrics.Spaced(reply, ChromeMetrics.HelpIndent, 0, 0, 0);
            stack.Children.Add(reply);
        }
        return stack;
    }
}
