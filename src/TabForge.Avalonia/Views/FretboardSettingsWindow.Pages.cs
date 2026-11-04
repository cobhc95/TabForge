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

// Owns: the five pages of the Fretboard settings window and the small row builders they share (a chip row, a switch, a stepper with a type-in box). Each row names
//   its setting; picking writes it and asks the window to save and redraw. Reset section writes that page's defaults back.
// Does not own: the window frame, the preview and saving (FretboardSettingsWindow.cs) or the setting types.
// Tests: TabForge.Avalonia.Tests.FretboardSettingsWindowTests.
public sealed partial class FretboardSettingsWindow
{
    private void ShowSection(int section)
    {
        var panel = new StackPanel().WithMargin(16, 12, 16, 12);
        var rows = panel.Children;
        rows.Add(new TextBlock { Text = SectionName(Math.Clamp(section, 0, SectionCount - 1)), FontSize = FretboardChromeMetrics.PanelTitleSize, FontWeight = global::Avalonia.Media.FontWeight.SemiBold });
        var ed = _settings.Editing;
        var ap = _settings.Appearance;
        switch (section)
        {
            case 0:
                Chips(rows, "LOOK", "FretboardLook", FretboardStyles.PresetNames, () => FretboardStyles.PresetOf(FretboardStyles.LookOf(ed)), v => FretboardStyles.Apply(ed, FretboardStyles.OfPreset(v)), disabled: FretboardStyles.Custom);
                Chips(rows, "WOOD", "FretboardWood", FretboardStyles.Woods, () => ed.FretboardWood, v => ed.FretboardWood = v);
                Chips(rows, "BINDING", "FretboardBinding", FretboardStyles.Bindings, () => ed.FretboardBinding, v => ed.FretboardBinding = v);
                Chips(rows, "INLAY SHAPE", "FretboardInlayShape", FretboardStyles.InlayShapes, () => ed.FretboardInlayShape, v => ed.FretboardInlayShape = v);
                Chips(rows, "INLAY MATERIAL", "FretboardInlayMaterial", FretboardStyles.InlayMaterials, () => ed.FretboardInlayMaterial, v => ed.FretboardInlayMaterial = v);
                Chips(rows, "POSITION INLAY COLOUR", "FretboardMarkerColour", FretMarkerLevels.Colours, () => ed.FretMarkerColour, v => ed.FretMarkerColour = v);
                Chips(rows, "POSITION INLAY BRIGHTNESS", "FretboardMarkerBrightness", FretMarkerLevels.All, () => ed.FretMarkerBrightness, v => ed.FretMarkerBrightness = v);
                rows.Add(new TextBlock { Text = "Position inlays mark frets 3, 5, 7, 9, 12…; playing-note brightness is under Notes and markers.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
                Slide(rows, "FRETBOARD BRIGHTNESS", "FretboardBrightness", FretboardStyles.MinBrightness, FretboardStyles.MaxBrightness, FretboardStyles.BrightnessStep, ed.FretboardBrightness, v => ed.FretboardBrightness = (int)v, v => (int)v + "%");
                break;
            case 1:
                Switch(rows, "FretboardPreviewNext", "Show look-ahead notes", ed.PreviewNotesEnabled, v => ed.PreviewNotesEnabled = v);
                Slide(rows, "NEXT NOTES SHOWN AHEAD", "FretboardHorizon", 1, 10, 1, ed.PreviewHorizon, v => ed.PreviewHorizon = (int)v, v => ((int)v).ToString());
                Switch(rows, "FretboardPreviewPrevious", "Show previous notes (a faint trail)", ed.PreviewPreviousNotesEnabled, v => ed.PreviewPreviousNotesEnabled = v);
                Chips(rows, "NOTE DOT LABEL", "FretboardDotSays", new[] { "Fret number", "Note name" }, () => ed.ShowNoteNames ? "Note name" : "Fret number", v => ed.ShowNoteNames = v == "Note name");
                rows.Add(new TextBlock { Text = "Labels cursor, playback and pointer-preview notes; the fret ruler and scale labels stay unchanged.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
                Slide(rows, "NOTE BRIGHTNESS", "FretboardNoteBrightness", FretboardStyles.MinBrightness, FretboardStyles.MaxBrightness, FretboardStyles.BrightnessStep, ed.FretboardNoteBrightness, v => ed.FretboardNoteBrightness = (int)v, v => (int)v + "%");
                Slide(rows, "DOT AND NUMBER SIZE", "FretboardMarkerSize", 60, 160, 10, ed.FretMarkerSizePercent, v => ed.FretMarkerSizePercent = (int)v, v => (int)v + "%");
                Chips(rows, "NUMBER SIZE", "FretboardNumberSize", FretNumberSizes.All, () => ed.FretNumberSize, v => ed.FretNumberSize = v);
                break;
            case 2:
                Chips(rows, "FRETS SHOWN", "FretboardFrets", new[] { "12", "24" }, () => ed.FretboardFrets.ToString(), v => ed.FretboardFrets = int.Parse(v));
                Switch(rows, "FretboardLeftHanded", "Left-handed", ed.LeftHanded, v => ed.LeftHanded = v);
                Chips(rows, "BOARD POSITION", "FretboardPosition", new[] { "Left", "Centre", "Right" }, () => ap.FretboardPosition, v => ap.FretboardPosition = v);
                Chips(rows, "STRING SPACING", "FretboardSpacing", FretStringSpacings.All, () => ed.FretStringSpacing, v => ed.FretStringSpacing = v);
                Chips(rows, "DOCK", "FretboardDock", new[] { "Top", "Bottom" }, () => ap.FretboardAtBottom ? "Bottom" : "Top", v => ap.FretboardAtBottom = v == "Bottom");
                Switch(rows, "FretboardLockSize", "Lock fretboard size", ap.LockInstrumentSize, v => ap.LockInstrumentSize = v);
                break;
            case 3:
                Chips(rows, "STRING FINISH", "FretboardStringLook", FretboardStyles.StringLooks, () => ed.FretboardStringLook, v => ed.FretboardStringLook = v);
                Chips(rows, "CAPO FINISH", "FretboardCapoLook", FretboardStyles.CapoLooks, () => ed.FretboardCapoLook, v => ed.FretboardCapoLook = v);
                Chips(rows, "STRING VIBRATION", "FretboardVibration", FretboardStyles.VibrationLevels, () => ed.FretboardVibration, v => ed.FretboardVibration = v);
                Chips(rows, "VIBRATION SPEED", "FretboardVibrationSpeed", FretboardStyles.VibrationSpeeds, () => ed.FretboardVibrationSpeed, v => ed.FretboardVibrationSpeed = v);
                Switch(rows, "FretboardStringNumbers", "Show string numbers", ed.FretboardStringNumbers, v => ed.FretboardStringNumbers = v);
                break;
            default:
                Chips(rows, "STYLE", "ScaleStyle", ScaleHighlightStyles.All, () => ScaleHighlightStyles.Normalize(ed.ScaleHighlightStyle), v => ed.ScaleHighlightStyle = v);
                Switch(rows, "ScaleEmphasiseRoot", "Emphasise the root", ed.ScaleEmphasiseRoot, v => ed.ScaleEmphasiseRoot = v);
                Switch(rows, "ScaleDimOutside", "Dim notes outside the scale", ed.ScaleDimOutside, v => ed.ScaleDimOutside = v);
                Switch(rows, "ScaleOnlyInView", "Only on the frets in view", ed.ScaleOnlyInView, v => ed.ScaleOnlyInView = v);
                var clear = new Button { Name = "ScaleClearHighlight", Content = "Clear highlight" }.WithMargin(0, 12, 0, 0);
                clear.Click += (_, _) => { ed.ScaleHighlight = null; Changed(); };
                rows.Add(clear);
                rows.Add(new TextBlock { Text = "The scale itself and its colours are chosen with the Scales button on the fretboard.", FontSize = FretboardChromeMetrics.PreviewLabelSize }.WithMargin(0, 10, 0, 0));
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

    private void Slide(global::Avalonia.Controls.Controls rows, string label, string name, double min, double max, double step, double value, Action<double> set, Func<double, string> text)
    {
        rows.Add(ScalePopupLayout.SectionLabel(label));
        SplitNumber(text(value), out var number, out var suffix);
        var longest = 1;
        for (var v = min; v <= max + step / 2; v += step) longest = Math.Max(longest, NumberOf(text(Snap(v, min, max, step))).Length);
        var box = new TextBox { Name = name + "Box", Text = number, Width = longest * 9 + 18, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Classes = { AvaloniaFormControlStyles.PrefsFieldClass } };
        AutomationProperties.SetName(box, label.ToLowerInvariant() + " value");
        var minus = new Button { Name = name + "Minus", Content = "−", MinWidth = 0, Width = 30, Classes = { AvaloniaFormControlStyles.StepButtonClass } };
        AutomationProperties.SetName(minus, "one less " + label.ToLowerInvariant());
        var plus = new Button { Name = name + "Plus", Content = "+", MinWidth = 0, Width = 30, Classes = { AvaloniaFormControlStyles.StepButtonClass } };
        AutomationProperties.SetName(plus, "one more " + label.ToLowerInvariant());
        var current = value;
        void Apply(double next)
        {
            current = Snap(next, min, max, step);
            set(current);
            box.Text = NumberOf(text(current));
            Changed(rebuild: false);
        }
        minus.Click += (_, _) => Apply(current - step);
        plus.Click += (_, _) => Apply(current + step);
        box.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Return) ApplyBox(); };
        box.LostFocus += (_, _) => ApplyBox();
        void ApplyBox()
        {
            if (double.TryParse(box.Text, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var typed)) Apply(typed);
            else box.Text = NumberOf(text(current));
        }
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { minus, box } };
        if (suffix.Length > 0) row.Children.Add(new TextBlock { Text = suffix, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
        row.Children.Add(plus);
        rows.Add(row);
    }

    /// <summary>The value snapped to its step and clamped to its range.</summary>
    private static double Snap(double value, double min, double max, double step) =>
        Math.Clamp(min + Math.Round((value - min) / step) * step, min, max);

    /// <summary>The leading number of a formatted value ("120" of "120%").</summary>
    private static string NumberOf(string text)
    {
        SplitNumber(text, out var number, out _);
        return number;
    }

    private static void SplitNumber(string text, out string number, out string suffix)
    {
        var i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] is '.' or '-' or '+' or ',')) i++;
        number = text[0..i];
        suffix = text[i..];
    }

    /// <summary>Writes the defaults of one page back (the Look page back to the Rosewood preset) and saves.</summary>
    internal void ResetSection(int section)
    {
        var ed = _settings.Editing;
        var ap = _settings.Appearance;
        var d = new EditingSettings();
        var da = new AppearanceSettings();
        switch (section)
        {
            case 0:
                FretboardStyles.Apply(ed, FretboardStyles.DefaultLook);
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
        Changed();
    }
}
