using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: the Composer panel beside the left strip: its title, the four clickable instrument-category cards (badge, name, coming-soon note),
//   the search box that filters the categories and styles, the collapsible style rows, and the results box with its honest empty text.
//   No pattern rows live here: every choice shows that pattern browsing is coming soon.
// Does not own: when the panel shows (LeftStripFeature), the saved open state (LeftStripSettings), the instrument badges (InstrumentBadgeView),
//   the catalogue entries (InstrumentCatalog), or the style-row control (InstrumentFamilyHeader).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests (the open-state rules only).
public static class ComposerPanel
{
    /// <summary>The panel's element name.</summary>
    public const string PanelName = "ComposerPanel";

    /// <summary>One clickable category card: its name, the catalogue program behind its badge, and its Palette accent key.</summary>
    private sealed record Category(string Name, int Program, bool Drums, string AccentKey);

    // The four category cards in grid order; the accents are Palette tokens with a dark and a light value.
    private static readonly Category[] Categories =
    {
        new("Drums", 0, true, "SectionColour3"),
        new("Bass", 33, false, "SectionColour5"),
        new("Guitar", 27, false, "SectionColour6"),
        new("Keyboard", 0, false, "SectionColour4"),
    };

    public static Border Build()
    {
        var styles = new[] { "Rock", "Pop", "Blues", "Metal", "Jazz", "Funk", "Latin" };
        string? chosenCategory = null, chosenStyle = null;
        var cards = new List<(Category Category, Border Card)>();
        var styleRows = new List<(string Style, InstrumentFamilyHeader Header, StackPanel Note)>();

        var results = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = ChromeMetrics.TextSmall };
        results[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");

        void ShowResults()
        {
            var name = chosenCategory ?? chosenStyle;
            results.Text = name is null
                ? "Pick a category or a style. Pattern browsing is coming soon."
                : name + " patterns are coming soon.";
        }

        void PaintCards()
        {
            foreach (var (category, card) in cards)
            {
                if (category.Name == chosenCategory)
                {
                    card[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoft");
                    card[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
                }
                else
                {
                    card.Background = AvaloniaPalette.TransparentBrush;
                    card[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
                }
            }
        }

        void ChooseCategory(Category category)
        {
            chosenCategory = chosenCategory == category.Name ? null : category.Name;
            chosenStyle = null;
            foreach (var (_, header, note) in styleRows)
            {
                if (header.IsOpen) header.Show(false, 0);
                note.IsVisible = false;
            }
            PaintCards();
            ShowResults();
        }

        var stack = new StackPanel { Spacing = ChromeMetrics.ComposerRowGap };
        stack.WithMargin(DialogMetrics.ComposerPanelInset);
        stack.Children.Add(Text("Composer", ChromeMetrics.TextHeading, FontWeight.Bold, "TextPrimary"));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < Categories.Length; i++)
        {
            var card = Card(Categories[i], ChooseCategory);
            cards.Add((Categories[i], card));
            Grid.SetRow(card, i / 2);
            Grid.SetColumn(card, i % 2);
            grid.Children.Add(card);
        }
        stack.Children.Add(grid);

        var search = new TextBox { Watermark = "Search patterns...", FontSize = ChromeMetrics.TextEmphasis };
        search.WithMargin(DialogMetrics.ComposerSearchGap);
        stack.Children.Add(search);

        var styleHost = new StackPanel { Spacing = ChromeMetrics.ComposerRowGap };
        foreach (var style in styles)
        {
            var captured = style;
            var note = new StackPanel();
            note.WithMargin(DialogMetrics.ComposerSearchGap);
            note.Children.Add(Text("Pattern browsing is coming soon.", ChromeMetrics.TextSmall, FontWeight.Normal, "TextMuted"));
            note.IsVisible = false;
            InstrumentFamilyHeader? header = null;
            header = new InstrumentFamilyHeader(captured, () =>
            {
                var open = !note.IsVisible;
                foreach (var (_, other, otherNote) in styleRows)
                {
                    other.Show(false, 0);
                    otherNote.IsVisible = false;
                }
                header!.Show(open, 0);
                note.IsVisible = open;
                chosenStyle = open ? captured : null;
                chosenCategory = null;
                PaintCards();
                ShowResults();
            });
            styleHost.Children.Add(header);
            styleHost.Children.Add(note);
            styleRows.Add((captured, header, note));
        }
        stack.Children.Add(styleHost);

        var resultsBox = new Border { Child = results, BorderThickness = DialogMetrics.ComposerOutline, CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium) };
        resultsBox.WithPadding(DialogMetrics.ComposerCardInset);
        resultsBox.WithMargin(DialogMetrics.ComposerResultsGap);
        resultsBox[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        stack.Children.Add(resultsBox);

        search.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            var query = search.Text?.Trim() ?? "";
            foreach (var (category, card) in cards)
                card.IsVisible = query.Length == 0 || category.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            foreach (var (style, header, note) in styleRows)
            {
                var match = query.Length == 0 || style.Contains(query, StringComparison.OrdinalIgnoreCase);
                header.IsVisible = match;
                if (!match) note.IsVisible = false;
            }
            if (chosenCategory is not null && !chosenCategory.Contains(query, StringComparison.OrdinalIgnoreCase)) chosenCategory = null;
            if (chosenStyle is not null && !chosenStyle.Contains(query, StringComparison.OrdinalIgnoreCase)) chosenStyle = null;
            PaintCards();
            ShowResults();
        };

        ShowResults();
        var panel = new Border
        {
            Name = PanelName, Width = ChromeMetrics.ComposerPanelWidth, BorderThickness = DialogMetrics.ComposerEdge,
            Child = new ScrollViewer { Content = stack },
        };
        panel[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        panel[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        return panel;
    }

    private static Border Card(Category category, Action<Category> choose)
    {
        var badge = new InstrumentBadgeView(ChromeMetrics.ComposerBadgeSize)
        {
            Entry = InstrumentCatalog.ForTrack(null, category.Program, category.Drums),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        badge.WithMargin(DialogMetrics.ComposerBadgeGap);
        var dot = new Border { Width = ChromeMetrics.ComposerAccentSize, Height = ChromeMetrics.ComposerAccentSize, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(ChromeMetrics.ComposerAccentSize / 2) };
        dot.WithMargin(DialogMetrics.ComposerDotGap);
        dot[!Border.BackgroundProperty] = new DynamicResourceExtension(category.AccentKey);
        var soon = Text("Coming soon", ChromeMetrics.TextSmall, FontWeight.Normal, "TextMuted");
        var note = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        note.Children.Add(dot);
        note.Children.Add(soon);
        var body = new StackPanel();
        body.Children.Add(badge);
        var name = Text(category.Name, ChromeMetrics.TextBody, FontWeight.SemiBold, "TextPrimary");
        name.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(name);
        body.Children.Add(note);
        var card = new Border
        {
            Child = body, MinHeight = ChromeMetrics.ComposerCardMinHeight, BorderThickness = DialogMetrics.ComposerOutline,
            CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium), Focusable = true,
        };
        card.WithPadding(DialogMetrics.ComposerCardInset);
        card.WithMargin(DialogMetrics.ComposerCardMargin);
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        card.Background = AvaloniaPalette.TransparentBrush;
        global::Avalonia.Automation.AutomationProperties.SetName(card, category.Name + " category");
        HoverShade.Wire(card, fill => card.Background = fill, () => AvaloniaPalette.TransparentBrush);
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            card.Focus();
            choose(category);
        };
        card.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            choose(category);
            e.Handled = true;
        };
        return card;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string colourKey)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(colourKey);
        return block;
    }
}
