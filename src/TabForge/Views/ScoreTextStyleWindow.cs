using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Score text styling per area (fret numbers, techniques, chords, text/lyrics, bar info, header, other):
/// font, size, bold, italic, colour and an outline stroke with thickness. Empty values inherit the global
/// score text style. Changes preview live on the score; Cancel restores the previous styles.
/// </summary>
public static class ScoreTextStyleWindow
{
    private static readonly (string Key, string Label)[] Areas =
    {
        ("Fret", "Fret numbers (TAB)"), ("Technique", "Techniques (P.M., accents, …)"), ("Chord", "Chord names"),
        ("Lyrics", "Text & lyrics"), ("BarInfo", "Bar numbers, tempo, sections"), ("Header", "Title & header"),
        ("General", "Other score text"),
    };

    public static bool Show(Window owner, Dictionary<string, ScoreTextAreaStyle> styles, Action preview)
    {
        var w = new Window
        {
            Title = "Score text & fonts", Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, ShowInTaskbar = false,
        };
        var root = new DockPanel { Margin = new Thickness(14) };
        var list = new ListBox { Width = 190, Margin = new Thickness(0, 0, 12, 0) };
        foreach (var (_, label) in Areas) list.Items.Add(label);
        DockPanel.SetDock(list, Dock.Left);
        root.Children.Add(list);

        var form = new StackPanel();
        root.Children.Add(form);
        UIElement Row(string label, UIElement control)
        {
            var p = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            p.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(control);
            return p;
        }
        var font = new ComboBox { IsEditable = true };
        font.Items.Add("(inherit)");
        foreach (var family in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n)) font.Items.Add(family);
        var size = new Slider { Minimum = 50, Maximum = 250, TickFrequency = 5, IsSnapToTickEnabled = true, Width = 170 };
        var sizeText = new TextBlock { Width = 44, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { size, sizeText } };
        var bold = new CheckBox { Content = "Bold", IsThreeState = true, ToolTip = "Filled = inherit" };
        var italic = new CheckBox { Content = "Italic", IsThreeState = true, ToolTip = "Filled = inherit", Margin = new Thickness(12, 0, 0, 0) };
        var styleRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { bold, italic } };

        Button Swatch() => new() { Width = 36, Height = 22, BorderThickness = new Thickness(1) };
        var colourButton = Swatch();
        var colourClear = new Button { Content = "Inherit", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
        var colourRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { colourButton, colourClear } };
        var outlineButton = Swatch();
        var outline = new Slider { Minimum = 0, Maximum = 4, TickFrequency = 0.25, IsSnapToTickEnabled = true, Width = 130, Margin = new Thickness(8, 0, 0, 0) };
        var outlineText = new TextBlock { Width = 44, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var outlineRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { outlineButton, outline, outlineText } };
        var reset = new Button { Content = "Reset this area", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 8, 0, 0) };

        form.Children.Add(Row("Font", font));
        form.Children.Add(Row("Size", sizeRow));
        form.Children.Add(Row("Style", styleRow));
        form.Children.Add(Row("Colour", colourRow));
        form.Children.Add(Row("Outline", outlineRow));
        form.Children.Add(reset);

        var syncing = false;
        ScoreTextAreaStyle Current()
        {
            var key = Areas[Math.Max(0, list.SelectedIndex)].Key;
            if (!styles.TryGetValue(key, out var s) || s is null) styles[key] = s = new ScoreTextAreaStyle();
            return s;
        }
        void Paint(Button b, string? hex, string fallback)
        {
            b.Background = new SolidColorBrush(ColourChooser.TryParse(hex, out var c) ? c : ColourChooser.TryParse(fallback, out var f) ? f : Colors.Gray);
            b.Content = hex is null ? "—" : null;
        }
        void Load()
        {
            syncing = true;
            var s = Current();
            font.Text = string.IsNullOrWhiteSpace(s.Font) ? "(inherit)" : s.Font;
            size.Value = s.SizePercent <= 0 ? 100 : s.SizePercent;
            sizeText.Text = $"{size.Value:0}%";
            bold.IsChecked = s.Bold; italic.IsChecked = s.Italic;
            Paint(colourButton, s.Colour, "#808080");
            Paint(outlineButton, s.OutlineColour ?? "#000000", "#000000");
            outline.Value = s.OutlineThickness;
            outlineText.Text = s.OutlineThickness <= 0 ? "off" : $"{s.OutlineThickness:0.##}px";
            syncing = false;
        }
        void Changed()
        {
            if (syncing) return;
            var s = Current();
            var f = font.Text?.Trim();
            s.Font = string.IsNullOrEmpty(f) || f == "(inherit)" ? null : f;
            s.SizePercent = size.Value;
            s.Bold = bold.IsChecked; s.Italic = italic.IsChecked;
            s.OutlineThickness = outline.Value;
            sizeText.Text = $"{size.Value:0}%";
            outlineText.Text = s.OutlineThickness <= 0 ? "off" : $"{s.OutlineThickness:0.##}px";
            preview();
        }
        list.SelectionChanged += (_, _) => Load();
        font.SelectionChanged += (_, _) => { if (!syncing && font.SelectedItem is string sel) { font.Text = sel; Changed(); } };
        font.LostFocus += (_, _) => Changed();
        size.ValueChanged += (_, _) => Changed();
        bold.Click += (_, _) => Changed();
        italic.Click += (_, _) => Changed();
        outline.ValueChanged += (_, _) => Changed();
        colourButton.Click += (_, _) =>
        {
            var s = Current();
            var initial = ColourChooser.TryParse(s.Colour, out var c) ? c : Colors.White;
            var chosen = ColourChooser.Show(w, "Text colour", initial);
            if (chosen is null) return;
            s.Colour = ColourChooser.Hex(chosen.Value);
            Load(); preview();
        };
        colourClear.Click += (_, _) => { Current().Colour = null; Load(); preview(); };
        outlineButton.Click += (_, _) =>
        {
            var s = Current();
            var initial = ColourChooser.TryParse(s.OutlineColour, out var c) ? c : Colors.Black;
            var chosen = ColourChooser.Show(w, "Outline colour", initial);
            if (chosen is null) return;
            s.OutlineColour = ColourChooser.Hex(chosen.Value);
            if (s.OutlineThickness <= 0) s.OutlineThickness = 1;
            Load(); preview();
        };
        reset.Click += (_, _) => { styles.Remove(Areas[Math.Max(0, list.SelectedIndex)].Key); Load(); preview(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        form.Children.Add(buttons);

        w.Content = root;
        list.SelectedIndex = 0;
        return DialogHost.ShowModal(w) == true;
    }
}
