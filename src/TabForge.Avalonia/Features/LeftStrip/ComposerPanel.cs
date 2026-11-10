using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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

// Owns: the Composer panel beside the left strip: its title, the four clickable instrument-category cards (badge, name, note), the search box that
//   filters the categories, genres and drum samples, the results box with its honest empty text, and the Drums
//   category's sample rows grouped under genre headers (grip, name, audition button, suggested tempo and length, the HH/SD/BD step pattern lit while auditioning) and when a
//   press on a row becomes a drag.
// Does not own: when the panel shows (LeftStripFeature), the saved open state (LeftStripSettings), the instrument badges (InstrumentBadgeView),
//   the catalogue entries (InstrumentCatalog, DrumSamples), or playing and dropping a sample
//   (ComposerDrumHooks, implemented by MainWindowComposerDrums).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests (the open-state rules), ComposerPanelTests (the sample rows), DrumSampleDropTests (the samples).
public static class ComposerPanel
{
    /// <summary>The panel's element name.</summary>
    public const string PanelName = "ComposerPanel";

    /// <summary>The element name prefix of a drum-sample row (followed by the sample id).</summary>
    public const string SampleRowPrefix = "ComposerSample_";

    /// <summary>The element name prefix of a drum-sample genre header (followed by the genre).</summary>
    public const string GenreHeaderPrefix = "ComposerGenre_";

    private const string PlayIcon = "M7,4 L19,12 L7,20 Z";
    private const string StopIcon = "M6,6 H18 V18 H6 Z";
    private const double DragThreshold = 4;

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

    /// <summary>One sample row's live parts: the play button and the step cells by voice row and step.</summary>
    private sealed class SampleRowParts
    {
        public required DrumSample Sample;
        public required Border Row;
        public required Button Play;
        public readonly List<(Border Cell, bool On, int Step)> Cells = new();
    }

    public static Border Build(ComposerDrumHooks? hooks = null)
    {
        hooks ??= new ComposerDrumHooks();
        string? chosenCategory = null;
        var cards = new List<(Category Category, Border Card)>();
        var sampleRows = new List<SampleRowParts>();
        SampleRowParts? playing = null;

        var results = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = ChromeMetrics.TextSmall };
        results[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var samplesHost = new StackPanel { Spacing = ChromeMetrics.ComposerRowGap, IsVisible = false };
        samplesHost.WithMargin(DialogMetrics.ComposerResultsGap);
        Border resultsBox = null!;

        // The window calls this with each step while a sample plays and with -1 when it stops for any reason (Stop, another sample, a drag).
        void Light(SampleRowParts parts, int step)
        {
            foreach (var (cell, on, cellStep) in parts.Cells)
                cell[!Border.BackgroundProperty] = new DynamicResourceExtension(StepBrushKey(on, cellStep == step));
            if (step >= 0) return;
            parts.Play.Content = MenuRowToggles.Icon(PlayIcon, ChromeMetrics.ComposerPlayIcon);
            ToolTip.SetTip(parts.Play, "Audition at the song tempo");
            if (ReferenceEquals(playing, parts)) playing = null;
        }

        void TogglePlay(SampleRowParts parts)
        {
            if (ReferenceEquals(playing, parts))
            {
                hooks.Stop();
                return;
            }
            hooks.Play(parts.Sample, step => Light(parts, step));
            playing = parts;
            parts.Play.Content = MenuRowToggles.Icon(StopIcon, ChromeMetrics.ComposerPlayIcon);
            ToolTip.SetTip(parts.Play, "Stop");
        }

        void ShowResults()
        {
            var showSamples = chosenCategory == "Drums";
            samplesHost.IsVisible = showSamples;
            resultsBox.IsVisible = !showSamples;
            if (!showSamples && playing is not null) hooks.Stop();
            if (showSamples) return;
            var name = chosenCategory;
            results.Text = name is null
                ? "Pick Drums for grooves you can audition and drag onto a track. Other patterns are coming soon."
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
            var category = Categories[i];
            var note = category.Drums ? $"{DrumSamples.All.Count} grooves" : "Coming soon";
            var card = Card(category, note, ChooseCategory);
            cards.Add((category, card));
            Grid.SetRow(card, i / 2);
            Grid.SetColumn(card, i % 2);
            grid.Children.Add(card);
        }
        stack.Children.Add(grid);

        var search = new TextBox { Watermark = "Search patterns...", FontSize = ChromeMetrics.TextEmphasis };
        search.WithMargin(DialogMetrics.ComposerSearchGap);
        stack.Children.Add(search);

        var genreHeaders = new List<(DrumGenre Genre, TextBlock Header)>();
        foreach (var genre in DrumSamples.GenresPresent)
        {
            var header = Text(DrumGenreNames.Of(genre), ChromeMetrics.TextSmall, FontWeight.SemiBold, "TextMuted");
            header.Name = GenreHeaderPrefix + genre;
            genreHeaders.Add((genre, header));
            samplesHost.Children.Add(header);
            foreach (var sample in DrumSamples.InGenre(genre))
            {
                var parts = SampleRow(sample, TogglePlay, hooks);
                sampleRows.Add(parts);
                samplesHost.Children.Add(parts.Row);
            }
        }
        stack.Children.Add(samplesHost);

        resultsBox = new Border { Child = results, BorderThickness = DialogMetrics.ComposerOutline, CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium) };
        resultsBox.WithPadding(DialogMetrics.ComposerCardInset);
        resultsBox.WithMargin(DialogMetrics.ComposerResultsGap);
        resultsBox[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        stack.Children.Add(resultsBox);

        search.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            var query = search.Text?.Trim() ?? "";
            bool Matches(string text) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool SampleMatches(SampleRowParts parts) => Matches(parts.Sample.Name) || Matches(DrumGenreNames.Of(parts.Sample.Genre));
            var anySample = sampleRows.Any(SampleMatches);
            foreach (var (category, card) in cards)
                card.IsVisible = Matches(category.Name) || (category.Drums && anySample);
            foreach (var parts in sampleRows)
                parts.Row.IsVisible = SampleMatches(parts) || Matches("Drums");
            foreach (var (genre, header) in genreHeaders)
                header.IsVisible = sampleRows.Any(parts => parts.Sample.Genre == genre && parts.Row.IsVisible);
            // A query naming a groove opens the Drums list; one that matches nothing in the chosen category clears the choice.
            if (query.Length > 0 && anySample && !Matches("Drums")) chosenCategory = "Drums";
            else if (chosenCategory is not null && !Matches(chosenCategory) && !(chosenCategory == "Drums" && anySample)) chosenCategory = null;
            PaintCards();
            ShowResults();
        };

        ShowResults();
        var panel = new Border
        {
            Name = PanelName, Width = ChromeMetrics.ComposerPanelWidth, BorderThickness = DialogMetrics.ComposerEdge,
            Child = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
        panel[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        panel[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        // A closed panel never keeps a groove playing.
        panel.DetachedFromVisualTree += (_, _) => { if (playing is not null) hooks.Stop(); };
        return panel;
    }

    /// <summary>The palette key of one step cell: a hit or an empty step, brighter on the step now playing.</summary>
    private static string StepBrushKey(bool on, bool current) => (on, current) switch
    {
        (true, true) => "TextPrimary",
        (true, false) => "AccentBrush",
        (false, true) => "AccentSoft",
        _ => "SoftBorder",
    };

    /// <summary>One drum-sample row, sized for the panel's width: grip, name and audition button on top, the suggested tempo and length under the name,
    /// and the HH/SD/BD step pattern across the full width with a wider gap between beats. Pressing the row and moving starts a drag.</summary>
    private static SampleRowParts SampleRow(DrumSample sample, Action<SampleRowParts> togglePlay, ComposerDrumHooks hooks)
    {
        var play = new Button
        {
            Content = MenuRowToggles.Icon(PlayIcon, ChromeMetrics.ComposerPlayIcon),
            Width = ChromeMetrics.ComposerPlaySize, Height = ChromeMetrics.ComposerPlaySize, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(play, "Audition at the song tempo");
        AutomationProperties.SetName(play, "Audition " + sample.Name);

        var name = Text(sample.Name, ChromeMetrics.TextBody, FontWeight.SemiBold, "TextPrimary");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var meta = Text($"{sample.SuggestedBpm} BPM · {sample.Bars} bar{(sample.Bars == 1 ? "" : "s")}", ChromeMetrics.TextSmall, FontWeight.Normal, "TextMuted");
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(name);
        words.Children.Add(meta);

        var grip = Grip();
        grip.WithMargin(DialogMetrics.ComposerGripGap);
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(words, 1);
        Grid.SetColumn(play, 2);
        header.Children.Add(grip);
        header.Children.Add(words);
        header.Children.Add(play);

        var body = new StackPanel();
        body.Children.Add(header);
        var row = new Border
        {
            Name = SampleRowPrefix + sample.Id, Child = body, BorderThickness = DialogMetrics.ComposerOutline,
            CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium), Cursor = new Cursor(StandardCursorType.Hand), Focusable = true,
        };
        var parts = new SampleRowParts { Sample = sample, Row = row, Play = play };
        var pattern = Pattern(sample, parts.Cells);
        pattern.WithMargin(DialogMetrics.ComposerPatternGap);
        body.Children.Add(pattern);

        row.WithPadding(DialogMetrics.ComposerCardInset);
        row[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        row.Background = AvaloniaPalette.TransparentBrush;
        ToolTip.SetTip(row, "Drag onto a track in the timeline");
        AutomationProperties.SetName(row, sample.Name + " drum groove");
        HoverShade.Wire(row, fill => row.Background = fill, () => AvaloniaPalette.TransparentBrush);
        play.Click += (_, e) =>
        {
            togglePlay(parts);
            e.Handled = true;
        };
        row.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            togglePlay(parts);
            e.Handled = true;
        };

        // A press only arms the drag; moving past the threshold with the button still down starts it, so a click never drags.
        Point? pressedAt = null;
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            pressedAt = e.GetPosition(row);
        };
        row.PointerMoved += (_, e) =>
        {
            if (pressedAt is not { } start) return;
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                pressedAt = null;
                return;
            }
            var now = e.GetPosition(row);
            if (Math.Abs(now.X - start.X) < DragThreshold && Math.Abs(now.Y - start.Y) < DragThreshold) return;
            pressedAt = null;
            hooks.Drag(sample, e);
        };
        row.PointerReleased += (_, _) => pressedAt = null;
        row.PointerCaptureLost += (_, _) => pressedAt = null;
        return parts;
    }

    /// <summary>The step pattern: a voice label then sixteen cells per row (hi-hat, snare, kick), stretched to the row's width.</summary>
    private static Grid Pattern(DrumSample sample, List<(Border Cell, bool On, int Step)> cells)
    {
        var grid = new Grid { RowSpacing = ChromeMetrics.ComposerStepGap * 2 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChromeMetrics.ComposerVoiceLabelWidth) });
        for (var step = 0; step < DrumSample.StepsPerBar; step++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var voices = new[] { ("HH", sample.HatSteps), ("SD", sample.SnareSteps), ("BD", sample.KickSteps) };
        for (var voice = 0; voice < voices.Length; voice++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Text(voices[voice].Item1, ChromeMetrics.TextMicro, FontWeight.Normal, "TextMuted");
            Grid.SetRow(label, voice);
            grid.Children.Add(label);
            for (var step = 0; step < DrumSample.StepsPerBar; step++)
            {
                var on = voices[voice].Item2.Contains(step);
                var left = step % 4 == 0 && step > 0 ? ChromeMetrics.ComposerBeatGap : ChromeMetrics.ComposerStepGap;
                var cell = new Border
                {
                    Height = ChromeMetrics.ComposerStepHeight, CornerRadius = new CornerRadius(ChromeMetrics.RadiusTiny),
                    Margin = new Thickness(left, 0, ChromeMetrics.ComposerStepGap, 0), VerticalAlignment = VerticalAlignment.Center,
                };
                cell[!Border.BackgroundProperty] = new DynamicResourceExtension(StepBrushKey(on, false));
                Grid.SetRow(cell, voice);
                Grid.SetColumn(cell, step + 1);
                grid.Children.Add(cell);
                cells.Add((cell, on, step));
            }
        }
        return grid;
    }

    /// <summary>The drag grip: two columns of three dots.</summary>
    private static Control Grip()
    {
        var dots = new Grid { VerticalAlignment = VerticalAlignment.Center, ColumnSpacing = 2, RowSpacing = 2 };
        for (var i = 0; i < 2; i++) dots.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 3; i++) dots.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 2; c++)
            {
                var dot = new Ellipse { Width = 3, Height = 3 };
                dot[!Shape.FillProperty] = new DynamicResourceExtension("TextMuted");
                Grid.SetRow(dot, r);
                Grid.SetColumn(dot, c);
                dots.Children.Add(dot);
            }
        return dots;
    }

    private static Border Card(Category category, string noteText, Action<Category> choose)
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
        var soon = Text(noteText, ChromeMetrics.TextSmall, FontWeight.Normal, "TextMuted");
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
        AutomationProperties.SetName(card, category.Name + " category");
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

/// <summary>The window's side of the Composer drum samples, implemented by MainWindowComposerDrums; the panel only calls it.</summary>
public sealed class ComposerDrumHooks
{
    /// <summary>Plays <paramref name="sample"/> on a loop at the song's tempo (stopping any other); the callback gets each 16th step as it sounds,
    /// and -1 once when the sample stops for any reason (Stop, another sample, a drag).</summary>
    public Action<DrumSample, Action<int>> Play = (_, _) => { };

    /// <summary>Stops the sample playing, if any.</summary>
    public Action Stop = () => { };

    /// <summary>Starts dragging a sample from the pointer move that crossed the drag threshold; the window runs the drag and the drop.</summary>
    public Action<DrumSample, PointerEventArgs> Drag = (_, _) => { };
}
