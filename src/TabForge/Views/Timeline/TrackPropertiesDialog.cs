using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// Owns: the Track properties and Add track dialog for a MIDI track: details, instrument, drum notation, mixer, tuning and footer,
//   with working copies that are applied to the track only on OK.
// Does not own: the audio-track variant (TrackPropertiesDialog.Audio.cs), the Add track placement and lanes (TrackPropertiesWindow)
//   and the song model.
// Tests: TestAddTrackLane, TestAudioTrackProperties.
/// <summary>
/// The Track properties / Add track window, built section by section: details (left), instrument, drum notation, mixer,
/// tuning (right) and the footer. Edits apply to the track only when OK is pressed.
/// </summary>
internal sealed partial class TrackPropertiesDialog
{
    private readonly Action<TrackModel>? _openFxChain;
    private readonly TrackModel _track;
    private readonly TrackPropertiesWindow.AddTrackPlacement? _add;
    private readonly Window _w;

    // Working copies.
    private List<int> _tunings;
    private readonly bool _trackHadNotes;
    private Color _colour;
    private bool _accepted;

    // Details.
    private Border _picture = null!;
    private TextBlock _pictureCaption = null!;
    private TextBox _name = null!, _performer = null!, _notes = null!, _colourHex = null!;
    private CheckBox _tint = null!;
    private string _defaultName = "";

    // Instrument.
    private readonly ComboBox _instrument = new();
    private TextBlock _chooseText = null!;
    private Image _chooseIcon = null!;
    private TextBox _program = null!, _channel = null!;
    private TextBlock _channelHint = null!, _stringsStayHint = null!;
    private TrackKind _lastKind;

    // Mixer.
    private KnobControl _volume = null!, _pan = null!;

    // Tuning.
    private Border _tuningCard = null!;
    private ComboBox _presetCombo = null!;
    private StackPanel _tuningRows = null!;
    private CheckBox _keepFrets = null!;
    private TextBox _frets = null!, _capo = null!;
    private TextBlock _stringsLabel = null!;
    private bool _syncingPreset;

    // Drum notation.
    private ComboBox _drumPreset = null!;
    private List<DrumMapEntry>? _customMap;
    private Border _drumCard = null!;

    // Footer.
    private TextBlock _error = null!;
    private ComboBox? _position;
    private TextBox? _positionNumber;

    public TrackPropertiesDialog(Window owner, TrackModel track, TrackPropertiesWindow.AddTrackPlacement? add, Action<TrackModel>? openFxChain)
    {
        _openFxChain = openFxChain; _track = track; _add = add;
        _w = new Window
        {
            Title = add is null ? $"Track properties — {track.Name}" : "Add track",
            // Sized to its content so everything shows without scrolling; WindowPolish caps it to the
            // monitor work area on small screens / high DPI, where the content then scrolls.
            Width = 800, SizeToContent = SizeToContent.Height, MinWidth = 640, MinHeight = 420,
            Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        _w.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        _w.SetResourceReference(Control.ForegroundProperty, "TextBrush");

        _tunings = track.StringTunings.ToList();
        // Choosing an instrument of another kind (bass, keys, guitar) gives an EMPTY track that kind's default strings;
        // a track with notes keeps its strings (an instrument change never moves written notes).
        _trackHadNotes = TrackSetup.HasNotes(track);
        _colour = ThemeService.TryParse(track.ColorHex, out var parsed) ? parsed : Colors.SteelBlue;

        var left = BuildLeftColumn();
        var instrumentCard = BuildInstrumentCard();
        var mixerCard = BuildMixerCard();
        BuildTuningCard();
        var drumCard = BuildDrumCard();

        var right = BuildRightColumn(instrumentCard, drumCard, mixerCard);

        var columns = new DockPanel { Margin = new Thickness(16, 16, 16, 0) };
        DockPanel.SetDock(left, Dock.Left);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var footer = BuildFooter();
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = columns, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _w.Content = root;

        RebuildStrings();
        RefreshPresets();
        RedrawPicture();

        _w.Closing += OnClosing;
    }

    /// <summary>Shows the window modally; true when the user accepted.</summary>
    public bool Run()
    {
        DialogHost.ShowModal(_w);
        return _accepted;
    }

    // ---------- shared builders ----------

    private static TextBlock Caption(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };

    private static TextBlock Hint(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private static Border Card(string title, UIElement content)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = Services.ThemeService.MinFontSize, FontWeight = FontWeights.SemiBold, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(content);
        var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1), Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        return card;
    }

    private string CurrentInstrument => (string?)_instrument.SelectedItem ?? _track.InstrumentName;

    // The chosen instrument decides the kind (a kit is drums, anything else is pitched), not the track's old channel.
    private TrackKind CurrentKind => InstrumentCatalog.Find(CurrentInstrument) is { IsDrumKit: true } ? TrackKind.Drums
        : TrackSetup.KindOf(CurrentInstrument, -1, _track.Kind == TrackKind.Drums || _track.MidiChannel == 9 ? TrackSetup.PitchedKindFor(CurrentInstrument) : _track.Kind);

    /// <summary>The chosen instrument is in the other family than the track now (drums to pitched or back).</summary>
    private bool FamilyChanges => _add is null && !_track.IsAudio && TrackSetup.IsDrumFamily(_track) != (CurrentKind == TrackKind.Drums);

    // ---------- left: picture + identity ----------

    private StackPanel BuildLeftColumn()
    {
        _picture = new Border { Height = 190, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 10), ClipToBounds = true };
        _pictureCaption = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -6, 0, 10) };

        _name = new TextBox { Text = _track.Name };
        _defaultName = _track.Name;
        _performer = new TextBox { Text = _track.Performer };
        _notes = new TextBox { Text = _track.TrackNotes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, VerticalContentAlignment = VerticalAlignment.Top };
        var swatches = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        _colourHex = new TextBox { Text = _track.ColorHex, Width = 86, Margin = new Thickness(6, 2, 0, 2) };
        foreach (var hex in new[] { "#E5484D", "#F76B15", "#FFC53D", "#46A758", "#12A594", "#3E63DD", "#8E4EC6", "#D6409F", "#8B8D98", "#F0F0F0" })
        {
            ThemeService.TryParse(hex, out var c);
            var sw = new Button { Width = 20, Height = 20, Margin = new Thickness(0, 2, 5, 2), Background = new SolidColorBrush(c), BorderThickness = new Thickness(1), ToolTip = hex, Padding = new Thickness(0) };
            sw.Click += (_, _) => SetColour(c);
            swatches.Children.Add(sw);
        }
        swatches.Children.Add(_colourHex);
        _colourHex.LostFocus += (_, _) => { if (ThemeService.TryParse(_colourHex.Text.Trim(), out var c)) SetColour(c, false); };

        var identity = new StackPanel();
        identity.Children.Add(Caption("Track name")); identity.Children.Add(_name);
        if (!_track.IsAudio)   // an audio track has no "played by"
        {
            identity.Children.Add(Caption("Played by")); identity.Children.Add(_performer);
            identity.Children.Add(Hint("The musician who normally plays this part."));
        }
        identity.Children.Add(Caption("Colour")); identity.Children.Add(swatches);
        _tint = new CheckBox { Content = new TextBlock { Text = "Tint the track's row and lane with its colour", TextWrapping = TextWrapping.Wrap }, IsChecked = _track.TintRow, Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "The strength is set in Settings > Appearance > Track colour tint." };
        identity.Children.Add(_tint);
        identity.Children.Add(Caption("Notes")); identity.Children.Add(_notes);

        var left = new StackPanel { Width = 250, Margin = new Thickness(0, 0, 14, 0) };
        left.Children.Add(_picture);
        left.Children.Add(_pictureCaption);
        left.Children.Add(Card("Details", identity));
        return left;
    }

    private void SetColour(Color c, bool updateText = true)
    {
        _colour = c;
        if (updateText) _colourHex.Text = TabForge.Visualization.ColourText.Hex(c);
        RedrawPicture();
    }

    private void RedrawPicture()
    {
        var kind = CurrentKind;
        _picture.Background = new LinearGradientBrush(Color.FromArgb(0x55, _colour.R, _colour.G, _colour.B), Color.FromArgb(0x10, _colour.R, _colour.G, _colour.B), 55);
        if (_track.IsAudio) { DrawAudioPicture(); return; }
        // The instrument badge, its circle in the track colour (falls back to the drawn art).
        var chosenName = CurrentInstrument;
        var badge = InstrumentIcon.Get(InstrumentCatalog.ForTrack(chosenName, _track.MidiProgram, _track.MidiChannel == 9)); // family colour
        _picture.Child = badge is not null
            ? new Image { Source = badge, Width = 160, Height = 160, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 12) }
            : InstrumentArt.Build(kind, _colour, _tunings.Count);
        _pictureCaption.Text = chosenName;
        RefreshChooser();
        // Strings are for fretted instruments: drums and keyboards (piano, organ, pads / strings) have no tuning to show.
        if (_tuningCard is not null) _tuningCard.Visibility = kind is TrackKind.Drums or TrackKind.Keys ? Visibility.Collapsed : Visibility.Visible;
        UpdateChannelHint();
    }

    // ---------- right: instrument ----------

    private Border BuildInstrumentCard()
    {
        foreach (var entry in InstrumentCatalog.All) _instrument.Items.Add(entry.Name);
        if (!_instrument.Items.Contains(_track.InstrumentName)) _instrument.Items.Insert(0, _track.InstrumentName);
        // The list itself is hidden: a button opens the searchable instrument catalogue instead.
        _instrument.Visibility = Visibility.Collapsed;
        _chooseText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        _chooseIcon = new Image { Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
        var chooseContent = new DockPanel();
        var chooseArrow = new TextBlock { Text = "Change…", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.7 };
        DockPanel.SetDock(_chooseIcon, Dock.Left); DockPanel.SetDock(chooseArrow, Dock.Right);
        chooseContent.Children.Add(_chooseIcon); chooseContent.Children.Add(chooseArrow); chooseContent.Children.Add(_chooseText);
        var chooseButton = new Button { Content = chooseContent, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5, 8, 5),
            ToolTip = "Open the instrument catalogue (search, pictures)" };
        chooseButton.Click += (_, _) =>
        {
            var picked = InstrumentPickerWindow.Show(_w, CurrentInstrument, _colour);
            if (picked is null) return;
            if (!_instrument.Items.Contains(picked)) _instrument.Items.Add(picked);
            _instrument.SelectedItem = picked;
        };
        _instrument.SelectedItem = _track.InstrumentName;
        _program = new TextBox { Text = _track.MidiProgram.ToString(), Width = 60 };
        _channel = new TextBox { Text = _track.MidiChannel.ToString(), Width = 60 };
        var soundRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        soundRow.Children.Add(new TextBlock { Text = "MIDI program", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        soundRow.Children.Add(_program);
        soundRow.Children.Add(new TextBlock { Text = "Channel", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
        soundRow.Children.Add(_channel);
        _lastKind = CurrentKind;
        _instrument.SelectionChanged += (_, _) => OnInstrumentChanged();
        var panel = new StackPanel();
        panel.Children.Add(chooseButton);
        panel.Children.Add(_instrument);
        panel.Children.Add(soundRow);
        // The drum-channel explanation matters only for a drum track or while channel 9 is typed.
        _channelHint = Hint("Channel 9 (the 10th) is the General MIDI drum channel.");
        panel.Children.Add(_channelHint);
        _stringsStayHint = Hint("This track has notes, so its strings stay as they are. Add a new track to start with this instrument's own strings.");
        _stringsStayHint.Visibility = Visibility.Collapsed;
        panel.Children.Add(_stringsStayHint);
        _channel.TextChanged += (_, _) => UpdateChannelHint();
        return Card("Instrument", panel);
    }

    private void OnInstrumentChanged()
    {
        var entry = InstrumentCatalog.Find((string?)_instrument.SelectedItem);
        if (entry is not null)
        {
            _program.Text = entry.Program.ToString();
            // Drum kits live on the GM drum channel (10); leaving a kit returns to a melodic channel.
            if (entry.IsDrumKit) _channel.Text = "9";
            else if (_channel.Text.Trim() == "9") _channel.Text = _track.MidiChannel != 9 ? _track.MidiChannel.ToString() : "0";
        }
        var newKind = CurrentKind;
        if (newKind != _lastKind)
        {
            if ((!_trackHadNotes || FamilyChanges) && TrackSetup.DefaultStrings(newKind) is { } defaults) { _tunings = defaults; RebuildStrings(); RefreshPresets(); }
            _lastKind = newKind;
        }
        _drumCard.Visibility = newKind == TrackKind.Drums ? Visibility.Visible : Visibility.Collapsed;
        _stringsStayHint.Visibility = _trackHadNotes && !FamilyChanges && newKind != _track.Kind && newKind != TrackKind.Drums ? Visibility.Visible : Visibility.Collapsed;
        RedrawPicture();
    }

    private void RefreshChooser()
    {
        var current = CurrentInstrument;
        _chooseText.Text = current;
        _chooseIcon.Source = InstrumentIcon.Get(InstrumentCatalog.ForTrack(current, _track.MidiProgram, _track.MidiChannel == 9));
    }

    private void UpdateChannelHint()
    {
        if (_channelHint is null) return;
        var drums = CurrentKind == TrackKind.Drums;
        _channelHint.Visibility = drums || _channel.Text.Trim() == "9" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- right: mixer ----------

    private static KnobControl Knob(string label, double value, double def, double origin, Func<double, string> fmt) => new()
    {
        Minimum = 0, Maximum = 127, Value = value, DefaultValue = def, Origin = origin,
        Width = 44, Height = 44, Label = label, Format = fmt, HorizontalAlignment = HorizontalAlignment.Center
    };

    private static StackPanel KnobBlock(KnobControl knob, string label)
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

    private Border BuildMixerCard()
    {
        _volume = Knob("Volume", _track.Volume, 100, 0, v => $"{Math.Round(v / 1.27)}%");
        _volume.FromDisplay = percent => percent * 1.27;
        _pan = Knob("Pan", _track.Pan, 64, 64, v => (int)v - 64 is var o && o == 0 ? "Centre" : o < 0 ? $"L {-o}" : $"R {o}");
        _pan.Parse = KnobValueParser.ParsePan;
        var mixer = new StackPanel { Orientation = Orientation.Horizontal };
        mixer.Children.Add(KnobBlock(_volume, "Volume"));
        mixer.Children.Add(KnobBlock(_pan, "Pan"));
        var mixerStack = new StackPanel();
        mixerStack.Children.Add(mixer);
        mixerStack.Children.Add(Hint("Drag or scroll a knob; double-click to type a value; Ctrl+click to reset."));
        var fxButton = new Button
        {
            Content = "Effects & instruments (FX)…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = _add is null && _openFxChain is not null
        };
        TooltipShortcuts.Bind(fxButton, _add is null ? "Open this track's FX chain window (plug-in instruments and effects)" : "Available once the track has been added",
            _add is null ? "Track.FxChain" : null);
        fxButton.Click += (_, _) => _openFxChain?.Invoke(_track);
        mixerStack.Children.Add(fxButton);
        return Card("Mixer", mixerStack);
    }

    // ---------- right: tuning ----------

    private void BuildTuningCard()
    {
        _presetCombo = new ComboBox { MinWidth = 220 };
        _tuningRows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        // A plain label beside the box (like "Frets"): text placed inside this CheckBox rendered dark on dark.
        _keepFrets = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var keepFretsText = new TextBlock
        {
            Text = "Keep fret numbers (notes change pitch, like retuning the instrument)",
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        keepFretsText.MouseLeftButtonUp += (_, _) => _keepFrets.IsChecked = _keepFrets.IsChecked != true;
        var keepFretsRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_keepFrets, Dock.Left);
        keepFretsRow.Children.Add(_keepFrets);
        keepFretsRow.Children.Add(keepFretsText);
        _frets = new TextBox { Text = _track.NumberOfFrets.ToString(), Width = 50 };
        _capo = new TextBox { Text = _track.Capo.ToString(), Width = 50 };
        _stringsLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };

        _presetCombo.SelectionChanged += (_, _) =>
        {
            if (_syncingPreset) return;
            var p = TrackPropertiesWindow.PresetsFor(_tunings.Count, IsBassNow()).FirstOrDefault(x => x.Name == (string?)_presetCombo.SelectedItem);
            if (p is null) return;
            _tunings = p.HighToLow.ToList();
            RebuildStrings();
            RefreshPresets();
        };
        var tuningTools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        tuningTools.Children.Add(Wide("All −1", "Tune every string down a semitone", () => { _tunings = _tunings.Select(t => Math.Clamp(t - 1, 12, 100)).ToList(); RebuildStrings(); RefreshPresets(); }));
        tuningTools.Children.Add(Wide("All +1", "Tune every string up a semitone", () => { _tunings = _tunings.Select(t => Math.Clamp(t + 1, 12, 100)).ToList(); RebuildStrings(); RefreshPresets(); }));
        tuningTools.Children.Add(Wide("+ Low string", "Add a string a fourth below the lowest", () => { if (_tunings.Count < 9) { _tunings.Add(Math.Max(12, _tunings[^1] - 5)); RebuildStrings(); RefreshPresets(); } }));
        tuningTools.Children.Add(Wide("− Low string", "Remove the lowest string", () => { if (_tunings.Count > 3) { _tunings.RemoveAt(_tunings.Count - 1); RebuildStrings(); RefreshPresets(); } }));

        var presetRow = new StackPanel { Orientation = Orientation.Horizontal };
        presetRow.Children.Add(_stringsLabel);
        presetRow.Children.Add(_presetCombo);
        var fretRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        fretRow.Children.Add(new TextBlock { Text = "Frets", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        fretRow.Children.Add(_frets);
        fretRow.Children.Add(new TextBlock { Text = "Capo", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
        fretRow.Children.Add(_capo);
        var tuningPanel = new StackPanel();
        tuningPanel.Children.Add(presetRow);
        tuningPanel.Children.Add(_tuningRows);
        tuningPanel.Children.Add(tuningTools);
        tuningPanel.Children.Add(keepFretsRow);
        tuningPanel.Children.Add(fretRow);
        _tuningCard = Card("Tuning", tuningPanel);
        UiIds.Id(_tuningCard, "Track.Tuning");
    }

    private static Button Wide(string text, string tip, Action act)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
        b.Click += (_, _) => act();
        return b;
    }

    private bool IsBassNow() => CurrentKind == TrackKind.Bass;

    private void RefreshPresets()
    {
        _syncingPreset = true;
        _presetCombo.Items.Clear();
        foreach (var p in TrackPropertiesWindow.PresetsFor(_tunings.Count, IsBassNow())) _presetCombo.Items.Add(p.Name);
        var match = TrackPropertiesWindow.PresetsFor(_tunings.Count, IsBassNow()).FirstOrDefault(p => p.HighToLow.SequenceEqual(_tunings));
        if (match is null) { _presetCombo.Items.Insert(0, "Custom"); _presetCombo.SelectedIndex = 0; }
        else _presetCombo.SelectedItem = match.Name;
        _syncingPreset = false;
    }

    private void RebuildStrings()
    {
        _tuningRows.Children.Clear();
        _stringsLabel.Text = $"{_tunings.Count} strings";
        for (var i = 0; i < _tunings.Count; i++) _tuningRows.Children.Add(BuildStringRow(i));
    }

    private Grid BuildStringRow(int index)
    {
        var row = new Grid { Height = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = new TextBlock { Text = (index + 1).ToString(), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        // The string itself: thicker for lower pitches, like real wound strings.
        var thickness = Math.Clamp(1 + (70 - _tunings[index]) / 9.0, 1, 5);
        var line = new Rectangle
        {
            Height = thickness, VerticalAlignment = VerticalAlignment.Center, RadiusX = thickness / 2, RadiusY = thickness / 2,
            Fill = new LinearGradientBrush(Color.FromRgb(0xE8, 0xE0, 0xC8), Color.FromRgb(0x9A, 0x8C, 0x6A), 90)
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        controls.Children.Add(StepButton("−", -1, "Tune this string down a semitone", index));
        controls.Children.Add(BuildNoteChip(index));
        controls.Children.Add(StepButton("+", 1, "Tune this string up a semitone", index));
        Grid.SetColumn(line, 1); Grid.SetColumn(controls, 2);
        row.Children.Add(number); row.Children.Add(line); row.Children.Add(controls);
        return row;
    }

    private Button StepButton(string text, int delta, string tip, int index)
    {
        var b = new Button { Content = text, Width = 26, Height = 24, Padding = new Thickness(0), ToolTip = tip, Margin = new Thickness(2, 0, 2, 0) };
        b.Click += (_, _) => { _tunings[index] = Math.Clamp(_tunings[index] + delta, 12, 100); RebuildStrings(); RefreshPresets(); };
        return b;
    }

    private Border BuildNoteChip(int index)
    {
        var colour = _colour;
        var note = new Border
        {
            Width = 52, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x40, colour.R, colour.G, colour.B)),
            BorderBrush = new SolidColorBrush(colour), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = TrackPropertiesWindow.NoteName(_tunings[index]), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
            ToolTip = $"MIDI {_tunings[index]} - double-click to type a note (e.g. D2, Eb3, F#4)",
            Cursor = System.Windows.Input.Cursors.Hand
        };
        // Hover glow in the track colour.
        note.MouseEnter += (_, _) => note.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = _colour, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
        note.MouseLeave += (_, _) => note.Effect = null;
        // Double-click: type the note name. Enter accepts; Esc or clicking elsewhere cancels.
        note.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || note.Child is TextBox) return;
            e.Handled = true;
            var label = note.Child;
            var box = new TextBox { Text = TrackPropertiesWindow.NoteName(_tunings[index]), BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = Brushes.White };
            var finished = false;
            void Finish(bool accept)
            {
                if (finished) return;
                finished = true;
                if (accept && TrackPropertiesWindow.TryParseNote(box.Text, out var midi))
                {
                    _tunings[index] = Math.Clamp(midi, 12, 100);
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
        return note;
    }

    // ---------- right: drum notation ----------

    /// <summary>Drum tracks: how the kit is written (the reference numbers, GP6/7 abbreviations, drum-tab lines, or custom).</summary>
    private Border BuildDrumCard()
    {
        _drumPreset = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var p in DrumMaps.Presets) _drumPreset.Items.Add(new ComboBoxItem { Content = DrumMaps.DisplayName(p), Tag = p });
        SelectDrumPreset(string.IsNullOrEmpty(_track.DrumMapPreset) ? DrumMaps.GuitarPro5 : _track.DrumMapPreset);
        _customMap = _track.CustomDrumMap?.Select(e => e.Clone()).ToList();
        var editMap = new Button { Content = "Edit custom map…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        editMap.Click += (_, _) =>
        {
            var probe = new TrackModel { Kind = TrackKind.Drums, DrumMapPreset = CurrentDrumPreset(), CustomDrumMap = _customMap };
            var edited = DrumMapWindow.Show(_w, probe);
            if (edited is null) return;
            _customMap = edited;
            SelectDrumPreset(DrumMaps.Custom);
        };
        var drumRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _drumPreset, editMap } };
        var drumPanel = new StackPanel { Children = { drumRow } };
        drumPanel.Children.Add(Hint(".gp5 file drum map: GM numbers on the TAB. .gp6/.gp7 file drum map: kit abbreviations (BD, SD, HH…). Drum tab: one line per drum with x / o. The staff uses standard drum positions in every preset; the key map above the score shows the same labels."));
        _drumCard = Card("Drum notation", drumPanel);
        _drumCard.Visibility = _track.Kind == TrackKind.Drums || _track.MidiChannel == 9 ? Visibility.Visible : Visibility.Collapsed;
        return _drumCard;
    }

    private void SelectDrumPreset(string name) => _drumPreset.SelectedItem = _drumPreset.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == name);

    private string CurrentDrumPreset() => (_drumPreset.SelectedItem as ComboBoxItem)?.Tag as string ?? DrumMaps.GuitarPro5;

    // ---------- footer ----------

    private DockPanel BuildFooter()
    {
        _error = new TextBlock { Foreground = Brushes.IndianRed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Content = _add is null ? "OK" : "Add track", Width = _add is null ? 92 : 110, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 92, IsCancel = true };
        UiIds.Id(ok, "Track.Ok");
        UiIds.Id(cancel, "Track.Cancel");
        var footer = new DockPanel { Margin = new Thickness(16, 10, 16, 14), LastChildFill = true };
        DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(ok, Dock.Right);
        footer.Children.Add(cancel); footer.Children.Add(ok);
        // Add mode: where the new track goes in the track list.
        if (_add is not null) footer.Children.Add(BuildPositionRow(_add));
        footer.Children.Add(_error);
        ok.Click += (_, _) => OnOk();
        return footer;
    }

    private StackPanel BuildPositionRow(TrackPropertiesWindow.AddTrackPlacement add)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        row.Children.Add(new TextBlock { Text = "Position", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var position = _position = new ComboBox { Width = 230, VerticalAlignment = VerticalAlignment.Center };
        position.Items.Add($"Last (track {add.TrackCount + 1})");
        position.Items.Add("First (track 1)");
        if (add.SelectedIndex >= 0 && add.SelectedIndex < add.TrackCount)
            position.Items.Add($"After the selected track (track {add.SelectedIndex + 2})");
        position.Items.Add("As track number…");
        position.SelectedIndex = 0;
        var positionNumber = _positionNumber = new TextBox { Width = 46, Margin = new Thickness(6, 0, 0, 0), Text = (add.TrackCount + 1).ToString(), IsEnabled = false,
            VerticalContentAlignment = VerticalAlignment.Center, ToolTip = $"1 to {add.TrackCount + 1}" };
        position.SelectionChanged += (_, _) => positionNumber.IsEnabled = (string?)position.SelectedItem == "As track number…";
        row.Children.Add(position);
        row.Children.Add(positionNumber);
        DockPanel.SetDock(row, Dock.Left);
        return row;
    }

    // ---------- accept / cancel ----------

    private void OnOk()
    {
        if (_track.IsAudio) { ApplyAudioTrack(); _accepted = true; _w.DialogResult = true; return; }
        if (!int.TryParse(_program.Text, out var prog) || prog is < 0 or > 127) { _error.Text = "MIDI program must be 0–127."; return; }
        if (!int.TryParse(_channel.Text, out var ch) || ch is < 0 or > 15) { _error.Text = "MIDI channel must be 0–15."; return; }
        if (!int.TryParse(_frets.Text, out var fr) || fr is < 12 or > 36) { _error.Text = "Frets must be 12–36."; return; }
        if (!int.TryParse(_capo.Text, out var cp) || cp is < 0 or > 12) { _error.Text = "Capo must be 0–12."; return; }
        if (_add is not null && _position is not null && _positionNumber is not null)
        {
            var choice = (string?)_position.SelectedItem ?? "";
            if (choice.StartsWith("First", StringComparison.Ordinal)) _add.InsertIndex = 0;
            else if (choice.StartsWith("After", StringComparison.Ordinal)) _add.InsertIndex = _add.SelectedIndex + 1;
            else if (choice.StartsWith("As track", StringComparison.Ordinal))
            {
                if (!int.TryParse(_positionNumber.Text.Trim(), out var n) || n < 1 || n > _add.TrackCount + 1)
                { _error.Text = $"Track number must be 1–{_add.TrackCount + 1}."; return; }
                _add.InsertIndex = n - 1;
            }
            else _add.InsertIndex = _add.TrackCount;
        }
        if (FamilyChanges && !FamilyChangePrompt.Confirm(_w, _track, CurrentInstrument, CurrentKind == TrackKind.Drums)) return;
        ApplyToTrack(prog, ch, fr, cp);
        _accepted = true;
        _w.DialogResult = true;
    }

    private void ApplyToTrack(int prog, int ch, int fr, int cp)
    {
        var track = _track;
        track.Name = string.IsNullOrWhiteSpace(_name.Text) ? track.Name : _name.Text.Trim();
        track.Performer = _performer.Text.Trim();
        track.TrackNotes = _notes.Text;
        track.ColorHex = TabForge.Visualization.ColourText.Hex(_colour);
        track.TintRow = _tint.IsChecked == true;
        var selected = InstrumentNaming.WithoutStringCount(CurrentInstrument);
        var preset = TrackController.InstrumentPresets.FirstOrDefault(p => p.Name == selected);
        var picked = InstrumentCatalog.Find(selected);
        // Extended-range basses/guitars carry "(N strings)" in their name.
        track.InstrumentName = InstrumentNaming.ForStringCount(selected, TrackPropertiesWindow.KindOf(selected, track), _tunings.Count);
        if (preset is not null) { track.Rig.Name = preset.Rig; track.Rig.ArticulationMap = preset.Map; }
        else if (picked is not null) { track.Rig.Name = picked.Name; track.Rig.ArticulationMap = picked.Map; }
        var convert = FamilyChanges;   // before the channel changes: it reads the track's current family
        var toDrums = CurrentKind == TrackKind.Drums;
        track.MidiProgram = prog;
        track.MidiChannel = ch;
        track.NumberOfFrets = fr;
        if (convert) TrackSetup.ConvertFamily(track, toDrums, CurrentKind, _tunings);
        if (_drumCard.Visibility == Visibility.Visible && (_drumPreset.SelectedItem as ComboBoxItem)?.Tag is string chosenDrumPreset)
        {
            track.CustomDrumMap = _customMap;
            DrumMaps.Apply(track, chosenDrumPreset);
        }
        track.Volume = (int)_volume.Value;
        track.Pan = (int)_pan.Value;
        track.NumberOfFrets = fr;
        // The capo moves the sounding pitch of the notes (frets are relative to it); the written frets stay.
        TrackSetup.SetCapo(track, cp);
        // Drum tracks have no tuning: their lines come from the drum notation preset instead.
        if (_drumCard.Visibility != Visibility.Visible) TrackPropertiesWindow.ApplyTuning(track, _tunings, _keepFrets.IsChecked == true);
        // A new track takes the type of the instrument picked (drums, bass, keys or guitar).
        if (_add is not null)
        {
            track.Kind = _drumCard.Visibility == Visibility.Visible || ch == 9 ? TrackKind.Drums : TrackPropertiesWindow.KindOf(selected, track);
            // The placeholder name follows the instrument unless the user typed their own.
            if (_name.Text.Trim() == _defaultName) track.Name = selected;
        }
        else if (!_trackHadNotes && track.Kind != TrackKind.Drums && TrackPropertiesWindow.KindOf(selected, track) is var newKind and not TrackKind.Drums) track.Kind = newKind;
    }

    /// <summary>What the user changed so far, for the discard confirmation.</summary>
    private List<string> PendingChanges()
    {
        var track = _track;
        var list = new List<string>();
        void Diff(string label, string before, string after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal)) list.Add($"{label}: {before} → {after}");
        }
        Diff("Track name", track.Name, _name.Text.Trim());
        Diff("Played by", track.Performer, _performer.Text.Trim());
        if (!string.Equals(track.TrackNotes, _notes.Text, StringComparison.Ordinal)) list.Add("Notes edited");
        Diff("Colour", track.ColorHex.ToUpperInvariant(), TabForge.Visualization.ColourText.Hex(_colour));
        Diff("Instrument", track.InstrumentName, CurrentInstrument);
        Diff("MIDI program", track.MidiProgram.ToString(), _program.Text.Trim());
        Diff("MIDI channel", track.MidiChannel.ToString(), _channel.Text.Trim());
        Diff("Volume", track.Volume.ToString(), ((int)_volume.Value).ToString());
        Diff("Pan", track.Pan.ToString(), ((int)_pan.Value).ToString());
        Diff("Tuning", string.Join(" ", track.StringTunings.Select(TrackPropertiesWindow.NoteName)), string.Join(" ", _tunings.Select(TrackPropertiesWindow.NoteName)));
        Diff("Frets", track.NumberOfFrets.ToString(), _frets.Text.Trim());
        Diff("Capo", track.Capo.ToString(), _capo.Text.Trim());
        return list;
    }

    /// <summary>Closing without OK (Cancel, Esc, the X) with edits made: confirm, listing what changed.</summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_accepted || _add is not null) return; // cancelling Add track simply adds nothing
        var changes = PendingChanges();
        if (changes.Count == 0) return;
        if (!DiscardPrompt.Confirm(_w, "Unsaved track changes", "Discard the changes made to this track?", changes,
            "Return to the track properties")) e.Cancel = true;
    }
}
