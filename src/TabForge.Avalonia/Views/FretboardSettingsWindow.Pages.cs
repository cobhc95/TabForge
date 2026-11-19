using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the five pages of the Fretboard settings window, their cards and the small row builders they share (a chip row, a switch, a slider). Each row names
//   its setting; picking writes it and asks the window to save and redraw. Reset section writes that page's defaults back; Cancel writes the opening values back.
// Does not own: the window frame, the preview and saving (FretboardSettingsWindow.cs) or the setting types.
// Tests: TabForge.Avalonia.Tests.FretboardSettingsWindowTests.
public sealed partial class FretboardSettingsWindow
{
    private static string SectionSubtitle(int section) => section switch
    {
        0 => "Wood grain, binding, inlays and overall board preset",
        1 => "Look-ahead trail, note dot labels, size and colours",
        2 => "Fret range, string spacing, orientation and dock position",
        3 => "String material, vibration feedback and capo finish",
        _ => "How scale notes are drawn over the fretboard",
    };

    private void ShowSection(int section)
    {
        var panel = new StackPanel { Spacing = FretboardChromeMetrics.PageCardGap }.WithMargin(18, 14, 18, 14);
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = SectionName(Math.Clamp(section, 0, SectionCount - 1)), FontSize = FretboardChromeMetrics.PageTitleSize, FontWeight = global::Avalonia.Media.FontWeight.SemiBold });
        var sub = new TextBlock { Text = SectionSubtitle(section), FontSize = 12, Margin = new global::Avalonia.Thickness(0, 2, 0, 0) };
        sub[!TextBlock.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("MutedTextBrush");
        titleStack.Children.Add(sub);
        var reset = new Button { Name = "FretboardResetSection", Content = "Reset section", VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
        reset.Classes.Add("PrefsActionButton");
        reset.Click += (_, _) => ResetSection(SelectedSection);
        DockPanel.SetDock(reset, Dock.Right);
        var headerBorder = new Border { Child = new DockPanel { Children = { reset, titleStack } }, Padding = new global::Avalonia.Thickness(0, 0, 0, 10), BorderThickness = new global::Avalonia.Thickness(0, 0, 0, 1) };
        headerBorder[!Border.BorderBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("BorderSoftBrush");
        panel.Children.Add(headerBorder);
        global::Avalonia.Controls.Controls Card(string name)
        {
            var rows = new StackPanel();
            panel.Children.Add(PreferencesSpecialPages.Card(name, rows));
            return rows.Children;
        }
        var ed = _settings.Editing;
        var ap = _settings.Appearance;
        switch (section)
        {
            case 0:
                var wood = Card("Wood and binding");
                Chips(wood, "Look preset", "FretboardLook", FretboardStyles.PresetNames, () => FretboardStyles.PresetOf(FretboardStyles.LookOf(ed)), v => FretboardStyles.Apply(ed, FretboardStyles.OfPreset(v)), disabled: FretboardStyles.Custom, description: "Apply a complete wood, binding and inlay combination at once.");
                Chips(wood, "Fingerboard wood", "FretboardWood", FretboardStyles.Woods, () => ed.FretboardWood, v => ed.FretboardWood = v, description: "Surface texture used for the neck.");
                Chips(wood, "Binding", "FretboardBinding", FretboardStyles.Bindings, () => ed.FretboardBinding, v => ed.FretboardBinding = v, description: "Edge trim along the top and bottom of the neck.", last: true);
                var inlays = Card("Inlays");
                Chips(inlays, "Inlay shape", "FretboardInlayShape", FretboardStyles.InlayShapes, () => ed.FretboardInlayShape, v => ed.FretboardInlayShape = v, description: "Marker style on fret positions 3, 5, 7, 9, 12…");
                Chips(inlays, "Inlay material", "FretboardInlayMaterial", FretboardStyles.InlayMaterials, () => ed.FretboardInlayMaterial, v => ed.FretboardInlayMaterial = v, description: "Surface finish for position markers.");
                Chips(inlays, "Position inlay brightness", "FretboardMarkerBrightness", FretMarkerLevels.All, () => ed.FretMarkerBrightness, v => ed.FretMarkerBrightness = v, description: "How strongly fret inlays stand out against the wood.", last: true);
                Slide(Card("Board"), "Fretboard brightness", "FretboardBrightness", FretboardStyles.MinBrightness, FretboardStyles.MaxBrightness, FretboardStyles.BrightnessStep, e => e.FretboardBrightness, v => ed.FretboardBrightness = v, "%", description: "Overall brightness of the neck surface.", last: true);
                break;
            case 1:
                var ahead = Card("Look-ahead");
                Switch(ahead, "FretboardPreviewNext", "Show look-ahead notes", ed.PreviewNotesEnabled, v => ed.PreviewNotesEnabled = v, description: "Preview upcoming notes on the fretboard before they sound.");
                Slide(ahead, "Next notes shown ahead", "FretboardHorizon", 1, 10, 1, e => e.PreviewHorizon, v => ed.PreviewHorizon = v, description: "How many upcoming notes to preview.");
                Switch(ahead, "FretboardPreviewPrevious", "Show previous notes (a faint trail)", ed.PreviewPreviousNotesEnabled, v => ed.PreviewPreviousNotesEnabled = v, description: "Leave a faint trail for notes that just finished.", last: true);
                var dots = Card("Note dots");
                Chips(dots, "Note dot label", "FretboardDotSays", new[] { "Fret number", "Note name" }, () => ed.ShowNoteNames ? "Note name" : "Fret number", v => ed.ShowNoteNames = v == "Note name", description: "Labels cursor, playback and pointer-preview notes.");
                Slide(dots, "Note brightness", "FretboardNoteBrightness", FretboardStyles.MinBrightness, FretboardStyles.MaxBrightness, FretboardStyles.BrightnessStep, e => e.FretboardNoteBrightness, v => ed.FretboardNoteBrightness = v, "%", description: "Brightness of playing and cursor note dots.");
                Slide(dots, "Dot and number size", "FretboardMarkerSize", SettingRanges.FretMarkerSizePercentMin, SettingRanges.FretMarkerSizePercentMax, 10, e => e.FretMarkerSizePercent, v => ed.FretMarkerSizePercent = v, "%", description: "Overall scale of note dots on the fretboard.");
                Chips(dots, "Number size", "FretboardNumberSize", FretNumberSizes.All, () => ed.FretNumberSize, v => ed.FretNumberSize = v, description: "Text size inside each note dot.", last: true);
                NoteColoursCard(Card("Note colours"));
                break;
            case 2:
                var board = Card("Board");
                Chips(board, "Frets shown", "FretboardFrets", new[] { "12", "24" }, () => ed.FretboardFrets.ToString(), v => ed.FretboardFrets = int.Parse(v), description: "Number of frets visible across the neck.");
                Chips(board, "String spacing", "FretboardSpacing", FretStringSpacings.All, () => ed.FretStringSpacing, v => ed.FretStringSpacing = v, description: "Vertical distance between strings.");
                Switch(board, "FretboardLeftHanded", "Left-handed", ed.LeftHanded, v => ed.LeftHanded = v, description: "Flip the neck horizontally for left-handed players.", last: true);
                var place = Card("Placement");
                Chips(place, "Board position", "FretboardPosition", new[] { "Left", "Centre", "Right" }, () => ap.FretboardPosition, v => ap.FretboardPosition = v, description: "Horizontal alignment when the panel is wider than the neck.");
                Chips(place, "Dock", "FretboardDock", new[] { "Top", "Bottom" }, () => ap.FretboardAtBottom ? "Bottom" : "Top", v => ap.FretboardAtBottom = v == "Bottom", description: "Pin the fretboard above or below the score.");
                Switch(place, "FretboardLockSize", "Lock fretboard size", ap.LockInstrumentSize, v => ap.LockInstrumentSize = v, description: "Prevent automatic resizing of the fretboard panel.", last: true);
                break;
            case 3:
                var strings = Card("Strings");
                Chips(strings, "String finish", "FretboardStringLook", FretboardStyles.StringLooks, () => ed.FretboardStringLook, v => ed.FretboardStringLook = v, description: "Visual metal finish of the strings.");
                Chips(strings, "String vibration", "FretboardVibration", FretboardStyles.VibrationLevels, () => ed.FretboardVibration, v => ed.FretboardVibration = v, description: "Intensity of string vibration during playback.");
                Chips(strings, "Vibration speed", "FretboardVibrationSpeed", FretboardStyles.VibrationSpeeds, () => ed.FretboardVibrationSpeed, v => ed.FretboardVibrationSpeed = v, description: "Animation speed of vibrating strings.");
                Switch(strings, "FretboardStringNumbers", "Show string numbers", ed.FretboardStringNumbers, v => ed.FretboardStringNumbers = v, description: "Display string numbers beside the nut.", last: true);
                Chips(Card("Capo"), "Capo finish", "FretboardCapoLook", FretboardStyles.CapoLooks, () => ed.FretboardCapoLook, v => ed.FretboardCapoLook = v, description: "Appearance of the capo bar when active.", last: true);
                break;
            default:
                var highlight = Card("Highlight");
                Chips(highlight, "Style", "ScaleStyle", ScaleHighlightStyles.All, () => ScaleHighlightStyles.Normalize(ed.ScaleHighlightStyle), v => ed.ScaleHighlightStyle = v, description: "Marker shape used for scale tones.");
                ScaleColours(highlight);
                Slide(highlight, "Highlight opacity", "ScaleHighlightOpacity", ScaleHighlightStyles.MinOpacity, ScaleHighlightStyles.MaxOpacity, ScaleHighlightStyles.OpacityStep, e => e.ScaleHighlightOpacity, v => ed.ScaleHighlightOpacity = v, "%", description: "Transparency of scale highlight markers.");
                Switch(highlight, "ScaleEmphasiseRoot", "Emphasise the root", ed.ScaleEmphasiseRoot, v => ed.ScaleEmphasiseRoot = v, description: "Give the root note a distinct marker.");
                Switch(highlight, "ScaleDimOutside", "Dim notes outside the scale", ed.ScaleDimOutside, v => ed.ScaleDimOutside = v, description: "Subtly fade frets that are not in the active scale.");
                Switch(highlight, "ScaleOnlyInView", "Only on the frets in view", ed.ScaleOnlyInView, v => ed.ScaleOnlyInView = v, description: "Restrict scale markers to the currently visible fret range.", last: true);
                var clear = new Button { Name = "ScaleClearHighlight", Content = "Clear highlight" }.WithMargin(0, 10, 0, 0);
                clear.Click += (_, _) => { ed.SetScaleShown(false); Changed(); };
                highlight.Add(clear);
                break;
        }
        _page.Content = panel;
    }

    /// <summary>The Note colours card: the shared colour button for now, next and upcoming, Reset colours, and the Fill or Ring dot style.</summary>
    private void NoteColoursCard(global::Avalonia.Controls.Controls rows)
    {
        var ed = _settings.Editing;
        _noteColourButtons.Clear();
        foreach (var role in FretNoteColours.Roles)
        {
            rows.Add(ScalePopupLayout.SectionLabel(FretNoteColours.Name(role).ToUpperInvariant()));
            var light = ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
            var button = new ColourButton(FretNoteColours.DefaultHex(role, light), _settings.Appearance.RecentColours)
            {
                Name = "FretboardNoteColour" + FretNoteColours.Name(role), Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.ColourSettingWidth,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(button, FretNoteColours.Name(role) + " note colour");
            button.Show(FretNoteColours.Shown(role, FretNoteColours.Get(ed, role), light));
            button.Previewed += hex => { FretNoteColours.Set(ed, role, hex); Changed(rebuild: false); };   // every popup click shows on the board at once
            button.Picked += hex => { FretNoteColours.Set(ed, role, hex); Changed(rebuild: false); };
            _noteColourButtons[role] = button;
            rows.Add(button);
        }
        var reset = new Button { Name = "FretboardResetNoteColours", Content = "Reset colours" }.WithMargin(0, 12, 0, 0);
        reset.Click += (_, _) => { foreach (var role in FretNoteColours.Roles) FretNoteColours.Set(ed, role, ""); Changed(); };
        rows.Add(reset);
        Chips(rows, "DOT STYLE", "FretboardDotStyle", FretNoteColours.DotStyles, () => FretNoteColours.NormalizeStyle(ed.FretDotStyle), v => ed.FretDotStyle = v);
    }

    /// <summary>The note colour buttons show the stored colours again (a colour picked from the fretboard's legend while this window is open).</summary>
    internal void ShowNoteColours()
    {
        var light = ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
        foreach (var (role, button) in _noteColourButtons) button.Show(FretNoteColours.Shown(role, FretNoteColours.Get(_settings.Editing, role), light));
    }

    /// <summary>The scale highlight colours: the named colour chips and the shared colour picker for a custom one (the same picker as the Note colours card).</summary>
    private void ScaleColours(global::Avalonia.Controls.Controls rows)
    {
        var ed = _settings.Editing;
        Chips(rows, "COLOUR", "ScaleColour", ScaleHighlightStyles.Colours, () => ed.ScaleHighlightColour, v => SetScaleColour(ed, v));
        rows.Add(ScalePopupLayout.SectionLabel("CUSTOM COLOUR"));
        var light = ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
        var button = new ColourButton(ScaleColourDefault(ed, light), _settings.Appearance.RecentColours)
        {
            Name = "ScaleCustomColour", Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.ColourSettingWidth,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(button, "Scale highlight custom colour");
        button.Show(ShownScaleCustom(ed, light));
        button.Previewed += hex => { StoreScaleCustom(ed, hex); Changed(rebuild: false); };   // every popup click shows on the board at once
        button.Picked += hex => { StoreScaleCustom(ed, hex); Changed(rebuild: false); };
        _scaleCustomColourButton = button;
        rows.Add(button);
    }

    /// <summary>The custom colour button shows the stored colour again (a colour picked in the Scales popup while this window is open).</summary>
    internal void ShowScaleColour()
    {
        if (_scaleCustomColourButton is null) return;
        var light = ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
        _scaleCustomColourButton.Show(ShownScaleCustom(_settings.Editing, light));
    }

    private static string ScaleColourDefault(EditingSettings ed, bool light) => Palette.Hex(FretboardSettingsBinding.ColourKey(ed.ScaleHighlightColour), light);

    /// <summary>The colour the custom picker shows: the stored custom colour, else the colour the named choice gives now (so Custom never draws grey).</summary>
    private static string ShownScaleCustom(EditingSettings ed, bool light) =>
        ColourHex.TryParse(ed.ScaleHighlightCustomColour, out var colour) ? ColourHex.Format(colour.R, colour.G, colour.B) : ScaleColourDefault(ed, light);

    private void SetScaleColour(EditingSettings ed, string value)
    {
        if (value == "Custom" && !ColourHex.TryParse(ed.ScaleHighlightCustomColour, out _))
        {
            var light = ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
            ed.ScaleHighlightCustomColour = ScaleColourDefault(ed, light);   // start from the colour on the board now
        }
        ed.ScaleHighlightColour = value;
    }

    private static void StoreScaleCustom(EditingSettings ed, string? picked)
    {
        if (!ColourHex.TryParse(picked, out var colour)) return;
        ed.ScaleHighlightCustomColour = ColourHex.Format(colour.R, colour.G, colour.B);
        ed.ScaleHighlightColour = "Custom";
    }

    private static string FormatLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return label;
        var lower = label.Trim().ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private void Chips(global::Avalonia.Controls.Controls rows, string label, string prefix, IReadOnlyList<string> values, Func<string> get, Action<string> set, string? disabled = null, string? description = null, bool last = false)
    {
        var current = get();
        var choices = values.Where(v => v != disabled || string.Equals(v, current, StringComparison.OrdinalIgnoreCase)).ToList();
        var combo = new ComboBox
        {
            Name = prefix,
            ItemsSource = choices,
            SelectedItem = choices.FirstOrDefault(v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault(),
            MinWidth = 190,
        };
        AutomationProperties.SetName(combo, label.ToLowerInvariant());
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string picked && picked != disabled && !string.Equals(picked, get(), StringComparison.OrdinalIgnoreCase))
            {
                set(picked);
                Changed();
            }
        };
        rows.Add(SettingsWindowChrome.CreateSettingRow(FormatLabel(label), combo, description, showDivider: !last));
    }

    private void Switch(global::Avalonia.Controls.Controls rows, string name, string text, bool on, Action<bool> set, string? description = null, bool last = false)
    {
        var box = new CheckBox { Name = name, IsChecked = on };
        AutomationProperties.SetName(box, text);
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Changed(rebuild: false); };
        rows.Add(SettingsWindowChrome.CreateSettingRow(text, box, description, showDivider: !last));
    }

    /// <summary>A whole-number setting on the track list's slider: the value shows on the thumb, a double-click returns it to its default, and the arrow keys step it.</summary>
    private void Slide(global::Avalonia.Controls.Controls rows, string label, string name, int min, int max, int step, Func<EditingSettings, int> get, Action<int> set, string unit = "", string? description = null, bool last = false)
    {
        var title = unit.Length > 0 ? $"{FormatLabel(label)} ({unit})" : FormatLabel(label);
        var slider = new MiniSlider(min, max, get(new EditingSettings()), global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.FretboardSliderWidth, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.FretboardSliderHeight)
        {
            Name = name, Step = step, Focusable = true, Value = get(_settings.Editing),
        };
        AutomationProperties.SetName(slider, label.ToLowerInvariant());
        slider.ValueChanged += value => { set(value); Changed(rebuild: false); };
        rows.Add(SettingsWindowChrome.CreateSettingRow(title, slider, description, showDivider: !last));
    }

    /// <summary>Writes the defaults of one page back (the Look page back to the Rosewood preset) and saves.</summary>
    internal void ResetSection(int section)
    {
        CopySection(section, new EditingSettings(), new AppearanceSettings());
        if (section == 0)
        {
            // The Look page goes back to the shown theme's defaults (Rosewood at 75% dark, Maple at 90% light).
            FretboardStyles.ResetToTheme(_settings.Editing, ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light);
        }
        Changed();
    }

    /// <summary>Every page back to its defaults at once (the Look page to the shown theme's); Cancel still returns to how they were.</summary>
    internal void ResetAll()
    {
        for (var section = 0; section < SectionCount; section++) CopySection(section, new EditingSettings(), new AppearanceSettings());
        FretboardStyles.ResetToTheme(_settings.Editing, ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light);
        Changed();
    }

    /// <summary>Writes one page's settings from the given values to the live settings.</summary>
    private void CopySection(int section, EditingSettings d, AppearanceSettings da)
    {
        var ed = _settings.Editing;
        var ap = _settings.Appearance;
        switch (section)
        {
            case 0:
                FretboardStyles.Apply(ed, FretboardStyles.LookOf(d));
                (ed.FretMarkerBrightness, ed.FretboardBrightness) = (d.FretMarkerBrightness, d.FretboardBrightness);
                break;
            case 1: (ed.PreviewNotesEnabled, ed.PreviewHorizon, ed.PreviewPreviousNotesEnabled, ed.ShowNoteNames, ed.FretboardNoteBrightness, ed.FretMarkerSizePercent, ed.FretNumberSize) =
                    (d.PreviewNotesEnabled, d.PreviewHorizon, d.PreviewPreviousNotesEnabled, d.ShowNoteNames, d.FretboardNoteBrightness, d.FretMarkerSizePercent, d.FretNumberSize);
                (ed.FretNowColour, ed.FretNextColour, ed.FretUpcomingColour, ed.FretDotStyle, ed.FretLegendMinimised) = (d.FretNowColour, d.FretNextColour, d.FretUpcomingColour, d.FretDotStyle, d.FretLegendMinimised); break;
            case 2: (ed.FretboardFrets, ed.LeftHanded, ed.FretStringSpacing, ap.FretboardPosition, ap.FretboardAtBottom, ap.LockInstrumentSize) =
                    (d.FretboardFrets, d.LeftHanded, d.FretStringSpacing, da.FretboardPosition, da.FretboardAtBottom, da.LockInstrumentSize); break;
            case 3: (ed.FretboardStringLook, ed.FretboardCapoLook, ed.FretboardVibration, ed.FretboardVibrationSpeed, ed.FretboardStringNumbers) =
                    (d.FretboardStringLook, d.FretboardCapoLook, d.FretboardVibration, d.FretboardVibrationSpeed, d.FretboardStringNumbers); break;
            default: (ed.ScaleHighlightStyle, ed.ScaleHighlightColour, ed.ScaleHighlightCustomColour, ed.ScaleHighlightOpacity, ed.ScaleEmphasiseRoot, ed.ScaleDimOutside, ed.ScaleOnlyInView) = (d.ScaleHighlightStyle, d.ScaleHighlightColour, d.ScaleHighlightCustomColour, d.ScaleHighlightOpacity, d.ScaleEmphasiseRoot, d.ScaleDimOutside, d.ScaleOnlyInView); break;
        }
    }
}
