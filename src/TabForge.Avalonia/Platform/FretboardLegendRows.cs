using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Views;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the legend's clickable now, next and upcoming rows: one full-width row target over each drawn row (dot and label, placed from
//   InstrumentPanelView.LegendRowBounds), its look (no fill at rest; a soft shade with a hairline edge on hover, a stronger shade while pressed,
//   switched at once as the Scales and Modify buttons do; the pointer shape never changes), and the click that opens the shared colour picker
//   (ColourPickerPopup) under that row. Each colour the open picker shows previews on the panel at once; the answer is stored through
//   FretNoteColours and saved, so the dots, the legend and the Fretboard settings card all read it.
// Does not own: drawing the legend's dots and labels (InstrumentPanelView.Fretboard), the colours' defaults and storage rules
//   (FretNoteColours), the picker (ColourPickerPopup), the Scales and Modify buttons (InstrumentPanelStrip) or saving (the host's callback).
// Tests: TabForge.Avalonia.Tests.FretboardLegendRowsTests.
// Threads: UI thread only.
internal sealed class FretboardLegendRows
{
    private readonly InstrumentPanelView _panel;
    private readonly Dictionary<FretNoteRole, Border> _rows = new();

    /// <summary>The settings the rows read and write and the save that tells every observer; null leaves the rows hidden.</summary>
    internal (Func<AppSettings?> Settings, Action Save)? Store { get; set; }

    public FretboardLegendRows(InstrumentPanelView panel, Canvas layer)
    {
        _panel = panel;
        foreach (var role in FretNoteColours.Roles)
        {
            var row = Row(role);
            _rows[role] = row;
            layer.Children.Add(row);
        }
    }

    /// <summary>The row target of a role, for tests.</summary>
    internal Border RowOf(FretNoteRole role) => _rows[role];

    /// <summary>Lays each row over its drawn legend row; a role the legend does not list now (no look-ahead) or a panel without a legend hides it.</summary>
    public void Place()
    {
        foreach (var (role, row) in _rows)
        {
            var bounds = Store is null ? null : _panel.LegendRowBounds(role);
            row.IsVisible = bounds is not null;
            if (bounds is not { } r) continue;
            Canvas.SetLeft(row, r.X);
            Canvas.SetTop(row, r.Y);
            row.Width = r.Width;
            row.Height = r.Height;
        }
    }

    private Border Row(FretNoteRole role)
    {
        var row = new Border
        {
            Name = "FretboardLegendRow" + FretNoteColours.Name(role), IsVisible = false,
            Background = AvaloniaPalette.TransparentBrush, BorderBrush = AvaloniaPalette.TransparentBrush,
            BorderThickness = FretboardChromeMetrics.PillBorder, CornerRadius = new CornerRadius(FretboardChromeMetrics.PillRadius),
        };
        global::Avalonia.Automation.AutomationProperties.SetName(row, FretNoteColours.Name(role) + " note colour");
        ToolTip.SetTip(row, "Choose the " + FretNoteColours.Name(role).ToLowerInvariant() + " note colour");
        bool over = false, held = false;
        void Paint()
        {
            var light = row.ActualThemeVariant == ThemeVariant.Light;
            // Translucent shades, so the drawn dot and label stay readable under the row.
            row.Background = held ? new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(HoverShade.PressPaletteKey, light).WithOpacity(HoverShade.OverlayOpacity)))
                : over ? new SolidColorBrush(AvaloniaPalette.ColorOf(HoverShade.Overlay(light))) : AvaloniaPalette.TransparentBrush;
            row.BorderBrush = (over || held) && row.TryFindResource("BorderSoftBrush", row.ActualThemeVariant, out var edge) && edge is IBrush hairline ? hairline : AvaloniaPalette.TransparentBrush;
        }
        row.PointerEntered += (_, _) => { over = true; Paint(); };
        row.PointerExited += (_, _) => { over = false; held = false; Paint(); };
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            held = true;
            Paint();
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            if (!held) return;
            held = false;
            Paint();
            e.Handled = true;
            if (over) Open(role, row);   // a click: pressed and let go over the row
        };
        row.PointerCaptureLost += (_, _) => { if (held) { held = false; Paint(); } };
        return row;
    }

    /// <summary>Opens the colour picker for <paramref name="role"/> under its row (a click, or a test).</summary>
    internal void Open(FretNoteRole role, Border row) =>
        UiTasks.Forget(ChooseAsync(role, row), "Opening the note colour picker", text => Trace.Error(Trace.Ui, text));

    private async System.Threading.Tasks.Task ChooseAsync(FretNoteRole role, Border row)
    {
        if (Store is not { } store || store.Settings() is not { } settings) return;
        var light = row.ActualThemeVariant == ThemeVariant.Light;
        var stored = FretNoteColours.Get(settings.Editing, role);
        var picker = new ColourPickerPopup(FretNoteColours.Shown(role, stored, light), FretNoteColours.DefaultHex(role, light), settings.Appearance.RecentColours);
        picker.Applied += hex => Preview(role, hex);   // a live look on the panel only; nothing is stored until the answer
        var answer = await picker.AskAsync(row);       // under the row
        FretNoteColours.Set(settings.Editing, role, answer ?? stored);
        store.Save();   // the settings observers re-read the look, which also ends the preview
    }

    private void Preview(FretNoteRole role, string hex)
    {
        var colour = FretboardSettingsBinding.CustomColour("Custom", hex);
        var look = _panel.Look;
        _panel.Look = role switch
        {
            FretNoteRole.Now => look with { NowColour = colour },
            FretNoteRole.Next => look with { NextColour = colour },
            _ => look with { UpcomingColour = colour },
        };
    }
}
