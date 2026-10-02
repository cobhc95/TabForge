using System.Linq;
using System.Windows;
using System.Windows.Controls;

using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Small standard modal dialogs built in code (no extra XAML files).
public static class GpDialogs
{
    public static string? Prompt(string title, string label, string initial = "")
    {
        var w = new Window { Title = title, Width = 380, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = label });
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 8) };
        panel.Children.Add(box);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 75, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 75, IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; w.DialogResult = true; w.Close(); };
        row.Children.Add(ok); row.Children.Add(cancel);
        panel.Children.Add(row);
        w.Content = panel;
        w.Owner = Application.Current?.MainWindow is { IsLoaded: true } main && !ReferenceEquals(main, w) ? main : null;
        // Ready to type: the box has focus with its current value selected.
        w.Loaded += (_, _) => { box.Focus(); System.Windows.Input.Keyboard.Focus(box); box.SelectAll(); };
        return DialogHost.ShowModal(w) == true ? result : null;
    }

    public static string? PickTemplate(IReadOnlyList<string>? userTemplates = null)
    {
        var w = new Window
        {
            Title = "New from template", Width = 340, Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Choose a starting point" });
        var combo = new ComboBox { Margin = new Thickness(0, 8, 0, 10) };
        foreach (var name in TabForge.Services.UserTemplates.BuiltInNames) combo.Items.Add(name);
        foreach (var name in userTemplates ?? Array.Empty<string>()) combo.Items.Add(name);
        combo.SelectedIndex = 0;
        panel.Children.Add(combo);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Create", Width = 88, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 88, IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = combo.SelectedItem?.ToString() ?? "Blank"; w.DialogResult = true; w.Close(); };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        panel.Children.Add(row);
        w.Content = panel;
        return DialogHost.ShowModal(w) == true ? result : null;
    }

    /// <summary>The bar's time signature; <c>OnlyThisBar</c> limits the change to that bar (otherwise it lasts until the next change).</summary>
    /// <param name="selectedBars">"bars 3-6" when a bar range is selected: the change applies to exactly those bars.</param>
    public static (int num, int denom, bool onlyThisBar)? TimeSignature(int num, int denom, string? selectedBars = null)    {
        var w = new Window { Title = "Time signature", Width = 300, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var p = new StackPanel { Margin = new Thickness(10) };
        p.Children.Add(new TextBlock { Text = "Beats per bar / beat value" });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        var nBox = new TextBox { Text = num.ToString(), Width = 50 };
        var dBox = new ComboBox { Width = 70, SelectedIndex = 2 };
        foreach (var d in new[] { "1", "2", "4", "8", "16", "32" }) dBox.Items.Add(d);
        dBox.SelectedItem = denom.ToString();
        if (dBox.SelectedIndex < 0) dBox.SelectedIndex = 2;
        row.Children.Add(nBox); row.Children.Add(new TextBlock { Text = " / ", VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(dBox);
        p.Children.Add(row);
        p.Children.Add(new TextBlock { Text = selectedBars is null ? "Applies from this bar until the next time signature change." : $"Applies to the selected {selectedBars}.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        var onlyThisBar = new CheckBox { Content = "Only this bar", Margin = new Thickness(0, 0, 0, 8), Visibility = selectedBars is null ? Visibility.Visible : Visibility.Collapsed };
        p.Children.Add(onlyThisBar);
        (int, int, bool)? res = null;
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 75, IsDefault = true };
        var c = new Button { Content = "Cancel", Width = 75, IsCancel = true };
        ok.Click += (_, _) => { if (int.TryParse(nBox.Text, out var n) && int.TryParse(dBox.SelectedItem?.ToString(), out var d)) res = (Math.Clamp(n, 1, 32), d, onlyThisBar.IsChecked == true); w.DialogResult = true; w.Close(); };
        btns.Children.Add(ok); btns.Children.Add(c); p.Children.Add(btns);
        w.Content = p;
        return DialogHost.ShowModal(w) == true ? res : null;
    }

    /// <summary>The bar's key signature; <c>OnlyThisBar</c> limits the change to that bar (otherwise it lasts until the next change).</summary>
    /// <param name="selectedBars">"bars 3-6" when a bar range is selected: the change applies to exactly those bars.</param>
    public static (int signature, bool minor, bool onlyThisBar)? KeySignature(int current, bool currentMinor, string? selectedBars = null)
    {
        var names = new[] { "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#" };
        var w = new Window { Title = "Key signature", Width = 340, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Margin = new Thickness(10) };
        var cb = new ComboBox { Margin = new Thickness(0, 6, 0, 8) };
        foreach (var name in names) cb.Items.Add(name);
        cb.SelectedIndex = current + 7;
        var minor = new CheckBox { Content = "Minor mode", IsChecked = currentMinor, Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(new TextBlock { Text = "Key" }); p.Children.Add(cb);
        p.Children.Add(minor);
        p.Children.Add(new TextBlock { Text = selectedBars is null ? "Applies from this bar until the next key signature change." : $"Applies to the selected {selectedBars}.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        var onlyThisBar = new CheckBox { Content = "Only this bar", Margin = new Thickness(0, 0, 0, 8), Visibility = selectedBars is null ? Visibility.Visible : Visibility.Collapsed };
        p.Children.Add(onlyThisBar);
        (int, bool, bool)? res = null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 75, IsDefault = true };
        var cc = new Button { Content = "Cancel", Width = 75, IsCancel = true };
        ok.Click += (_, _) => { if (cb.SelectedIndex >= 0) res = (cb.SelectedIndex - 7, minor.IsChecked == true, onlyThisBar.IsChecked == true); w.DialogResult = true; w.Close(); };
        row.Children.Add(ok); row.Children.Add(cc); p.Children.Add(row);
        w.Content = p;
        return DialogHost.ShowModal(w) == true ? res : null;
    }

    public static string? Directions(string current, int ending, out int selectedEnding)
    {
        var names = new[] { "Segno", "Coda", "DoubleCoda", "Fine", "DaCapo", "DalSegno", "ToCoda", "DaCapoAlCoda", "DalSegnoAlCoda" };
        var existing = (current ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var known = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var unknown = existing.Where(token => !known.Contains(token)).ToArray();
        var w = new Window { Title = "Score directions", Width = 370, Height = 390, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Musical navigation marks and jumps", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var checks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, string>
        {
            ["Segno"] = "Segno destination", ["Coda"] = "Coda destination", ["DoubleCoda"] = "Double Coda destination",
            ["Fine"] = "Fine (stop after a navigation jump)", ["DaCapo"] = "Da Capo (jump to beginning)",
            ["DalSegno"] = "Dal Segno (jump to Segno)", ["ToCoda"] = "To Coda (jump to Coda)",
            ["DaCapoAlCoda"] = "Da Capo al Coda", ["DalSegnoAlCoda"] = "Dal Segno al Coda"
        };
        foreach (var name in names)
        {
            var check = new CheckBox { Content = labels[name], IsChecked = existing.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase)), Margin = new Thickness(0, 2, 0, 2) };
            checks.Add(name, check);
            panel.Children.Add(check);
        }
        panel.Children.Add(new TextBlock { Text = "Alternate ending (0–8)", Margin = new Thickness(0, 8, 0, 2) });
        var endingBox = new TextBox { Text = Math.Clamp(ending, 0, 8).ToString(), Width = 72, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(endingBox);
        var result = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Apply", Width = 75, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 75, IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
        string? selected = null;
        var resultEnding = Math.Clamp(ending, 0, 8);
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(endingBox.Text, out var value) || value is < 0 or > 8) return;
            resultEnding = value;
            selected = string.Join(',', unknown.Concat(checks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key)));
            w.DialogResult = true;
            w.Close();
        };
        result.Children.Add(ok); result.Children.Add(cancel); panel.Children.Add(result);
        w.Content = panel;
        var accepted = DialogHost.ShowModal(w) == true;
        selectedEnding = accepted ? resultEnding : ending;
        return accepted ? selected : null;
    }

    /// <summary>One entry of a colour drop-down: a name and its #RRGGBB value. ToString is the name (shown by UI Automation).</summary>
    internal sealed record ColourChoice(string Name, string Hex)
    {
        public override string ToString() => Name;

        public System.Windows.Media.Brush Swatch => Visualization.ColourText.TryParse(Hex, out var c) ? Frozen(c) : System.Windows.Media.Brushes.Transparent;

        private static System.Windows.Media.Brush Frozen(System.Windows.Media.Color c) { var b = new System.Windows.Media.SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>Small rounded swatch followed by the name; used for both the list items and the selection box.</summary>
        public static DataTemplate Template()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var box = new FrameworkElementFactory(typeof(Border));
            box.SetValue(FrameworkElement.WidthProperty, 14.0);
            box.SetValue(FrameworkElement.HeightProperty, 14.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            box.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Swatch)));
            box.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Name)));
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            row.AppendChild(box);
            row.AppendChild(text);
            return new DataTemplate { VisualTree = row };
        }
    }

    public static (string title, string color)? Marker(string title = "", string color = "#2E74B5", string action = "Add")
    {
        var w = new Window { Title = $"{action} score marker", Width = 340, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Marker name" });
        var nameBox = new TextBox { Text = title, Margin = new Thickness(0, 4, 0, 10) };
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "Colour" });
        var colors = new ColourChoice[]
        {
            new("Blue", "#2E74B5"), new("Green", "#3FB950"), new("Gold", "#D8A032"), new("Violet", "#8B5CF6"),
            new("Teal", "#00A6A6"), new("Rose", "#E06C75"), new("Slate", "#64748B"), new("Pink", "#C45A9A")
        };
        var combo = new ComboBox { Margin = new Thickness(0, 4, 0, 10), ItemTemplate = ColourChoice.Template(), SelectedValuePath = nameof(ColourChoice.Hex) };
        System.Windows.Automation.AutomationProperties.SetName(combo, "Colour");
        foreach (var item in colors) combo.Items.Add(item);
        var current = colors.FirstOrDefault(item => item.Hex.Equals(color, StringComparison.OrdinalIgnoreCase));
        if (current is null && Visualization.ColourText.TryParse(color, out _))
        {
            current = new ColourChoice($"Custom ({color.ToUpperInvariant()})", color);
            combo.Items.Add(current);
        }
        combo.SelectedItem = current ?? colors[0];
        panel.Children.Add(combo);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = action, Width = 75, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 75, IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
        (string, string)? result = null;
        ok.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(nameBox.Text) && combo.SelectedValue is string selectedColor)
                result = (nameBox.Text.Trim(), selectedColor);
            w.DialogResult = true;
            w.Close();
        };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        w.Content = panel;
        return DialogHost.ShowModal(w) == true ? result : null;
    }

    public static bool EditScoreInfo(Models.SongProject p)
    {
        static TextBox LongText(string? value) => new()
        {
            Text = value ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 44, MaxHeight = 140
        };
        var w = new Window { Title = "Score information", Width = 480, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var fields = new (string label, TextBox box)[]
        {
            ("Title", new TextBox { Text = p.Title }), ("Subtitle", new TextBox { Text = p.Subtitle }),
            ("Artist", new TextBox { Text = p.Artist }), ("Album", new TextBox { Text = p.Album }),
            ("Music author", new TextBox { Text = p.MusicAuthor }), ("Lyrics author", new TextBox { Text = p.LyricsAuthor }),
            ("Copyright", new TextBox { Text = p.Copyright }), ("Tab author", new TextBox { Text = p.TabAuthor }),
            // Multi-line texts (a notice may be 64K characters) scroll inside a bounded box instead of stretching the dialog.
            ("Instructions", LongText(p.Instructions)), ("Notice", LongText(p.Notice)),
        };
        var sp = new StackPanel { Margin = new Thickness(10) };
        foreach (var (l, b) in fields) { sp.Children.Add(new TextBlock { Text = l, FontWeight = FontWeights.Bold }); sp.Children.Add(b); }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
        var c = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var done = false;
        ok.Click += (_, _) =>
        {
            p.Title = fields[0].box.Text; p.Subtitle = fields[1].box.Text; p.Artist = fields[2].box.Text; p.Album = fields[3].box.Text;
            p.MusicAuthor = fields[4].box.Text; p.LyricsAuthor = fields[5].box.Text; p.Copyright = fields[6].box.Text; p.TabAuthor = fields[7].box.Text;
            p.Instructions = fields[8].box.Text; p.Notice = fields[9].box.Text; p.IsDirty = true;
            done = true; w.DialogResult = true; w.Close();
        };
        row.Children.Add(ok); row.Children.Add(c); sp.Children.Add(row);
        w.Content = new ScrollViewer { Content = sp };
        DialogHost.ShowModal(w);
        return done;
    }

    public static bool EditTrack(Models.TrackModel t)
    {
        var w = new Window { Title = "Track properties", Width = 420, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var name = new TextBox { Text = t.Name };
        var inst = new TextBox { Text = t.InstrumentName };
        var color = new TextBox { Text = t.ColorHex };
        var frets = new TextBox { Text = t.NumberOfFrets.ToString() };
        var capo = new TextBox { Text = t.Capo.ToString() };
        var tuning = new TextBox { Text = string.Join(" ", t.StringTunings.Select(MusicTheoryService.NoteName)) };
        var ch = new TextBox { Text = t.MidiChannel.ToString() };
        var prog = new TextBox { Text = t.MidiProgram.ToString() };
        var sp = new StackPanel { Margin = new Thickness(10) };
        void Add(string l, Control c) { sp.Children.Add(new TextBlock { Text = l, FontWeight = FontWeights.Bold }); sp.Children.Add(c); }
        Add("Name", name); Add("Instrument", inst); Add("Color (hex, e.g. #F61A16)", color);
        Add("Frets", frets); Add("Capo", capo); Add("Tuning high->low (MIDI numbers)", tuning);
        Add("MIDI channel 0-15 (9=drums)", ch); Add("MIDI program 0-127", prog);
        sp.Children.Add(new TextBlock { Text = "Tuning can also be typed as note names, e.g. E4 B3 G3 D3 A2 E2", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
        var cc = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var done = false;
        ok.Click += (_, _) =>
        {
            t.Name = name.Text; t.InstrumentName = inst.Text; t.ColorHex = color.Text;
            if (int.TryParse(frets.Text, out var f)) t.NumberOfFrets = Math.Clamp(f, 12, 36);
            if (int.TryParse(capo.Text, out var cp)) t.Capo = Math.Clamp(cp, 0, 12);
            var parsed = ParseTuning(tuning.Text);
            if (parsed.Count is >= 3 and <= 9) t.StringTunings = parsed;
            if (int.TryParse(ch.Text, out var cch)) t.MidiChannel = Math.Clamp(cch, 0, 15);
            if (int.TryParse(prog.Text, out var pr)) t.MidiProgram = Math.Clamp(pr, 0, 127);
            done = true; w.DialogResult = true; w.Close();
        };
        row.Children.Add(ok); row.Children.Add(cc); sp.Children.Add(row);
        w.Content = new ScrollViewer { Content = sp };
        DialogHost.ShowModal(w);
        return done;
    }

    private static List<int> ParseTuning(string s)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var m = 12; m < 100; m++) map[MusicTheoryService.NoteName(m)] = m;
        var out_ = new List<int>();
        foreach (var tok in s.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(tok, out var v)) out_.Add(Math.Clamp(v, 12, 96));
            else if (map.TryGetValue(tok.Trim(), out var mv)) out_.Add(mv);
        }
        return out_;
    }
}
