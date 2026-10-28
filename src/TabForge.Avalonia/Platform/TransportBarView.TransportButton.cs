using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Menus;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The transport bar's drawn controls: the 1 DIP divider between the playback group and the toggles, and one button (a 34 DIP playback/mixer button
/// or a 26 x 17 zoom button) drawn through IDrawContext in the playbar colours, with its hover, pressed, lit and playing glow states.
/// Owns: the TransportButton and Divider controls.
/// Does not own: the bar's layout, wiring, lit state, tooltips and background (TransportBarView.cs).
/// Tests: TabForge.Avalonia.Tests.TransportBarViewTests.
/// </summary>
public sealed partial class TransportBarView
{
    private sealed class Divider : Control
    {
        public Divider() { Width = 1; Height = 22; Margin = new Thickness(3, 0); VerticalAlignment = VerticalAlignment.Center; }
        public override void Render(DrawingContext context) =>
            context.FillRectangle(AvaloniaPalette.PaletteBrush("PlaybarBorder", ActualThemeVariant == ThemeVariant.Light), new Rect(Bounds.Size));
    }

    /// <summary>One transport button drawn through IDrawContext: 34 x 34 for the playback group and mixer, 26 x 17 for each zoom button.</summary>
    public sealed class TransportButton : Control
    {
        private const double IconInset = 3;
        // The halo: rings outside the button, each fainter than the one inside it, drawn only when the render runs (state or size change).
        private void DrawHalo(IDrawContext d, TabForge.Platform.UiColor accent)
        {
            for (var distance = TransportGlowRules.HaloStep; distance <= TransportGlowRules.HaloWidth; distance += TransportGlowRules.HaloStep)
                d.Rect(null, new UiStroke(TransportGlowRules.Halo(accent, distance), 1), 0.5 - distance, 0.5 - distance,
                    Width - 1 + 2 * distance, Height - 1 + 2 * distance, ButtonCornerRadius + distance);
        }

        // Ink centre minus the 96-unit centre (48, 48) of each off-centre icon: the tuning fork's ink spans x 42-66, y 4-60; the magnet's y 12-68.
        private static readonly IReadOnlyDictionary<string, (double X, double Y)> IconInkOffset = new Dictionary<string, (double X, double Y)>
        {
            ["tuning_fork"] = (-6, 16),
            ["snap_magnet"] = (0, 8),
        };
        private const double PauseBarWidthShare = 0.14;
        private const double PauseBarHeightShare = 0.5;
        private const double PauseBarGapShare = 0.12;
        internal const double ButtonCornerRadius = 6;
        // The button's outline width in DIPs: 1 DIP drew under 1 px at the 0.8 UI scale and read too faint, so the outline is 1.5 DIPs.
        internal const double BorderWidth = 1.5;
        private readonly AvaloniaDrawContext _draw = new();

        public TransportButton(Item item, string baseTip, double width, double height)
        {
            Item = item;
            BaseTip = baseTip;
            Width = width;
            Height = height;
            Margin = new Thickness(1);
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
            // The right press opens the button's own popup (SecondaryPressed): it must not also open the pane's context menu above it.
            ContextRequested += (_, e) => e.Handled = true;
        }

        public Item Item { get; }

        /// <summary>The tooltip text without the preview's unavailable note (key text included).</summary>
        public string BaseTip { get; }

        /// <summary>Runs when a left press lands on an enabled button; null when the button is not wired.</summary>
        public Action? Pressed { get; set; }

        /// <summary>Runs when a right press lands on an enabled button (the right-click settings); null when the button has none.</summary>
        public Action? SecondaryPressed { get; set; }

        /// <summary>True for a button that works without a bound session (the tuning fork, the snap magnet); the session never disables it.</summary>
        public bool Local { get; set; }

        /// <summary>True while a toggle is on: it takes the accent border, and the loop lit fill for the loop button.</summary>
        public bool Lit { get; set; }

        /// <summary>True while the song plays: the Play button shows its pause bars and takes the active glow.</summary>
        public bool Playing { get; set; }

        private bool _hovered;
        private bool _pressed;

        /// <summary>The accent this button glows with while lit or playing; null when it is not glowing.</summary>
        public TabForge.Platform.UiColor? GlowAccent(bool light) =>
            Lit || Playing ? Palette.Get(TransportGlowRules.AccentKeyOf(Item.Name), light) : null;

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            _hovered = true;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hovered = false;
            _pressed = false;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_pressed) return;
            _pressed = false;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!IsEnabled) return;
            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsLeftButtonPressed && !_pressed)
            {
                _pressed = true;
                InvalidateVisual();
            }
            if (properties.IsLeftButtonPressed) Pressed?.Invoke();
            else if (properties.IsRightButtonPressed)
            {
                e.Handled = true;
                SecondaryPressed?.Invoke();
            }
        }

        public override void Render(DrawingContext context)
        {
            var light = ActualThemeVariant == ThemeVariant.Light;
            var d = _draw.Use(context);
            // Unavailable buttons keep the transport's full brightness (the Core Disabled dim of Looks/App.json is not applied here); unlit toggles take 0.78 on dark.
            d.PushOpacity(!IsEnabled && Item.Toggle && !light ? 0.78 : 1);
            var accent = GlowAccent(light);
            var hovered = _hovered && IsEnabled && accent is null;
            var fill = hovered ? AvaloniaPalette.Get(HoverFillKey, light) : Fill(Item.Fill, light);
            if (light && !hovered) fill = ThemeColours.Blend(fill, AvaloniaPalette.Get("PanelBackground", light), TransportGlowRules.LightFillLift);   // light: the coloured fill lifted toward the surface, the outline keeps the colour
            if (_pressed && IsEnabled) fill = ThemeColours.Shade(fill, TransportGlowRules.PressedShade);
            if (accent is { } glow && !_pressed) DrawHalo(d, glow);
            var border = accent is { } lit ? TransportGlowRules.Border(lit) : Palette.Get(Item.Border, light);
            // The outline is centred on its rectangle, so the rectangle is inset by half its width to keep the outer edge on the button's bounds.
            d.Rect(fill, new UiStroke(border, BorderWidth), BorderWidth / 2, BorderWidth / 2, Width - BorderWidth, Height - BorderWidth, ButtonCornerRadius);
            if (accent is { } tinted)
            {
                // Under the pointer a lit button's tint is a clear step stronger, so the hover shows on it too (its own hover fill is held back while lit).
                var hoverLit = _hovered && IsEnabled && !_pressed;
                var tint = hoverLit ? TransportGlowRules.HoveredTint(tinted, light) : TransportGlowRules.Tint(tinted, light);
                d.Rect(tint, null, 0.5, 0.5, Width - 1, Height - 1, ButtonCornerRadius);
            }
            if (Playing && Item.Name == PlayItemName)
            {
                // The pause shape: two bars centred in the button, in the glow colour.
                var barWidth = Width * PauseBarWidthShare;
                var barHeight = Height * PauseBarHeightShare;
                var top = (Height - barHeight) / 2;
                var gap = Width * PauseBarGapShare;
                var left = (Width - 2 * barWidth - gap) / 2;
                d.Rect(border, null, left, top, barWidth, barHeight, 1);
                d.Rect(border, null, left + barWidth + gap, top, barWidth, barHeight, 1);
            }
            else if (Item.Icon is { } icon)
            {
                var iconSize = Math.Min(Width, Height) - 2 * IconInset;
                // The artwork's ink sits off the 96-unit centre for these two icons; the offset moves the ink to the button centre.
                var (inkX, inkY) = IconInkOffset.GetValueOrDefault(icon);
                var scale = iconSize / 96;
                TransportIconArt.Draw(d, icon, (Width - iconSize) / 2 + inkX * scale, (Height - iconSize) / 2 + inkY * scale, iconSize, light);
            }
            else
            {
                // The record dot is drawn for the 34 DIP button and scaled to this button's size (compact mode shrinks the buttons), centred in it.
                var danger = Palette.Get("Danger", light);
                var dotScale = Math.Min(Width, Height) / TransportBarView.TransportButtonSize;
                d.Ellipse(null, new UiStroke(danger, 2), Width / 2, Height / 2, 10 * dotScale, 10 * dotScale);
                d.Ellipse(danger, null, Width / 2, Height / 2, 5 * dotScale, 5 * dotScale);
            }
            d.Pop();
        }

        // The playbar fills are palette tokens with their own light value (a pale tint of the same hue in the light theme).
        internal static UiColor Fill(string key, bool light) => Palette.Get(key, light);
    }
}
