using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Platform.Theme;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The track properties sections beyond the fields: the instrument cover (the catalogue badge on the track colour, or the owner's line art when the instrument has none), the instrument button,
/// the colour and tint, the tuning preset box, the keep-frets box and the drum notation card.
/// Owns: building those controls and writing them on OK. Does not own: the rules (TrackSetup, DrumMaps, ColourHex, TuningPresets) or the icon art (TrackIconArt).
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests.
/// </summary>
public sealed partial class TrackPropertiesWindow
{
    // The cover: the track colour as a soft gradient behind the instrument badge (160 DIP, 12 above and below) and its name.
    private const double CoverBadgeSize = 160, CoverBadgeGap = 12, CoverGradientAngle = 55, CoverHeight = 190;
    private const byte CoverAlphaStart = 0x55, CoverAlphaEnd = 0x10;
    private const double NotesMinHeight = 64;
    private const string TintTip = "The strength is set in Settings > Appearance > Track colour tint.";
    private const double DrumMapBoxWidth = 200, PresetBoxMinWidth = 220;
    private const string DrumNotationHint = ".gp5 file drum map: GM numbers on the TAB. .gp6/.gp7 file drum map: kit abbreviations (BD, SD, HH…). "
        + "Drum tab: one line per drum with x / o. The staff uses standard drum positions in every preset; the key map above the score shows the same labels.";
    private readonly TrackIconPicture _picture = new() { Name = "InstrumentPicture", Width = CoverBadgeSize, Height = CoverBadgeSize, Margin = new Thickness(0, CoverBadgeGap) };
    private readonly InstrumentBadgeView _badge = new(CoverBadgeSize) { Name = "InstrumentBadge", Margin = new Thickness(0, CoverBadgeGap) };
    private readonly InstrumentBadgeView _chooserBadge = new(30) { Name = "ChooserBadge", Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _caption = new() { Name = "InstrumentCaption", FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -6, 0, 10) };
    private readonly TextBlock _chooserName = new() { Name = "ChooserName", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _cover = new() { Name = "InstrumentCover", Height = CoverHeight, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 10), ClipToBounds = true };
    private ColourButton _colour = null!;
    private readonly List<string> _recentColours = new();   // this window's picks: it has no settings to keep them in
    private CheckBox _tint = null!, _keepFrets = null!;
    private ComboBox _drumMap = null!, _presetCombo = null!;
    private bool _syncingPresets;
    private Border _drumCard = null!, _tuningCard = null!;
    private List<DrumMapEntry>? _customMap;

    private Control PictureAndColour()
    {
        _colour = new ColourButton(_track.ColorHex, _recentColours) { Name = "ColourBox", Margin = new Thickness(0, 2, 0, 2) };
        _colour.Show(_track.ColorHex);
        AutomationProperties.SetName(_colour, "Track colour");
        _tint = new CheckBox { Name = "TintBox", Content = new TextBlock { Text = "Tint the track's row and lane with its colour", TextWrapping = TextWrapping.Wrap }, IsChecked = _track.TintRow, Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(_tint, TintTip);
        _colour.Previewed += _ => UpdateSwatch();
        _program.TextChanged += (_, _) => UpdatePicture();
        _channel.TextChanged += (_, _) => UpdatePicture();
        _cover.Child = new Panel { Children = { _badge, _picture }, HorizontalAlignment = HorizontalAlignment.Center };
        UpdateSwatch();
        UpdatePicture();
        return new StackPanel { Children = { Caption("Colour"), _colour, _tint } };
    }

    // The performer with its hint ("Played by"); an audio track has no performer, so the fields stay empty.
    private StackPanel PlayedBy()
    {
        var fields = new StackPanel();
        if (_track.IsAudio) return fields;
        fields.Children.Add(Caption("Played by"));
        fields.Children.Add(_performer);
        fields.Children.Add(Hint("The musician who normally plays this part."));
        return fields;
    }

    // The track notes: a box tall enough for several lines.
    private StackPanel NotesBox() => new() { Children = { Caption("Notes"), _notes } };

    private CheckBox KeepFretsBox() => _keepFrets = new CheckBox
    {
        Name = "KeepFretsBox", IsChecked = true, Margin = new Thickness(0, 8, 0, 0),
        Content = "Keep fret numbers (notes change pitch, like retuning the instrument)",
    };

    // Drum tracks: the notation preset sets the tab lines; "Edit custom map..." edits the track's own map (TrackDrumMapWindow) and selects Custom.
    private Border DrumCard()
    {
        _drumMap = new ComboBox { Name = "DrumMapBox", Width = DrumMapBoxWidth };
        AutomationProperties.SetName(_drumMap, "Drum notation");
        foreach (var preset in DrumMaps.Presets) _drumMap.Items.Add(new ComboBoxItem { Content = DrumMaps.DisplayName(preset), Tag = preset });
        SelectDrumPreset(string.IsNullOrEmpty(_track.DrumMapPreset) ? DrumMaps.GuitarPro5 : _track.DrumMapPreset);
        _customMap = _track.CustomDrumMap?.Select(e => e.Clone()).ToList();
        var editMap = new Button { Name = "EditCustomMapButton", Content = "Edit custom map…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2) };
        editMap.Click += async (_, _) =>
        {
            if (await DialogLauncher.Ask<TrackDrumMapWindow, List<DrumMapEntry>?>(this, () => new TrackDrumMapWindow(DrumMapProbe())) is { } edited) TakeCustomMap(edited);
        };
        var drumRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _drumMap, editMap } };
        var card = Card("Drum notation", new StackPanel { Children = { drumRow, Hint(DrumNotationHint) } });
        card.Name = "DrumCard";
        _drumCard = card;
        return card;
    }

    // What the custom map window starts from: the chosen preset with the working custom map.
    internal TrackModel DrumMapProbe() => new() { Kind = TrackKind.Drums, DrumMapPreset = CurrentDrumPreset(), CustomDrumMap = _customMap };

    internal void TakeCustomMap(List<DrumMapEntry> edited)
    {
        _customMap = edited;
        SelectDrumPreset(DrumMaps.Custom);
    }

    private void SelectDrumPreset(string preset) => _drumMap.SelectedItem = _drumMap.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == preset);

    private string CurrentDrumPreset() => (_drumMap.SelectedItem as ComboBoxItem)?.Tag as string ?? DrumMaps.GuitarPro5;

    // The tuning preset box, beside the string count: a preset sets every string box.
    private StackPanel PresetRow()
    {
        _presetCombo = new ComboBox { Name = "TuningPresetBox", MinWidth = PresetBoxMinWidth, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(_presetCombo, "Tuning preset");
        _presetCombo.SelectionChanged += (_, _) => { if (!_syncingPresets) ApplyPickedPreset(); };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { _stringsLabel, _presetCombo } };
    }

    private void ApplyPickedPreset()
    {
        var picked = _presetCombo.SelectedItem as string;
        var preset = PresetsForTrack().FirstOrDefault(p => p.Name == picked);
        if (preset is null) return;
        _tunings.Clear();
        _tunings.AddRange(preset.HighToLow);
        RebuildStringRows();
    }

    // Lists the presets for this string count and instrument. A tuning that matches none is shown as Custom, first in the list.
    private void RefreshPresets()
    {
        _syncingPresets = true;
        _presetCombo.Items.Clear();
        var presets = PresetsForTrack().ToList();
        foreach (var preset in presets) _presetCombo.Items.Add(preset.Name);
        var match = presets.FirstOrDefault(p => p.HighToLow.SequenceEqual(_tunings));
        if (match is null) { _presetCombo.Items.Insert(0, "Custom"); _presetCombo.SelectedIndex = 0; }
        else _presetCombo.SelectedItem = match.Name;
        _syncingPresets = false;
    }

    private IEnumerable<TuningPreset> PresetsForTrack() => TuningPresets.For(_tunings.Count, _track.Kind == TrackKind.Bass);

    private string? ColourError() => ColourHex.TryParseStrict(_colour.Value.Trim(), out _) ? null : $"Colour must be a hex value like {TabForge.Platform.Theme.Palette.Hex("Accent", false)}.";

    // Runs inside the OK edit, after the fields: colour, tint, then the retune or the drum lines (drum tracks have no tuning).
    private void WriteSections()
    {
        var picked = _colour.Value.Trim().ToUpperInvariant();
        if (!string.Equals(picked, _track.ColorHex, System.StringComparison.OrdinalIgnoreCase)) _track.ColorFollowsInstrument = false;
        _track.ColorHex = picked;
        _track.TintRow = _tint.IsChecked == true;
        if (_track.Kind == TrackKind.Drums)
        {
            var preset = CurrentDrumPreset();
            if (preset != _track.DrumMapPreset || preset == DrumMaps.Custom)
            {
                _track.CustomDrumMap = _customMap;
                DrumMaps.Apply(_track, preset);
            }
        }
        else TrackSetup.ApplyTuning(_track, _tunings, _keepFrets.IsChecked == true);
    }

    private void UpdateSwatch()
    {
        var parsed = ColourHex.TryParseStrict(_colour.Value.Trim(), out var c);
        _cover.Background = parsed ? CoverGradient(c) : null;
    }

    // A linear gradient brush (from, to, 55 degrees): the line through the box centre at that angle.
    private static LinearGradientBrush CoverGradient(Rgba c)
    {
        var turn = CoverGradientAngle * System.Math.PI / 180;
        double dx = System.Math.Cos(turn) / 2, dy = System.Math.Sin(turn) / 2;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5 - dx, 0.5 - dy, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5 + dx, 0.5 + dy, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(CoverAlphaStart, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(CoverAlphaEnd, c.R, c.G, c.B), 1) },
        };
    }

    // The instrument button: the badge, the name and "Change..."; it opens the picker of the track row icon (InstrumentPickerFlow).
    private Button InstrumentChooser()
    {
        var change = new TextBlock { Text = "Change…", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.7 };
        var button = new Button
        {
            // The chooser is a full-width bordered box with its badge, name and "Change..." centred.
            Name = "InstrumentChooserButton", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(8, 5, 8, 5),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _chooserBadge, _chooserName, change } },
        };
        ToolTip.SetTip(button, "Open the instrument catalogue (search, pictures)");
        button.Click += async (_, _) =>
        {
            var project = _document?.Project ?? SongDocument.BlankProject();
            await _picker.ChooseAsync(this, _adding ? null : _document, project, _track, ReloadInstrument, ReloadInstrument);
        };
        return button;
    }

    // Drum notation for a drum track, tuning for a pitched one that has strings (drum and keys tracks have none).
    private void ShowFamilyCards()
    {
        _drumCard.IsVisible = _track.IsDrumTrack;
        _tuningCard.IsVisible = _track.Kind is not (TrackKind.Drums or TrackKind.Keys);
    }

    // The track now has the previewed, committed or restored instrument (its program, channel, strings and kind follow): the form shows the track again.
    private void ReloadInstrument()
    {
        _program.Text = _track.MidiProgram.ToString();
        _channel.Text = _track.MidiChannel.ToString();
        _tunings.Clear();
        _tunings.AddRange(_track.StringTunings);
        RebuildStrings();
        ShowFamilyCards();
        UpdatePicture();
    }

    private void UpdatePicture()
    {
        var program = int.TryParse(Typed(_program), out var p) ? p : _track.MidiProgram;
        var drums = _track.Kind == TrackKind.Drums || Typed(_channel) == "9";
        var entry = InstrumentCatalog.ForTrack(_track.InstrumentName, program, drums);
        _badge.Entry = _chooserBadge.Entry = entry;
        var hasArt = _badge.HasArt;   // one read: the second call would only hit the cache
        _badge.IsVisible = hasArt;
        _picture.IsVisible = !hasArt;
        _picture.IconKey = TrackIconKeys.KeyFor(program, drums);
        _caption.Text = _chooserName.Text = _track.InstrumentName;
    }
}
