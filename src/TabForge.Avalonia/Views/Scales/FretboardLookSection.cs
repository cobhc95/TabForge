using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Services;
using TabForge.Avalonia.Views;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the FRETBOARD LOOK section of the fretboard popup: show the scale on / off, the style chips, the colour swatches (Custom… opens the colour picker),
//   the opacity slider, the extras and the Clear highlight / Done buttons. Every change is written to the editing settings and applies live to the fretboard and keyboard.
// Does not own: the drawing (InstrumentPanelView), the settings types (EditingSettings, ScaleHighlightStyles).
// Tests: TabForge.Avalonia.Tests.ScaleLookPopupTests.
// Threads: UI thread only.
public sealed class FretboardLookSection
{
    private readonly IScalePanelHost _host;
    private readonly EditingSettings _ed;
    private readonly Action _close, _clearHighlight, _lookChanged;
    private bool _building;

    /// <param name="host">The host whose scale settings the look edits (the show on / off).</param>
    /// <param name="editing">The editing settings that hold the fretboard look.</param>
    /// <param name="close">Closes the panel (Done).</param>
    /// <param name="clearHighlight">Clears the highlight and switches the display off.</param>
    /// <param name="lookChanged">Saves after a look change; the fretboard redraws.</param>
    public FretboardLookSection(IScalePanelHost host, EditingSettings editing, Action close, Action clearHighlight, Action lookChanged)
        => (_host, _ed, _close, _clearHighlight, _lookChanged) = (host, editing, close, clearHighlight, lookChanged);

    private const string ShowLabel = "SHOW THE SCALE", StyleLabel = "STYLE", ColourLabel = "COLOUR", OpacityLabel = "OPACITY", ExtrasLabel = "EXTRAS";
    private const string ClearText = "Clear highlight", DoneText = "Done";

    /// <summary>Builds the look rows (the panel prints the FRETBOARD LOOK title above).</summary>
    public StackPanel Build()
    {
        var panel = new StackPanel();
        var rows = panel.Children;
        var ed = _ed;
        AddShowToggle(rows, ed);
        AddExclusive(rows, StyleLabel, "ScaleStyle", ScaleHighlightStyles.All, ScaleHighlightStyles.Normalize(ed.ScaleHighlightStyle), v => ed.ScaleHighlightStyle = v, "Scale style: ");
        AddColours(rows, ed);
        AddOpacity(rows, ed);
        rows.Add(ScalePopupLayout.SectionLabel(ExtrasLabel));
        var extras = new WrapPanel();
        extras.Children.Add(Toggle("ScaleEmphasiseRoot", "Emphasise the root", ed.ScaleEmphasiseRoot, v => ed.ScaleEmphasiseRoot = v));
        extras.Children.Add(Toggle("ScaleDimOutside", "Dim notes outside the scale", ed.ScaleDimOutside, v => ed.ScaleDimOutside = v));
        extras.Children.Add(Toggle("ScaleOnlyInView", "Only on the frets in view", ed.ScaleOnlyInView, v => ed.ScaleOnlyInView = v));
        rows.Add(extras);
        var clear = new Button { Name = "ScaleClearHighlight", Content = ClearText };
        var done = new Button { Name = "ScaleDone", Content = DoneText };
        AutomationProperties.SetName(clear, ClearText);
        AutomationProperties.SetName(done, DoneText);
        clear.Click += (_, _) => _clearHighlight();
        done.Click += (_, _) => _close();
        rows.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = ScalePopupLayout.ButtonGap, Children = { clear, done } }.WithMargin(ScalePopupLayout.ButtonRowMargin));
        return panel;
    }

    private void AddShowToggle(global::Avalonia.Controls.Controls rows, EditingSettings ed)
    {
        rows.Add(ScalePopupLayout.SectionLabel(ShowLabel));
        var on = ScalePopupLayout.Segment("ScaleShowOn", "On", _host.Settings.ShowOnFretboard);
        var off = ScalePopupLayout.Segment("ScaleShowOff", "Off", !_host.Settings.ShowOnFretboard);
        AutomationProperties.SetName(on, "Show the scale on the fretboard: On");
        AutomationProperties.SetName(off, "Show the scale on the fretboard: Off");
        void Choose(ToggleButton picked, ToggleButton other, bool value)
        {
            if (_building) return;
            if (picked.IsChecked != true) { picked.IsChecked = true; return; }
            _building = true;
            other.IsChecked = false;
            _building = false;
            _host.Settings.ShowOnFretboard = value;
            if (!value) ed.ScaleHighlight = null;
            _host.SettingChanged();
        }
        on.IsCheckedChanged += (_, _) => Choose(on, off, true);
        off.IsCheckedChanged += (_, _) => Choose(off, on, false);
        rows.Add(ScalePopupLayout.SegmentRow(new[] { on, off }));
    }

    /// <summary>A row of chips of which exactly one is on; picking one stores its value and turns the others off.</summary>
    private void AddExclusive(global::Avalonia.Controls.Controls rows, string label, string prefix, IReadOnlyList<string> values, string current, Action<string> store, string nameStart)
    {
        rows.Add(ScalePopupLayout.SectionLabel(label));
        var buttons = values.Select(v => ScalePopupLayout.Chip(prefix + v.Replace(" ", ""), v, v == current)).ToList();
        for (var i = 0; i < buttons.Count; i++)
        {
            var (value, button) = (values[i], buttons[i]);
            AutomationProperties.SetName(button, nameStart + value);
            button.IsCheckedChanged += (_, _) =>
            {
                if (_building) return;
                if (button.IsChecked != true) { button.IsChecked = true; return; }   // one stays on
                _building = true;
                foreach (var other in buttons) if (other != button) other.IsChecked = false;
                _building = false;
                store(value);
                _lookChanged();
            };
        }
        var wrap = new WrapPanel();
        foreach (var b in buttons) wrap.Children.Add(b);
        rows.Add(wrap);
    }

    private void AddColours(global::Avalonia.Controls.Controls rows, EditingSettings ed)
    {
        rows.Add(ScalePopupLayout.SectionLabel(ColourLabel));
        var names = ScaleHighlightStyles.Swatches;
        var current = ed.ScaleHighlightColour is { } c && names.Contains(c) ? c : "Accent";
        var buttons = new List<ToggleButton>();
        var wrap = new WrapPanel();
        foreach (var name in names)
        {
            var brushKey = FretboardSettingsBinding.ColourKey(name);
            var brushName = brushKey == "Accent" ? "AccentBrush" : brushKey;
            global::Avalonia.Media.IBrush? customFill = null;
            var label = "Scale colour: " + name;
            if (name == "Custom")
            {
                label = "Scale colour: Custom (pick a colour)";
                if (ColourHex.TryParse(ed.ScaleHighlightCustomColour, out var rgba))
                    customFill = ScalePopupLayout.CustomFill(rgba.R, rgba.G, rgba.B);
            }
            var button = ScalePopupLayout.Swatch("ScaleColour" + name.Replace(" ", ""), label, name == current, brushName, customFill);
            var captured = name;
            button.IsCheckedChanged += (_, _) =>
            {
                if (_building) return;
                if (button.IsChecked != true) { if (captured == current) button.IsChecked = true; return; }
                if (captured == "Custom")
                {
                    _building = true;
                    foreach (var other in buttons) if (other != button) other.IsChecked = false;
                    _building = false;
                    OpenCustomColour(ed, buttons, current);
                    return;
                }
                _building = true;
                foreach (var other in buttons) if (other != button) other.IsChecked = false;
                _building = false;
                current = captured;
                ed.ScaleHighlightColour = captured;
                _lookChanged();
            };
            buttons.Add(button);
            wrap.Children.Add(button);
        }
        rows.Add(wrap);
    }

    private void OpenCustomColour(EditingSettings ed, List<ToggleButton> buttons, string previous)
    {
        _ = PickCustomColourAsync(ed, buttons, previous);
    }

    private async System.Threading.Tasks.Task PickCustomColourAsync(EditingSettings ed, List<ToggleButton> buttons, string? previous = null)
    {
        var anchor = buttons.FirstOrDefault(b => b.Name == "ScaleColourCustom") ?? buttons.Last();
        var start = ColourHex.TryParse(ed.ScaleHighlightCustomColour, out var rgba) ? rgba : default;
        var picked = await ColourPickerPopup.PickRgbaAsync(anchor, start, new List<string>());
        if (picked is not { } p)
        {
            _building = true;
            foreach (var other in buttons) other.IsChecked = other.Name == "ScaleColour" + (previous ?? ed.ScaleHighlightColour).Replace(" ", "");
            _building = false;
            return;
        }
        ed.ScaleHighlightCustomColour = $"#{p.R:X2}{p.G:X2}{p.B:X2}";
        ed.ScaleHighlightColour = "Custom";
        _building = true;
        foreach (var other in buttons) other.IsChecked = other.Name == "ScaleColourCustom";
        if (anchor.Content is Border dot) dot.Background = ScalePopupLayout.CustomFill(p.R, p.G, p.B);
        _building = false;
        _lookChanged();
    }

    private void AddOpacity(global::Avalonia.Controls.Controls rows, EditingSettings ed)
    {
        rows.Add(ScalePopupLayout.SectionLabel(OpacityLabel));
        var amount = new TextBlock { Text = ed.ScaleHighlightOpacity + "%", VerticalAlignment = VerticalAlignment.Center, MinWidth = ScalePopupLayout.PercentSpan };
        var slider = new Slider
        {
            Name = "ScaleOpacity", Minimum = ScaleHighlightStyles.MinOpacity, Maximum = ScaleHighlightStyles.MaxOpacity, Value = ed.ScaleHighlightOpacity,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, "Scale opacity");
        var coalesced = new SliderChangeCoalescer(() => _lookChanged());
        slider.ValueChanged += (_, e) =>
        {
            ed.ScaleHighlightOpacity = (int)Math.Round(e.NewValue);
            amount.Text = ed.ScaleHighlightOpacity + "%";
            coalesced.SliderChanged();
        };
        slider.PointerReleased += (_, _) => coalesced.Flush();
        DockPanel.SetDock(amount, Dock.Right);
        rows.Add(new DockPanel { Children = { amount, slider } });
    }

    private ToggleButton Toggle(string name, string text, bool on, Action<bool> store)
    {
        var chip = ScalePopupLayout.Chip(name, text, on);
        AutomationProperties.SetName(chip, text);
        chip.IsCheckedChanged += (_, _) =>
        {
            if (_building) return;
            store(chip.IsChecked == true);
            _lookChanged();
        };
        return chip;
    }
}
