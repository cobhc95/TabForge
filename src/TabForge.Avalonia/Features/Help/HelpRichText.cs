using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Features.Help;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Help;

// Owns: building the Help window's text controls: a wrapping text block from a chapter line (plain, bold UI names, key-cap chips for code spans) or from a
//   search snippet (matched words in the accent colour and bold), links, and the theme-key binding every Help control uses for its colours.
// Does not own: the layout of blocks (HelpReader, HelpResults), the colours (Palette keys, bound live so a theme or accent change repaints), the text parsing (Core HelpDocument).
// Tests: TabForge.Avalonia.Tests.HelpWindowTests.
internal static class HelpRichText
{
    /// <summary>Binds a colour property of <paramref name="target"/> to a palette key, so it follows the theme and the accent.</summary>
    internal static T Bind<T>(T target, AvaloniaProperty property, string key) where T : AvaloniaObject
    {
        target[!property] = new DynamicResourceExtension(key);
        return target;
    }

    /// <summary>A wrapping text block for a chapter line: bold names in semi-bold, code spans as key-cap chips.</summary>
    internal static TextBlock Line(string text, string foregroundKey, double size = ChromeMetrics.HelpBodySize, FontWeight weight = FontWeight.Normal, string? lead = null)
    {
        var block = Block(foregroundKey, size, weight);
        if (lead is not null) block.Inlines!.Add(new Run(lead) { FontWeight = FontWeight.SemiBold });
        foreach (var span in HelpDocument.Spans(text))
        {
            switch (span.Style)
            {
                case HelpSpanStyle.Code: block.Inlines!.Add(new InlineUIContainer(KeyCap(span.Text)) { BaselineAlignment = BaselineAlignment.Center }); break;
                case HelpSpanStyle.Bold: block.Inlines!.Add(new Run(span.Text) { FontWeight = FontWeight.SemiBold }); break;
                default: block.Inlines!.Add(new Run(span.Text)); break;
            }
        }
        return block;
    }

    /// <summary>A text block holding <paramref name="text"/> with the marked ranges in the accent colour and semi-bold.</summary>
    internal static TextBlock Marked(string text, IReadOnlyList<HelpMark> marks, string foregroundKey, double size, FontWeight weight = FontWeight.Normal)
    {
        var block = Block(foregroundKey, size, weight);
        var at = 0;
        foreach (var mark in marks)
        {
            if (mark.Start < at || mark.Start + mark.Length > text.Length) continue;
            if (mark.Start > at) block.Inlines!.Add(new Run(text[at..mark.Start]));
            var run = new Run(text.Substring(mark.Start, mark.Length)) { FontWeight = FontWeight.SemiBold };
            Bind(run, TextElement.ForegroundProperty, "Accent");
            block.Inlines!.Add(run);
            at = mark.Start + mark.Length;
        }
        if (at < text.Length) block.Inlines!.Add(new Run(text[at..]));
        return block;
    }

    private static TextBlock Block(string foregroundKey, double size, FontWeight weight)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = weight };
        Bind(block, TextBlock.ForegroundProperty, foregroundKey);
        return block;
    }

    /// <summary>A small key-cap chip: the text on a raised surface with a soft edge.</summary>
    internal static Border KeyCap(string text)
    {
        var label = new TextBlock { Text = text, FontSize = ChromeMetrics.HelpKeyCapSize, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Bind(label, TextBlock.ForegroundProperty, "TextStrong");
        var chip = new Border { Child = label, CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall) };
        DialogMetrics.Outlined(chip, ChromeMetrics.HelpDivider);
        DialogMetrics.Padded(chip, ChromeMetrics.HelpKeyCapPad, 0);
        DialogMetrics.Spaced(chip, ChromeMetrics.HelpRowGap, 0);
        Bind(chip, Border.BackgroundProperty, "RaisedBackground");
        Bind(chip, Border.BorderBrushProperty, "SoftBorder");
        return chip;
    }

    /// <summary>Text that acts as a link: accent coloured, underlined while the pointer is over it, and runs <paramref name="click"/> on a left click.</summary>
    internal static TextBlock Link(string text, double size, Action click, FontWeight weight = FontWeight.Normal)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
        Bind(block, TextBlock.ForegroundProperty, "Accent");
        block.PointerEntered += (_, _) => block.TextDecorations = TextDecorations.Underline;
        block.PointerExited += (_, _) => block.TextDecorations = null;
        block.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(block).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            click();
        };
        return block;
    }
}
