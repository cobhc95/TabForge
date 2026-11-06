using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Views;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the legend's clickable now, next and upcoming dots: one small hit target over each drawn dot (placed from InstrumentPanelView's
//   anchors), its hover state (a soft shade disc that fades in, the HoverShade colour; the pointer shape never changes), and the click that opens
//   the shared colour picker (ColourPickerPopup) on that dot. Each colour the open picker shows previews on the panel at once; the answer is
//   stored through FretNoteColours and saved, so the dots, the legend and the Fretboard settings card all read it.
// Does not own: drawing the legend (InstrumentPanelView.Fretboard), the colours' defaults and storage rules (FretNoteColours), the picker
//   (ColourPickerPopup) or saving (the host's save callback).
// Tests: TabForge.Avalonia.Tests.FretNoteColoursTests.
// Threads: UI thread only.
internal sealed class FretboardLegendDots
{
    /// <summary>The hit target's side over a drawn dot (DIPs), the hover disc's share of it and its fade (ms).</summary>
    public const double HitSize = 16, HoverFadeMs = 120;

    private readonly InstrumentPanelView _panel;
    private readonly Dictionary<FretNoteRole, Border> _dots = new();

    /// <summary>The settings the dots read and write and the save that tells every observer; null leaves the dots hidden.</summary>
    internal (Func<AppSettings?> Settings, Action Save)? Store { get; set; }

    public FretboardLegendDots(InstrumentPanelView panel, Canvas layer)
    {
        _panel = panel;
        foreach (var role in FretNoteColours.Roles)
        {
            var dot = Dot(role);
            _dots[role] = dot;
            layer.Children.Add(dot);
        }
    }

    /// <summary>Puts each dot over its drawn legend dot; a role the legend does not list now (no look-ahead) or a panel without a legend hides it.</summary>
    public void Place()
    {
        foreach (var (role, dot) in _dots)
        {
            var anchor = Store is null ? null : _panel.LegendDotAnchor(role);
            dot.IsVisible = anchor is not null;
            if (anchor is not { } at) continue;
            Canvas.SetLeft(dot, at.X - HitSize / 2);
            Canvas.SetTop(dot, at.Y - HitSize / 2);
        }
    }

    private Border Dot(FretNoteRole role)
    {
        var shade = new Ellipse
        {
            Width = HitSize, Height = HitSize, Opacity = 0, IsHitTestVisible = false,
            Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(HoverFadeMs) } },
        };
        var dot = new Border
        {
            Name = "FretboardLegendDot" + FretNoteColours.Name(role), Width = HitSize, Height = HitSize,
            Background = AvaloniaPalette.TransparentBrush, Child = shade, IsVisible = false,
        };
        global::Avalonia.Automation.AutomationProperties.SetName(dot, FretNoteColours.Name(role) + " note colour");
        ToolTip.SetTip(dot, "Choose the " + FretNoteColours.Name(role).ToLowerInvariant() + " note colour");
        HoverShade.Attach(dot, hovered =>
        {
            if (hovered) shade.Fill = HoverShade.Brush(dot);
            shade.Opacity = hovered ? 1 : 0;
        });
        dot.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(dot).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            UiTasks.Forget(ChooseAsync(role, dot), "Opening the note colour picker", text => Trace.Error(Trace.Ui, text));
        };
        return dot;
    }

    private async System.Threading.Tasks.Task ChooseAsync(FretNoteRole role, Border dot)
    {
        if (Store is not { } store || store.Settings() is not { } settings) return;
        var light = dot.ActualThemeVariant == ThemeVariant.Light;
        var stored = FretNoteColours.Get(settings.Editing, role);
        var picker = new ColourPickerPopup(FretNoteColours.Shown(role, stored, light), FretNoteColours.DefaultHex(role, light), settings.Appearance.RecentColours);
        picker.Applied += hex => Preview(role, hex);   // a live look on the panel only; nothing is stored until the answer
        var answer = await picker.AskAsync(dot);
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
