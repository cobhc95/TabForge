using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the five pages of the Fretboard settings window, their cards and the small row builders they share (a chip row, a switch, a slider). Each row names
//   its setting; picking writes it and asks the window to save and redraw. Reset section writes that page's defaults back; Cancel writes the opening values back.
// Does not own: the window frame, the preview and saving (FretboardSettingsWindow.cs) or the setting types.
// Tests: TabForge.Avalonia.Tests.FretboardSettingsWindowTests.
public sealed partial class FretboardSettingsWindow
{
    private void ShowSection(int section)
    {
        var panel = new StackPanel { Spacing = FretboardChromeMetrics.PageCardGap }.WithMargin(16, 12, 16, 12);
        var title = new TextBlock { Text = SectionName(Math.Clamp(section, 0, SectionCount - 1)), FontSize = FretboardChromeMetrics.PageTitleSize, FontWeight = global::Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
        var reset = new Button { Name = "FretboardResetSection", Content = "Reset section" };
        reset.Classes.Add("PrefsActionReset");
        reset.Click += (_, _) => ResetSection(_nav.SelectedIndex);
        DockPanel.SetDock(reset, Dock.Right);
        panel.Children.Add(new DockPanel { Children = { reset, title } });
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
                var preset = Card("Preset");
                Chips(preset, "LOOK", "FretboardLook", FretboardStyles.PresetNames, () => FretboardStyles.PresetOf(FretboardStyles.LookOf(ed)), v => FretboardStyles.Apply(ed, FretboardStyles.OfPreset(v)), disabled: FretboardStyles.Custom);
                var wood = Card("Wood and binding");
                Chips(wood, "WOOD", "FretboardWood", FretboardStyles.Woods, () => ed.FretboardWood, v => ed.FretboardWood = v);
                Chips(wood, "BINDING", "FretboardBinding", FretboardStyles.Bindings, () => ed.FretboardBinding, v => ed.FretboardBinding = v);
                var inlays = Card("Inlays");
                Chips(inlays, "INLAY SHAPE", "FretboardInlayShape", FretboardStyles.InlayShapes, () => ed.FretboardInlayShape, v => ed.FretboardInlayShape = v);
                Chips(inlays, "INLAY MATERIAL", "FretboardInlayMaterial", FretboardStyles.InlayMaterials, () => ed.FretboardInlayMaterial, v => ed.FretboardInlayMaterial = v);
                Chips(inlays, "POSITION INLAY COLOUR", "FretboardMarkerColour", FretMarkerLevels.Colours, () => ed.FretMarkerColour, v => ed.FretMarkerColour = v);
                Chips(inlays, "POSITION INLAY BRIGHTNESS", "FretboardMarkerBrightness", FretMarkerLevels.All, () => ed.FretMarkerBrightness, v => ed.FretMarkerBrightness = v);
                inlays.Add(new TextBlock { Text = "Position inlays mark frets 3, 5, 7, 9, 12…; playing-note brightness is under Notes and markers.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
                Slide(Card("Board"), "FRETBOARD BRIGHTNESS", "FretboardBrightness", FretboardStyles.MinBrightness, FretboardStyles.MaxBrightness, FretboardStyles.BrightnessStep, e => e.FretboardBrightness, v => ed.FretboardBrightness = v, "%");
                break;
            case 1:
                var ahead = Card("Look-ahead");
                Switch(ahead, "FretboardPreviewNext", "Show look-ahead notes", ed.PreviewNotesEnabled, v => ed.PreviewNotesEnabled = v);
                Slide(ahead, "NEXT NOTES SHOWN AHEAD", "FretboardHorizon", 1, 10, 1, e => e.PreviewHorizon, v => ed.PreviewHorizon = v);
                Switch(ahead, "FretboardPreviewPrevious", "Show previous notes (a faint trail)", ed.PreviewPreviousNotesEnabled, v => ed.PreviewPreviousNotesEnabled = v);
                var dots = Card("Note dots");
                Chips(dots, "NOTE DOT LABEL", "FretboardDotSays", new[] { "Fret number", "Note name" }, () => ed.ShowNoteNames ? "Note name" : "Fret number", v => ed.ShowNoteNames = v == "Note name");
                dots.Add(new TextBlock { Text = "Labels cursor, playback and pointer-preview notes; the fret ruler and scale labels stay unchanged.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
                Slide(dots, "NOTE BRIGHTNESS", "FretboardNoteBrightness", FretboardStyles.MinBrightness, FretboardStyles.MaxBrightness, FretboardStyles.BrightnessStep, e => e.FretboardNoteBrightness, v => ed.FretboardNoteBrightness = v, "%");
                Slide(dots, "DOT AND NUMBER SIZE", "FretboardMarkerSize", 60, 160, 10, e => e.FretMarkerSizePercent, v => ed.FretMarkerSizePercent = v, "%");
                Chips(dots, "NUMBER SIZE", "FretboardNumberSize", FretNumberSizes.All, () => ed.FretNumberSize, v => ed.FretNumberSize = v);
                break;
            case 2:
                var board = Card("Board");
                Chips(board, "FRETS SHOWN", "FretboardFrets", new[] { "12", "24" }, () => ed.FretboardFrets.ToString(), v => ed.FretboardFrets = int.Parse(v));
                Chips(board, "STRING SPACING", "FretboardSpacing", FretStringSpacings.All, () => ed.FretStringSpacing, v => ed.FretStringSpacing = v);
                Switch(board, "FretboardLeftHanded", "Left-handed", ed.LeftHanded, v => ed.LeftHanded = v);
                var place = Card("Placement");
                Chips(place, "BOARD POSITION", "FretboardPosition", new[] { "Left", "Centre", "Right" }, () => ap.FretboardPosition, v => ap.FretboardPosition = v);
                Chips(place, "DOCK", "FretboardDock", new[] { "Top", "Bottom" }, () => ap.FretboardAtBottom ? "Bottom" : "Top", v => ap.FretboardAtBottom = v == "Bottom");
                Switch(place, "FretboardLockSize", "Lock fretboard size", ap.LockInstrumentSize, v => ap.LockInstrumentSize = v);
                break;
            case 3:
                var strings = Card("Strings");
                Chips(strings, "STRING FINISH", "FretboardStringLook", FretboardStyles.StringLooks, () => ed.FretboardStringLook, v => ed.FretboardStringLook = v);
                Chips(strings, "STRING VIBRATION", "FretboardVibration", FretboardStyles.VibrationLevels, () => ed.FretboardVibration, v => ed.FretboardVibration = v);
                Chips(strings, "VIBRATION SPEED", "FretboardVibrationSpeed", FretboardStyles.VibrationSpeeds, () => ed.FretboardVibrationSpeed, v => ed.FretboardVibrationSpeed = v);
                Switch(strings, "FretboardStringNumbers", "Show string numbers", ed.FretboardStringNumbers, v => ed.FretboardStringNumbers = v);
                Chips(Card("Capo"), "CAPO FINISH", "FretboardCapoLook", FretboardStyles.CapoLooks, () => ed.FretboardCapoLook, v => ed.FretboardCapoLook = v);
                break;
            default:
                var highlight = Card("Highlight");
                Chips(highlight, "STYLE", "ScaleStyle", ScaleHighlightStyles.All, () => ScaleHighlightStyles.Normalize(ed.ScaleHighlightStyle), v => ed.ScaleHighlightStyle = v);
                Switch(highlight, "ScaleEmphasiseRoot", "Emphasise the root", ed.ScaleEmphasiseRoot, v => ed.ScaleEmphasiseRoot = v);
                Switch(highlight, "ScaleDimOutside", "Dim notes outside the scale", ed.ScaleDimOutside, v => ed.ScaleDimOutside = v);
                Switch(highlight, "ScaleOnlyInView", "Only on the frets in view", ed.ScaleOnlyInView, v => ed.ScaleOnlyInView = v);
                var clear = new Button { Name = "ScaleClearHighlight", Content = "Clear highlight" }.WithMargin(0, 12, 0, 0);
                clear.Click += (_, _) => { ed.ScaleHighlight = null; Changed(); };
                highlight.Add(clear);
                highlight.Add(new TextBlock { Text = "The scale itself and its colours are chosen with the Scales button on the fretboard.", FontSize = FretboardChromeMetrics.PreviewLabelSize }.WithMargin(0, 10, 0, 0));
                break;
        }
        _page.Content = panel;
    }

    private void Chips(global::Avalonia.Controls.Controls rows, string label, string prefix, IReadOnlyList<string> values, Func<string> get, Action<string> set, string? disabled = null)
    {
        rows.Add(ScalePopupLayout.SectionLabel(label));
        var current = get();
        var wrap = new WrapPanel();
        foreach (var value in values)
        {
            var chip = ScalePopupLayout.Chip(prefix + value.Replace(" ", ""), value, string.Equals(value, current, StringComparison.OrdinalIgnoreCase));
            AutomationProperties.SetName(chip, label.ToLowerInvariant() + ": " + value);
            chip.IsEnabled = value != disabled;
            chip.Click += (_, _) => { set(value); Changed(); };
            wrap.Children.Add(chip);
        }
        rows.Add(wrap);
    }

    private void Switch(global::Avalonia.Controls.Controls rows, string name, string text, bool on, Action<bool> set)
    {
        var box = new CheckBox { Name = name, Content = text, IsChecked = on }.WithMargin(0, 10, 0, 0);
        AutomationProperties.SetName(box, text);
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Changed(rebuild: false); };
        rows.Add(box);
    }

    /// <summary>A whole-number setting on the track list's slider: the value shows on the thumb, a double-click returns it to its default, and the arrow keys step it.</summary>
    private void Slide(global::Avalonia.Controls.Controls rows, string label, string name, int min, int max, int step, Func<EditingSettings, int> get, Action<int> set, string unit = "")
    {
        rows.Add(ScalePopupLayout.SectionLabel(unit.Length > 0 ? $"{label} ({unit})" : label));
        var slider = new MiniSlider(min, max, get(new EditingSettings()), global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.FretboardSliderWidth, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.FretboardSliderHeight)
        {
            Name = name, Step = step, Focusable = true, Value = get(_settings.Editing), HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(slider, label.ToLowerInvariant());
        slider.ValueChanged += value => { set(value); Changed(rebuild: false); };
        rows.Add(slider);
    }

    /// <summary>Writes the defaults of one page back (the Look page back to the Rosewood preset) and saves.</summary>
    internal void ResetSection(int section)
    {
        CopySection(section, new EditingSettings(), new AppearanceSettings());
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
                (ed.FretMarkerColour, ed.FretMarkerBrightness, ed.FretboardBrightness) = (d.FretMarkerColour, d.FretMarkerBrightness, d.FretboardBrightness);
                break;
            case 1: (ed.PreviewNotesEnabled, ed.PreviewHorizon, ed.PreviewPreviousNotesEnabled, ed.ShowNoteNames, ed.FretboardNoteBrightness, ed.FretMarkerSizePercent, ed.FretNumberSize) =
                    (d.PreviewNotesEnabled, d.PreviewHorizon, d.PreviewPreviousNotesEnabled, d.ShowNoteNames, d.FretboardNoteBrightness, d.FretMarkerSizePercent, d.FretNumberSize); break;
            case 2: (ed.FretboardFrets, ed.LeftHanded, ed.FretStringSpacing, ap.FretboardPosition, ap.FretboardAtBottom, ap.LockInstrumentSize) =
                    (d.FretboardFrets, d.LeftHanded, d.FretStringSpacing, da.FretboardPosition, da.FretboardAtBottom, da.LockInstrumentSize); break;
            case 3: (ed.FretboardStringLook, ed.FretboardCapoLook, ed.FretboardVibration, ed.FretboardVibrationSpeed, ed.FretboardStringNumbers) =
                    (d.FretboardStringLook, d.FretboardCapoLook, d.FretboardVibration, d.FretboardVibrationSpeed, d.FretboardStringNumbers); break;
            default: (ed.ScaleHighlightStyle, ed.ScaleEmphasiseRoot, ed.ScaleDimOutside, ed.ScaleOnlyInView) = (d.ScaleHighlightStyle, d.ScaleEmphasiseRoot, d.ScaleDimOutside, d.ScaleOnlyInView); break;
        }
    }
}
