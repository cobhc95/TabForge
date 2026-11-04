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
using TabForge.Models;
using TabForge.Services;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The track properties sections beyond the fields: the instrument button (its badge, name and Change button),
/// the colour and tint, the tuning preset box, the keep-frets box and the drum notation card.
/// Owns: building those controls and writing them on OK; the instrument picker keeps its pick with no undo step (OK commits it with everything else as one step, Cancel restores the song) and refreshes the form only, resetting the tuning draft only when it really changed the strings and keeping typed banks (always) and channel/capo unless the choice moved them.
/// Does not own: the rules (TrackSetup, DrumMaps, ColourHex, TuningPresets) or the icon art (TrackIconArt).
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests, TrackPropertiesWindowConcurrencyTests.
/// </summary>
public sealed partial class TrackPropertiesWindow
{
    private const double NotesMinHeight = 40;
    private const string TintTip = "The strength is set in Settings > Appearance > Track colour tint.";
    private const double DrumMapBoxWidth = 200;
    private const string DrumNotationHint = ".gp5 file drum map: GM numbers on the TAB. .gp6/.gp7 file drum map: kit abbreviations (BD, SD, HH…). "
        + "Drum tab: one line per drum with x / o. The staff uses standard drum positions in every preset; the key map above the score shows the same labels.";
    private readonly InstrumentBadgeView _chooserBadge = new(26) { Name = "ChooserBadge", Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _chooserName = new() { Name = "ChooserName", FontSize = ChromeMetrics.TextBody, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private ColourButton _colour = null!;
    private readonly List<string> _recentColours = new();   // this window's picks: it has no settings to keep them in
    private CheckBox _tint = null!, _keepFrets = null!;
    private ComboBox _drumMap = null!;
    private Border _drumCard = null!, _tuningCard = null!;
    private List<DrumMapEntry>? _customMap;

    private Control PictureAndColour()
    {
        _colour = new ColourButton(_track.ColorHex, _recentColours) { Name = "ColourBox", Margin = new Thickness(0, 1, 0, 1) };
        _colour.Show(_track.ColorHex);
        AutomationProperties.SetName(_colour, "Track colour");
        _tint = new CheckBox { Name = "TintBox", Content = new TextBlock { Text = "Tint the track's row and lane with its colour", TextWrapping = TextWrapping.Wrap }, IsChecked = _track.TintRow, Margin = new Thickness(0, 2, 0, 0) };
        ToolTip.SetTip(_tint, TintTip);
        _program.TextChanged += (_, _) => UpdatePicture();
        _channel.TextChanged += (_, _) => UpdatePicture();
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
        Name = "KeepFretsBox", IsChecked = true, Margin = new Thickness(0, 4, 0, 0),
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
        else _draft.Commit(_track, _keepFrets.IsChecked == true);
    }

    // The instrument button: the badge, the name and "Change..."; it opens the picker of the track row icon (InstrumentPickerFlow).
    private Button InstrumentChooser()
    {
        var change = new TextBlock { Text = "Change…", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.7 };
        var button = new Button
        {
            // The chooser is a full-width bordered box with its badge, name and "Change..." centred.
            Name = "InstrumentChooserButton", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(6, 3, 6, 3),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _chooserBadge, _chooserName, change } },
        };
        ToolTip.SetTip(button, "Open the instrument catalogue (search, pictures)");        button.Click += async (_, _) =>
        {
            if (_pickerRunning) return;
            _pickerRunning = true;
            var tunings = _track.StringTunings.ToList();
            var kind = _track.Kind;
            var savedTrackChannel = _track.MidiChannel;
            var savedTrackCapo = _track.Capo;
            var savedProgram = _program.Text;
            var savedBankMsb = _bankMsb.Text;
            var savedBankLsb = _bankLsb.Text;
            var savedChannel = _channel.Text;
            try
            {
                var project = _document?.Project ?? SongDocument.BlankProject();
                // Preview refreshes the form only; the tuning draft stays untouched until the picker ends.
                // Inside the properties the pick stores no undo step: OK commits it with everything else as one step,
                // Cancel restores the song. The engine still follows the previews live through the picker's sound.
                var kept = await _picker.ChooseAsync(this, _adding ? null : _document, project, _track, ReloadInstrumentForm, storeUndo: _adding);
                if (_closed) return;
                if (kept) _pendingInstrument = _track.InstrumentName;
                ReloadInstrument(_track.Kind != kind || !_track.StringTunings.SequenceEqual(tunings));
                if (!kept)
                {
                    // A cancelled pick must not lose what was typed before it opened: the form reload showed the
                    // restored track, so put the typed program, banks and channel back.
                    _program.Text = savedProgram;
                    _bankMsb.Text = savedBankMsb;
                    _bankLsb.Text = savedBankLsb;
                    _channel.Text = savedChannel;
                    UpdatePicture();
                }
                else
                {
                    // A kept pick sets the program; banks are never touched by a choice, so the typed banks stay.
                    // The channel stays typed unless the choice moved the track to the other family (drums 9 and back).
                    _bankMsb.Text = savedBankMsb;
                    _bankLsb.Text = savedBankLsb;
                    if (_track.MidiChannel == savedTrackChannel) _channel.Text = savedChannel;
                    // The capo follows the track when the choice reset it (family/kind switch to 0);
                    // otherwise the working capo (possibly edited before the picker) is kept.
                    if (_track.Capo != savedTrackCapo) { _capoFret = _track.Capo; LoadPartialCapos(); }
                    UpdatePicture();
                }
            }
            finally { _pickerRunning = false; }
        };
        return button;
    }

    // Drum notation for a drum track, tuning for a pitched one that has strings (drum and keys tracks have none).
    private void ShowFamilyCards()
    {
        _drumCard.IsVisible = _track.IsDrumTrack;
        _capoCard.IsVisible = _tuningCard.IsVisible = _track.Kind is not (TrackKind.Drums or TrackKind.Keys);
    }

    // The previewed, committed or restored instrument (its program, channel and kind follow): the form shows the track again.
    private void ReloadInstrumentForm()
    {
        _program.Text = _track.MidiProgram.ToString();
        _bankMsb.Text = _track.MidiBankMsb.ToString();
        _bankLsb.Text = _track.MidiBankLsb.ToString();
        _channel.Text = _track.MidiChannel.ToString();
        ShowFamilyCards();
        UpdatePicture();
    }

    // The picker ended: reset the working strings only when it really changed them (a cancel restores the track, so an edited draft survives).
    private void ReloadInstrument(bool resetDraft)
    {
        ReloadInstrumentForm();
        if (resetDraft)
        {
            _draft.Reset(_track.StringTunings);
            _disclosedUnplayable.Clear();
        }
        RebuildStrings();
    }

    private void UpdatePicture()
    {
        var program = int.TryParse(Typed(_program), out var p) ? p : _track.MidiProgram;
        var drums = _track.Kind == TrackKind.Drums || Typed(_channel) == "9";
        _chooserBadge.Entry = InstrumentCatalog.ForTrack(_track.InstrumentName, program, drums);
        _chooserName.Text = _track.InstrumentName;
    }
}
