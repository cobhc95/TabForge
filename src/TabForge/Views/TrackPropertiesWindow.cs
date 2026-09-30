using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Track properties: an instrument picture, identity (name, performer, colour, notes), instrument and
/// MIDI sound, mixer (volume/pan knobs) and a visual string-tuning editor with presets.
/// Edits apply to the track only when OK is pressed.
/// </summary>
public static class TrackPropertiesWindow
{
    private sealed record TuningPreset(string Name, int[] HighToLow);

    private static readonly TuningPreset[] SixString =
    {
        new("Standard E", new[] { 64, 59, 55, 50, 45, 40 }),
        new("Eb standard (half step down)", new[] { 63, 58, 54, 49, 44, 39 }),
        new("D standard", new[] { 62, 57, 53, 48, 43, 38 }),
        new("C# standard", new[] { 61, 56, 52, 47, 42, 37 }),
        new("C standard", new[] { 60, 55, 51, 46, 41, 36 }),
        new("Drop D", new[] { 64, 59, 55, 50, 45, 38 }),
        new("Drop C#", new[] { 63, 58, 54, 49, 44, 37 }),
        new("Drop C", new[] { 62, 57, 53, 48, 43, 36 }),
        new("Drop B", new[] { 61, 56, 52, 47, 42, 35 }),
        new("Drop A", new[] { 59, 54, 50, 45, 40, 33 }),
        new("Open G (D G D G B D)", new[] { 62, 59, 55, 50, 43, 38 }),
        new("Open D (D A D F# A D)", new[] { 62, 57, 54, 50, 45, 38 }),
        new("DADGAD", new[] { 62, 57, 55, 50, 45, 38 }),
    };
    private static readonly TuningPreset[] SevenString =
    {
        new("Standard B", new[] { 64, 59, 55, 50, 45, 40, 35 }),
        new("Drop A", new[] { 64, 59, 55, 50, 45, 40, 33 }),
        new("A standard", new[] { 62, 57, 53, 48, 43, 38, 33 }),
    };
    private static readonly TuningPreset[] EightString =
    {
        new("Standard F#", new[] { 64, 59, 55, 50, 45, 40, 35, 30 }),
        new("Drop E", new[] { 64, 59, 55, 50, 45, 40, 35, 28 }),
    };
    private static readonly TuningPreset[] FourStringBass =
    {
        new("Standard E", new[] { 43, 38, 33, 28 }),
        new("Eb standard", new[] { 42, 37, 32, 27 }),
        new("D standard", new[] { 41, 36, 31, 26 }),
        new("Drop D", new[] { 43, 38, 33, 26 }),
        new("Drop C", new[] { 41, 36, 31, 24 }),
    };
    private static readonly TuningPreset[] FiveStringBass =
    {
        new("Standard B", new[] { 43, 38, 33, 28, 23 }),
        new("Standard E, high C", new[] { 48, 43, 38, 33, 28 }),
        new("Drop A", new[] { 43, 38, 33, 28, 21 }),
        new("A standard", new[] { 41, 36, 31, 26, 21 }),
    };
    private static readonly TuningPreset[] SixStringBass =
    {
        new("Standard B (high C)", new[] { 48, 43, 38, 33, 28, 23 }),
        new("Drop A", new[] { 48, 43, 38, 33, 28, 21 }),
    };
    private static readonly TuningPreset[] SevenStringBass =
    {
        new("Standard F# (high F)", new[] { 53, 48, 43, 38, 33, 28, 23 }),
    };
    private static readonly TuningPreset[] NineString =
    {
        new("Standard C#", new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25 }),
    };

    public static IEnumerable<(string Name, int[] HighToLow)> SixStringPresets => SixString.Select(p => (p.Name, p.HighToLow));

    // Presets depend on the instrument as well as the count: a 6-string bass is not a guitar.
    private static IEnumerable<TuningPreset> PresetsFor(int strings, bool bass) => (strings, bass) switch
    {
        (4, _) => FourStringBass,
        (5, _) => FiveStringBass,
        (6, true) => SixStringBass,
        (6, false) => SixString,
        (7, true) => SevenStringBass,
        (7, false) => SevenString,
        (8, _) => EightString,
        (9, _) => NineString,
        _ => Array.Empty<TuningPreset>()
    };

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    /// <summary>Parses "C4", "D#2", "Eb3", "f#" (octave optional: nearest to middle of the guitar range) to MIDI.</summary>
    public static bool TryParseNote(string text, out int midi)
    {
        midi = 0;
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^([A-Ga-g])([#b♯♭]?)(-?\d)?$");
        if (!m.Success) return false;
        var pc = char.ToUpperInvariant(m.Groups[1].Value[0]) switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11 };
        var acc = m.Groups[2].Value;
        if (acc is "#" or "♯") pc++;
        else if (acc is "b" or "♭") pc--;
        var octave = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 3;
        midi = (octave + 1) * 12 + pc;
        return midi is >= 12 and <= 108;
    }

    public static string NoteName(int midi) => $"{NoteNames[((midi % 12) + 12) % 12]}{midi / 12 - 1}";

    private static TrackKind KindOf(string instrument, TrackModel track)
    {
        instrument = InstrumentNaming.WithoutStringCount(instrument); // "(7 strings)" is not the Strings family
        if (track.MidiChannel == 9 || instrument.Contains("Drum", StringComparison.OrdinalIgnoreCase)) return TrackKind.Drums;
        if (instrument.Contains("Bass", StringComparison.OrdinalIgnoreCase)) return TrackKind.Bass;
        if (instrument.Contains("Piano", StringComparison.OrdinalIgnoreCase) || instrument.Contains("Organ", StringComparison.OrdinalIgnoreCase) ||
            instrument.Contains("Strings", StringComparison.OrdinalIgnoreCase)) return TrackKind.Keys;
        if (instrument.Contains("Guitar", StringComparison.OrdinalIgnoreCase)) return TrackKind.Guitar;
        return track.Kind;
    }

    /// <summary>Where "Add track" puts the new track; <see cref="InsertIndex"/> is set when the user confirms.</summary>
    public sealed class AddTrackPlacement
    {
        public AddTrackPlacement(int trackCount, int selectedIndex) { TrackCount = trackCount; SelectedIndex = selectedIndex; InsertIndex = trackCount; }
        public int TrackCount { get; }
        public int SelectedIndex { get; }
        public int InsertIndex { get; set; }
    }

    public static bool Show(Window owner, TrackModel track) => ShowCore(owner, track, null);

    /// <summary>
    /// The same window as Track properties, for a new (not yet added) track: pick the instrument from the
    /// catalogue, tuning, mixer and details, then where it goes in the track list. Returns false on Cancel.
    /// </summary>
    public static bool ShowAdd(Window owner, TrackModel track, AddTrackPlacement placement) => ShowCore(owner, track, placement);

    private static bool ShowCore(Window owner, TrackModel track, AddTrackPlacement? add)
    {
        var w = new Window
        {
            Title = add is null ? $"Track properties — {track.Name}" : "Add track",
            // Sized to its content so everything shows without scrolling; WindowPolish caps it to the
            // monitor work area on small screens / high DPI, where the content then scrolls.
            Width = 800, SizeToContent = SizeToContent.Height, MinWidth = 640, MinHeight = 420,
            Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        w.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Control.ForegroundProperty, "TextBrush");

        // ---------- working copies ----------
        var tunings = track.StringTunings.ToList();
        var instrument = new ComboBox();
        Border? tuningCard = null;
        var colour = ThemeService.TryParse(track.ColorHex, out var parsed) ? parsed : Colors.SteelBlue;

        TextBlock Caption(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        TextBlock Hint(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            return t;
        }
        Border Card(string title, UIElement content)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 2) });
            stack.Children.Add(content);
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1), Child = stack };
            card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
            return card;
        }

        // ---------- left: picture + identity ----------
        Action? refreshChooser = null;
        var picture = new Border { Height = 190, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 10), ClipToBounds = true };
        var pictureCaption = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -6, 0, 10) };

        var name = new TextBox { Text = track.Name };
        var defaultName = track.Name;
        var performer = new TextBox { Text = track.Performer };
        var notes = new TextBox { Text = track.TrackNotes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, VerticalContentAlignment = VerticalAlignment.Top };
        var swatches = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var colourHex = new TextBox { Text = track.ColorHex, Width = 86, Margin = new Thickness(6, 2, 0, 2) };
        void SetColour(Color c, bool updateText = true)
        {
            colour = c;
            if (updateText) colourHex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            RedrawPicture();
        }
        foreach (var hex in new[] { "#E5484D", "#F76B15", "#FFC53D", "#46A758", "#12A594", "#3E63DD", "#8E4EC6", "#D6409F", "#8B8D98", "#F0F0F0" })
        {
            ThemeService.TryParse(hex, out var c);
            var sw = new Button { Width = 20, Height = 20, Margin = new Thickness(0, 2, 5, 2), Background = new SolidColorBrush(c), BorderThickness = new Thickness(1), ToolTip = hex, Padding = new Thickness(0) };
            sw.Click += (_, _) => SetColour(c);
            swatches.Children.Add(sw);
        }
        swatches.Children.Add(colourHex);
        colourHex.LostFocus += (_, _) => { if (ThemeService.TryParse(colourHex.Text.Trim(), out var c)) SetColour(c, false); };

        var identity = new StackPanel();
        identity.Children.Add(Caption("Track name")); identity.Children.Add(name);
        identity.Children.Add(Caption("Played by")); identity.Children.Add(performer);
        identity.Children.Add(Hint("The musician who normally plays this part."));
        identity.Children.Add(Caption("Colour")); identity.Children.Add(swatches);
        var tint = new CheckBox { Content = "Tint the track's row and lane with its colour", IsChecked = track.TintRow, Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "The strength is set in Settings > Appearance > Track colour tint." };
        identity.Children.Add(tint);
        identity.Children.Add(Caption("Notes")); identity.Children.Add(notes);

        var left = new StackPanel { Width = 250, Margin = new Thickness(0, 0, 14, 0) };
        left.Children.Add(picture);
        left.Children.Add(pictureCaption);
        left.Children.Add(Card("Details", identity));

        // ---------- right: instrument / mixer / tuning ----------
        foreach (var entry in InstrumentCatalog.All) instrument.Items.Add(entry.Name);
        if (!instrument.Items.Contains(track.InstrumentName)) instrument.Items.Insert(0, track.InstrumentName);
        // The list itself is hidden: a button opens the searchable instrument catalogue instead.
        instrument.Visibility = Visibility.Collapsed;
        var chooseText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        var chooseIcon = new Image { Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
        var chooseContent = new DockPanel();
        var chooseArrow = new TextBlock { Text = "Change…", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.7 };
        DockPanel.SetDock(chooseIcon, Dock.Left); DockPanel.SetDock(chooseArrow, Dock.Right);
        chooseContent.Children.Add(chooseIcon); chooseContent.Children.Add(chooseArrow); chooseContent.Children.Add(chooseText);
        var chooseButton = new Button { Content = chooseContent, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5, 8, 5),
            ToolTip = "Open the instrument catalogue (search, pictures)" };
        void RefreshChooser()
        {
            var current = (string?)instrument.SelectedItem ?? track.InstrumentName;
            chooseText.Text = current;
            chooseIcon.Source = InstrumentIcon.Get(InstrumentCatalog.ForTrack(current, track.MidiProgram, track.MidiChannel == 9));
        }
        refreshChooser = RefreshChooser;
        chooseButton.Click += (_, _) =>
        {
            var picked = InstrumentPickerWindow.Show(w, (string?)instrument.SelectedItem ?? track.InstrumentName, colour);
            if (picked is null) return;
            if (!instrument.Items.Contains(picked)) instrument.Items.Add(picked);
            instrument.SelectedItem = picked;
        };
        instrument.SelectedItem = track.InstrumentName;
        var program = new TextBox { Text = track.MidiProgram.ToString(), Width = 60 };
        var channel = new TextBox { Text = track.MidiChannel.ToString(), Width = 60 };
        var soundRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        soundRow.Children.Add(new TextBlock { Text = "MIDI program", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        soundRow.Children.Add(program);
        soundRow.Children.Add(new TextBlock { Text = "Channel", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
        soundRow.Children.Add(channel);
        instrument.SelectionChanged += (_, _) =>
        {
            var entry = InstrumentCatalog.Find((string?)instrument.SelectedItem);
            if (entry is not null)
            {
                program.Text = entry.Program.ToString();
                // Drum kits live on the GM drum channel (10); leaving a kit returns to a melodic channel.
                if (entry.IsDrumKit) channel.Text = "9";
                else if (channel.Text.Trim() == "9") channel.Text = track.MidiChannel != 9 ? track.MidiChannel.ToString() : "0";
            }
            RedrawPicture();
        };
        var instrumentPanel = new StackPanel();
        instrumentPanel.Children.Add(chooseButton);
        instrumentPanel.Children.Add(instrument);
        instrumentPanel.Children.Add(soundRow);
        instrumentPanel.Children.Add(Hint("Channel 9 (the 10th) is the General MIDI drum channel."));

        KnobControl Knob(string label, double value, double def, double origin, Func<double, string> fmt) => new()
        {
            Minimum = 0, Maximum = 127, Value = value, DefaultValue = def, Origin = origin,
            Width = 44, Height = 44, Label = label, Format = fmt, HorizontalAlignment = HorizontalAlignment.Center
        };
        var volume = Knob("Volume", track.Volume, 100, 0, v => $"{Math.Round(v / 1.27)}%");
        var pan = Knob("Pan", track.Pan, 64, 64, v => (int)v - 64 is var o && o == 0 ? "Centre" : o < 0 ? $"L {-o}" : $"R {o}");
        StackPanel KnobBlock(KnobControl knob, string label)
        {
            var block = new StackPanel { Margin = new Thickness(0, 4, 26, 0) };
            var readout = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) };
            void Update() => readout.Text = knob.Format!(knob.Value);
            knob.ValueChanged += (_, _) => Update();
            Update();
            block.Children.Add(knob);
            block.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            block.Children.Add(readout);
            return block;
        }
        var mixer = new StackPanel { Orientation = Orientation.Horizontal };
        mixer.Children.Add(KnobBlock(volume, "Volume"));
        mixer.Children.Add(KnobBlock(pan, "Pan"));
        var mixerStack = new StackPanel();
        mixerStack.Children.Add(mixer);
        mixerStack.Children.Add(Hint("Drag or scroll a knob; double-click to reset."));
        var fxButton = new Button
        {
            Content = "Effects & instruments (FX)…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = add is null && owner is MainWindow,
            ToolTip = add is null ? "Open this track's FX chain window (plug-in instruments and effects)" : "Available once the track has been added"
        };
        fxButton.Click += (_, _) => (owner as MainWindow)?.OpenFxChain(track);
        mixerStack.Children.Add(fxButton);

        // Tuning editor
        var presetCombo = new ComboBox { MinWidth = 220 };
        var tuningRows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        // A plain label beside the box (like "Frets"): text placed inside this CheckBox rendered dark on dark.
        var keepFrets = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var keepFretsText = new TextBlock
        {
            Text = "Keep fret numbers (notes change pitch, like retuning the instrument)",
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        keepFretsText.MouseLeftButtonUp += (_, _) => keepFrets.IsChecked = keepFrets.IsChecked != true;
        var keepFretsRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(keepFrets, Dock.Left);
        keepFretsRow.Children.Add(keepFrets);
        keepFretsRow.Children.Add(keepFretsText);
        var frets = new TextBox { Text = track.NumberOfFrets.ToString(), Width = 50 };
        var capo = new TextBox { Text = track.Capo.ToString(), Width = 50 };
        var stringsLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var syncingPreset = false;

        bool IsBassNow() => KindOf((string?)instrument.SelectedItem ?? track.InstrumentName, track) == TrackKind.Bass;

        void RefreshPresets()
        {
            syncingPreset = true;
            presetCombo.Items.Clear();
            foreach (var p in PresetsFor(tunings.Count, IsBassNow())) presetCombo.Items.Add(p.Name);
            var match = PresetsFor(tunings.Count, IsBassNow()).FirstOrDefault(p => p.HighToLow.SequenceEqual(tunings));
            if (match is null) { presetCombo.Items.Insert(0, "Custom"); presetCombo.SelectedIndex = 0; }
            else presetCombo.SelectedItem = match.Name;
            syncingPreset = false;
        }

        void RebuildStrings()
        {
            tuningRows.Children.Clear();
            stringsLabel.Text = $"{tunings.Count} strings";
            for (var i = 0; i < tunings.Count; i++)
            {
                var index = i;
                var row = new Grid { Height = 30 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var number = new TextBlock { Text = (i + 1).ToString(), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
                // The string itself: thicker for lower pitches, like real wound strings.
                var thickness = Math.Clamp(1 + (70 - tunings[i]) / 9.0, 1, 5);
                var line = new Rectangle
                {
                    Height = thickness, VerticalAlignment = VerticalAlignment.Center, RadiusX = thickness / 2, RadiusY = thickness / 2,
                    Fill = new LinearGradientBrush(Color.FromRgb(0xE8, 0xE0, 0xC8), Color.FromRgb(0x9A, 0x8C, 0x6A), 90)
                };
                var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
                Button Step(string text, int delta, string tip)
                {
                    var b = new Button { Content = text, Width = 26, Height = 24, Padding = new Thickness(0), ToolTip = tip, Margin = new Thickness(2, 0, 2, 0) };
                    b.Click += (_, _) => { tunings[index] = Math.Clamp(tunings[index] + delta, 12, 100); RebuildStrings(); RefreshPresets(); };
                    return b;
                }
                var note = new Border
                {
                    Width = 52, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(2, 0, 2, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x40, colour.R, colour.G, colour.B)),
                    BorderBrush = new SolidColorBrush(colour), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = NoteName(tunings[i]), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
                    ToolTip = $"MIDI {tunings[i]} - double-click to type a note (e.g. D2, Eb3, F#4)",
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                // Hover glow in the track colour.
                note.MouseEnter += (_, _) => note.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = colour, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
                note.MouseLeave += (_, _) => note.Effect = null;
                // Double-click: type the note name. Enter accepts; Esc or clicking elsewhere cancels.
                note.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount != 2 || note.Child is TextBox) return;
                    e.Handled = true;
                    var label = note.Child;
                    var box = new TextBox { Text = NoteName(tunings[index]), BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                        Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = Brushes.White };
                    var finished = false;
                    void Finish(bool accept)
                    {
                        if (finished) return;
                        finished = true;
                        if (accept && TryParseNote(box.Text, out var midi))
                        {
                            tunings[index] = Math.Clamp(midi, 12, 100);
                            RebuildStrings(); RefreshPresets();
                            return;
                        }
                        note.Child = label;
                    }
                    box.KeyDown += (_, k) =>
                    {
                        if (k.Key == System.Windows.Input.Key.Enter) { Finish(true); k.Handled = true; }
                        else if (k.Key == System.Windows.Input.Key.Escape) { Finish(false); k.Handled = true; }
                    };
                    box.LostKeyboardFocus += (_, _) => Finish(false);
                    note.Child = box;
                    box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => { box.Focus(); box.SelectAll(); }));
                };
                controls.Children.Add(Step("−", -1, "Tune this string down a semitone"));
                controls.Children.Add(note);
                controls.Children.Add(Step("+", 1, "Tune this string up a semitone"));
                Grid.SetColumn(line, 1); Grid.SetColumn(controls, 2);
                row.Children.Add(number); row.Children.Add(line); row.Children.Add(controls);
                tuningRows.Children.Add(row);
            }
        }

        presetCombo.SelectionChanged += (_, _) =>
        {
            if (syncingPreset) return;
            var p = PresetsFor(tunings.Count, IsBassNow()).FirstOrDefault(x => x.Name == (string?)presetCombo.SelectedItem);
            if (p is null) return;
            tunings = p.HighToLow.ToList();
            RebuildStrings();
            RefreshPresets();
        };
        Button Wide(string text, string tip, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
            b.Click += (_, _) => act();
            return b;
        }
        var tuningTools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        tuningTools.Children.Add(Wide("All −1", "Tune every string down a semitone", () => { tunings = tunings.Select(t => Math.Clamp(t - 1, 12, 100)).ToList(); RebuildStrings(); RefreshPresets(); }));
        tuningTools.Children.Add(Wide("All +1", "Tune every string up a semitone", () => { tunings = tunings.Select(t => Math.Clamp(t + 1, 12, 100)).ToList(); RebuildStrings(); RefreshPresets(); }));
        tuningTools.Children.Add(Wide("+ Low string", "Add a string a fourth below the lowest", () => { if (tunings.Count < 9) { tunings.Add(Math.Max(12, tunings[^1] - 5)); RebuildStrings(); RefreshPresets(); } }));
        tuningTools.Children.Add(Wide("− Low string", "Remove the lowest string", () => { if (tunings.Count > 3) { tunings.RemoveAt(tunings.Count - 1); RebuildStrings(); RefreshPresets(); } }));

        var presetRow = new StackPanel { Orientation = Orientation.Horizontal };
        presetRow.Children.Add(stringsLabel);
        presetRow.Children.Add(presetCombo);
        var fretRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        fretRow.Children.Add(new TextBlock { Text = "Frets", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        fretRow.Children.Add(frets);
        fretRow.Children.Add(new TextBlock { Text = "Capo", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
        fretRow.Children.Add(capo);
        var tuningPanel = new StackPanel();
        tuningPanel.Children.Add(presetRow);
        tuningPanel.Children.Add(tuningRows);
        tuningPanel.Children.Add(tuningTools);
        tuningPanel.Children.Add(keepFretsRow);
        tuningPanel.Children.Add(fretRow);
        tuningCard = Card("Tuning", tuningPanel);

        var right = new StackPanel();
        right.Children.Add(Card("Instrument", instrumentPanel));

        // Drum tracks: how the kit is written (the reference numbers, GP6/7 abbreviations, drum-tab lines, or custom).
        var drumPreset = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var p in DrumMaps.Presets) drumPreset.Items.Add(new ComboBoxItem { Content = DrumMaps.DisplayName(p), Tag = p });
        void SelectPreset(string name) => drumPreset.SelectedItem = drumPreset.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == name);
        string CurrentPreset() => (drumPreset.SelectedItem as ComboBoxItem)?.Tag as string ?? DrumMaps.GuitarPro5;
        SelectPreset(string.IsNullOrEmpty(track.DrumMapPreset) ? DrumMaps.GuitarPro5 : track.DrumMapPreset);
        List<DrumMapEntry>? customMap = track.CustomDrumMap?.Select(e => e.Clone()).ToList();
        var editMap = new Button { Content = "Edit custom map…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        editMap.Click += (_, _) =>
        {
            var probe = new TrackModel { Kind = TrackKind.Drums, DrumMapPreset = CurrentPreset(), CustomDrumMap = customMap };
            var edited = DrumMapWindow.Show(w, probe);
            if (edited is null) return;
            customMap = edited;
            SelectPreset(DrumMaps.Custom);
        };
        var drumRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { drumPreset, editMap } };
        var drumPanel = new StackPanel { Children = { drumRow } };
        drumPanel.Children.Add(Hint("GP5 file drum map: GM numbers on the TAB. GP6/7 file drum map: kit abbreviations (BD, SD, HH…). Drum tab: one line per drum with x / o. The staff uses standard drum positions in every preset; the key map above the score shows the same labels."));
        var drumCard = Card("Drum notation", drumPanel);
        drumCard.Visibility = track.Kind == TrackKind.Drums || track.MidiChannel == 9 ? Visibility.Visible : Visibility.Collapsed;
        right.Children.Add(drumCard);
        right.Children.Add(Card("Mixer", mixerStack));
        right.Children.Add(tuningCard);

        var columns = new DockPanel { Margin = new Thickness(16, 16, 16, 0) };
        DockPanel.SetDock(left, Dock.Left);
        columns.Children.Add(left);
        columns.Children.Add(right);

        // ---------- picture ----------
        void RedrawPicture()
        {
            var kind = KindOf((string?)instrument.SelectedItem ?? track.InstrumentName, track);
            picture.Background = new LinearGradientBrush(Color.FromArgb(0x55, colour.R, colour.G, colour.B), Color.FromArgb(0x10, colour.R, colour.G, colour.B), 55);
            // The instrument badge, its circle in the track colour (falls back to the drawn art).
            var chosenName = (string?)instrument.SelectedItem ?? track.InstrumentName;
            var badge = InstrumentIcon.Get(InstrumentCatalog.ForTrack(chosenName, track.MidiProgram, track.MidiChannel == 9)); // family colour
            picture.Child = badge is not null
                ? new Image { Source = badge, Width = 160, Height = 160, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 12) }
                : InstrumentArt.Build(kind, colour, tunings.Count);
            pictureCaption.Text = chosenName;
            refreshChooser?.Invoke();
            if (tuningCard is not null) tuningCard.Visibility = kind == TrackKind.Drums ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------- footer ----------
        var error = new TextBlock { Foreground = Brushes.IndianRed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Content = add is null ? "OK" : "Add track", Width = add is null ? 92 : 110, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 92, IsCancel = true };
        var footer = new DockPanel { Margin = new Thickness(16, 10, 16, 14), LastChildFill = true };
        DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(ok, Dock.Right);
        footer.Children.Add(cancel); footer.Children.Add(ok);
        // Add mode: where the new track goes in the track list.
        ComboBox? position = null;
        TextBox? positionNumber = null;
        if (add is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            row.Children.Add(new TextBlock { Text = "Position", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            position = new ComboBox { Width = 230, VerticalAlignment = VerticalAlignment.Center };
            position.Items.Add($"Last (track {add.TrackCount + 1})");
            position.Items.Add("First (track 1)");
            if (add.SelectedIndex >= 0 && add.SelectedIndex < add.TrackCount)
                position.Items.Add($"After the selected track (track {add.SelectedIndex + 2})");
            position.Items.Add("As track number…");
            position.SelectedIndex = 0;
            positionNumber = new TextBox { Width = 46, Margin = new Thickness(6, 0, 0, 0), Text = (add.TrackCount + 1).ToString(), IsEnabled = false,
                VerticalContentAlignment = VerticalAlignment.Center, ToolTip = $"1 to {add.TrackCount + 1}" };
            position.SelectionChanged += (_, _) => positionNumber.IsEnabled = (string?)position.SelectedItem == "As track number…";
            row.Children.Add(position);
            row.Children.Add(positionNumber);
            DockPanel.SetDock(row, Dock.Left);
            footer.Children.Add(row);
        }
        footer.Children.Add(error);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = columns, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        w.Content = root;

        RebuildStrings();
        RefreshPresets();
        RedrawPicture();

        var accepted = false;
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(program.Text, out var prog) || prog is < 0 or > 127) { error.Text = "MIDI program must be 0–127."; return; }
            if (!int.TryParse(channel.Text, out var ch) || ch is < 0 or > 15) { error.Text = "MIDI channel must be 0–15."; return; }
            if (!int.TryParse(frets.Text, out var fr) || fr is < 12 or > 36) { error.Text = "Frets must be 12–36."; return; }
            if (!int.TryParse(capo.Text, out var cp) || cp is < 0 or > 12) { error.Text = "Capo must be 0–12."; return; }
            if (add is not null && position is not null && positionNumber is not null)
            {
                var choice = (string?)position.SelectedItem ?? "";
                if (choice.StartsWith("First", StringComparison.Ordinal)) add.InsertIndex = 0;
                else if (choice.StartsWith("After", StringComparison.Ordinal)) add.InsertIndex = add.SelectedIndex + 1;
                else if (choice.StartsWith("As track", StringComparison.Ordinal))
                {
                    if (!int.TryParse(positionNumber.Text.Trim(), out var n) || n < 1 || n > add.TrackCount + 1)
                    { error.Text = $"Track number must be 1–{add.TrackCount + 1}."; return; }
                    add.InsertIndex = n - 1;
                }
                else add.InsertIndex = add.TrackCount;
            }

            track.Name = string.IsNullOrWhiteSpace(name.Text) ? track.Name : name.Text.Trim();
            track.Performer = performer.Text.Trim();
            track.TrackNotes = notes.Text;
            track.ColorHex = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
            track.TintRow = tint.IsChecked == true;
            var selected = InstrumentNaming.WithoutStringCount((string?)instrument.SelectedItem ?? track.InstrumentName);
            var preset = TrackController.InstrumentPresets.FirstOrDefault(p => p.Name == selected);
            var picked = InstrumentCatalog.Find(selected);
            // Extended-range basses/guitars carry "(N strings)" in their name.
            track.InstrumentName = InstrumentNaming.ForStringCount(selected, KindOf(selected, track), tunings.Count);
            if (preset is not null) { track.Rig.Name = preset.Rig; track.Rig.ArticulationMap = preset.Map; }
            else if (picked is not null) { track.Rig.Name = picked.Name; track.Rig.ArticulationMap = picked.Map; }
            track.MidiProgram = prog;
            track.MidiChannel = ch;
            if (drumCard.Visibility == Visibility.Visible && (drumPreset.SelectedItem as ComboBoxItem)?.Tag is string chosenDrumPreset)
            {
                track.CustomDrumMap = customMap;
                DrumMaps.Apply(track, chosenDrumPreset);
            }
            track.Volume = (int)volume.Value;
            track.Pan = (int)pan.Value;
            track.NumberOfFrets = fr;
            track.Capo = cp;
            // Drum tracks have no tuning: their lines come from the drum notation preset instead.
            if (drumCard.Visibility != Visibility.Visible) ApplyTuning(track, tunings, keepFrets.IsChecked == true);
            // A new track takes the type of the instrument picked (drums, bass, keys or guitar).
            if (add is not null)
            {
                track.Kind = drumCard.Visibility == Visibility.Visible || ch == 9 ? TrackKind.Drums : KindOf(selected, track);
                // The placeholder name follows the instrument unless the user typed their own.
                if (name.Text.Trim() == defaultName) track.Name = selected;
            }
            accepted = true;
            w.DialogResult = true;
        };

        // Closing without OK (Cancel, Esc, the X) with edits made: confirm, listing what changed.
        List<string> PendingChanges()
        {
            var list = new List<string>();
            void Diff(string label, string before, string after)
            {
                if (!string.Equals(before, after, StringComparison.Ordinal)) list.Add($"{label}: {before} → {after}");
            }
            Diff("Track name", track.Name, name.Text.Trim());
            Diff("Played by", track.Performer, performer.Text.Trim());
            if (!string.Equals(track.TrackNotes, notes.Text, StringComparison.Ordinal)) list.Add("Notes edited");
            Diff("Colour", track.ColorHex.ToUpperInvariant(), $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}");
            Diff("Instrument", track.InstrumentName, (string?)instrument.SelectedItem ?? track.InstrumentName);
            Diff("MIDI program", track.MidiProgram.ToString(), program.Text.Trim());
            Diff("MIDI channel", track.MidiChannel.ToString(), channel.Text.Trim());
            Diff("Volume", track.Volume.ToString(), ((int)volume.Value).ToString());
            Diff("Pan", track.Pan.ToString(), ((int)pan.Value).ToString());
            Diff("Tuning", string.Join(" ", track.StringTunings.Select(NoteName)), string.Join(" ", tunings.Select(NoteName)));
            Diff("Frets", track.NumberOfFrets.ToString(), frets.Text.Trim());
            Diff("Capo", track.Capo.ToString(), capo.Text.Trim());
            return list;
        }
        w.Closing += (_, e) =>
        {
            if (accepted || add is not null) return; // cancelling Add track simply adds nothing
            var changes = PendingChanges();
            if (changes.Count == 0) return;
            if (!DiscardPrompt.Confirm(w, "Unsaved track changes", "Discard the changes made to this track?", changes,
                "Return to the track properties")) e.Cancel = true;

        };

        DialogHost.ShowModal(w);
        return accepted;
    }

    /// <summary>
    /// Sets new string tunings. With <paramref name="keepFrets"/> each note keeps its fret and moves in
    /// pitch by its string's change; otherwise pitches stay and frets are recomputed where possible.
    /// </summary>
    private static void ApplyTuning(TrackModel track, List<int> tunings, bool keepFrets)
    {
        var old = track.StringTunings;
        if (old.SequenceEqual(tunings)) return;
        foreach (var measure in track.Measures)
            foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                foreach (var note in cell.Notes)
                {
                    if (note.StringIndex < 0 || note.StringIndex >= tunings.Count) continue;
                    if (keepFrets)
                    {
                        var delta = note.StringIndex < old.Count ? tunings[note.StringIndex] - old[note.StringIndex] : 0;
                        note.MidiValue = Math.Clamp(note.MidiValue + delta, 0, 127);
                        if (note.SlideTargetMidi > 0) note.SlideTargetMidi = Math.Clamp(note.SlideTargetMidi + delta, 0, 127);
                        if (note.TrillTargetMidi > 0) note.TrillTargetMidi = Math.Clamp(note.TrillTargetMidi + delta, 0, 127);
                    }
                    else
                    {
                        var fret = note.MidiValue - tunings[note.StringIndex] - track.Capo;
                        if (fret >= 0 && fret <= track.NumberOfFrets) note.Fret = fret;
                    }
                }
        track.StringTunings = tunings.ToList();
    }
}

/// <summary>Vector instrument pictures for the track properties header, tinted with the track colour.</summary>
internal static class InstrumentArt
{
    public static UIElement Build(TrackKind kind, Color tint, int strings)
    {
        var canvas = new Canvas { Width = 250, Height = 190 };
        var wood = new LinearGradientBrush(Color.FromRgb(0x8A, 0x5A, 0x2B), Color.FromRgb(0x4E, 0x30, 0x16), 90);
        var body = new LinearGradientBrush(Lighten(tint, 0.25), Darken(tint, 0.35), 60);
        var metal = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE2));
        var edge = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0));
        void Add(Shape s, double x, double y) { Canvas.SetLeft(s, x); Canvas.SetTop(s, y); canvas.Children.Add(s); }

        switch (kind)
        {
            case TrackKind.Drums:
            {
                Add(new Ellipse { Width = 150, Height = 40, Fill = body, Stroke = edge, StrokeThickness = 2 }, 50, 80);
                Add(new Rectangle { Width = 150, Height = 50, Fill = body, Stroke = edge, StrokeThickness = 2 }, 50, 100);
                Add(new Ellipse { Width = 150, Height = 40, Fill = new SolidColorBrush(Color.FromRgb(0xEE, 0xEA, 0xE0)), Stroke = edge, StrokeThickness = 2 }, 50, 60 + 20);
                Add(new Ellipse { Width = 150, Height = 40, Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xEA)), Stroke = edge, StrokeThickness = 2 }, 50, 78);
                Add(new Ellipse { Width = 120, Height = 16, Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x4A)), Stroke = edge, StrokeThickness = 1.5 }, 10, 30);
                Add(new Ellipse { Width = 100, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x4A)), Stroke = edge, StrokeThickness = 1.5 }, 140, 22);
                Add(new Rectangle { Width = 3, Height = 120, Fill = metal }, 69, 38);
                Add(new Rectangle { Width = 3, Height = 130, Fill = metal }, 189, 30);
                Add(new Line { X1 = 0, Y1 = 0, X2 = 60, Y2 = 40, Stroke = wood, StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }, 100, 40);
                Add(new Line { X1 = 60, Y1 = 0, X2 = 0, Y2 = 40, Stroke = wood, StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }, 95, 42);
                break;
            }
            case TrackKind.Keys:
            {
                Add(new Rectangle { Width = 220, Height = 90, RadiusX = 8, RadiusY = 8, Fill = body, Stroke = edge, StrokeThickness = 2 }, 15, 55);
                for (var i = 0; i < 14; i++)
                    Add(new Rectangle { Width = 14, Height = 56, Fill = Brushes.White, Stroke = edge, StrokeThickness = 1 }, 26 + i * 14.3, 80);
                foreach (var i in new[] { 0, 1, 3, 4, 5, 7, 8, 10, 11, 12 })
                    Add(new Rectangle { Width = 9, Height = 34, Fill = Brushes.Black }, 36 + i * 14.3, 80);
                Add(new Rectangle { Width = 190, Height = 12, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)) }, 30, 62);
                break;
            }
            default:
            {
                var bass = kind == TrackKind.Bass;
                // Neck and headstock run diagonally across the card.
                var group = new Canvas { Width = 250, Height = 190, RenderTransform = new RotateTransform(-28, 125, 95) };
                void G(Shape s, double x, double y) { Canvas.SetLeft(s, x); Canvas.SetTop(s, y); group.Children.Add(s); }
                var neckLength = bass ? 150 : 130;
                G(new Rectangle { Width = neckLength, Height = 16, Fill = wood, Stroke = edge, StrokeThickness = 1 }, 118, 87);
                G(new Rectangle { Width = 34, Height = 26, RadiusX = 5, RadiusY = 5, Fill = wood, Stroke = edge, StrokeThickness = 1 }, 118 + neckLength - 4, 82);
                for (var f = 1; f < 9; f++) G(new Rectangle { Width = 1.5, Height = 16, Fill = metal }, 118 + f * (neckLength / 9.0), 87);
                var pegs = Math.Clamp(strings, 4, 8);
                for (var p = 0; p < pegs; p++)
                    G(new Ellipse { Width = 6, Height = 6, Fill = metal }, 122 + neckLength + (p % 2) * 12, 78 + (p / 2) * 10 + (p % 2) * 3);
                // Body: two overlapping bouts plus a waist.
                G(new Ellipse { Width = bass ? 92 : 100, Height = bass ? 78 : 88, Fill = body, Stroke = edge, StrokeThickness = 2 }, 20, 51);
                G(new Ellipse { Width = bass ? 70 : 76, Height = bass ? 64 : 70, Fill = body, Stroke = edge, StrokeThickness = 2 }, 84, 60);
                G(new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10)) }, 92, 84);
                G(new Rectangle { Width = 8, Height = 34, RadiusX = 2, RadiusY = 2, Fill = Brushes.Black }, 46, 78);
                for (var s = 0; s < Math.Min(pegs, 6); s++)
                    G(new Rectangle { Width = neckLength + 70, Height = 0.9, Fill = metal }, 50, 89 + s * (12.0 / Math.Max(1, Math.Min(pegs, 6) - 1)));
                canvas.Children.Add(group);
                break;
            }
        }
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    }

    private static Color Lighten(Color c, double t) => Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
    private static Color Darken(Color c, double t) => Color.FromRgb((byte)(c.R * (1 - t)), (byte)(c.G * (1 - t)), (byte)(c.B * (1 - t)));
}
