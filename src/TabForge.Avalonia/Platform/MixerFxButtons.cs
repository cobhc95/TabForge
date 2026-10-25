using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The Mixer's two drawn buttons, twins of the WPF FxSplitButton and MonitorFxButton: the split FX button (the left part opens a chain, the
/// right part is its power) and the amber MON button. Both draw through one reused <see cref="AvaloniaDrawContext"/> in theme colours and redraw
/// only on a state or hover change.
/// Owns: the two controls' drawing and pointer parts. Does not own: what a click does (the Mixer window) or the chain state.
/// Tests: TabForge.Avalonia.Tests.MixerWindowTests.
/// </summary>
public abstract class MixerDrawnButton : Control
{
    private readonly AvaloniaDrawContext _draw = new();

    protected MixerDrawnButton() { Focusable = true; }

    protected bool Light => ActualThemeVariant == ThemeVariant.Light;

    /// <summary>True while the pointer is held down on the button; the drawn state uses the pressed surface instead of the hover surface.</summary>
    protected bool Down { get; private set; }

    protected void SetDown(bool down)
    {
        if (Down == down) return;
        Down = down;
        InvalidateVisual();
    }

    protected abstract void Draw(IDrawContext d, double width, double height);

    public override void Render(DrawingContext context) => Draw(_draw.Use(context), Bounds.Width, Bounds.Height);
}

/// <summary>The split FX button: "FX" opens the chain, the power part switches it. Width 54 and height 22 as the WPF mixer rows.</summary>
public sealed class FxSplitButton : MixerDrawnButton
{
    private const double SplitShare = 0.58;
    private int _hover = -1;
    private bool _chainOn;
    private int _pluginCount;

    public bool ChainOn { get => _chainOn; set { _chainOn = value; InvalidateVisual(); } }
    public int PluginCount { get => _pluginCount; set { _pluginCount = value; InvalidateVisual(); } }
    public event EventHandler? OpenChain;
    public event EventHandler? TogglePower;

    public FxSplitButton() { Width = ChromeMetrics.FxSplitButtonWidth; Height = ChromeMetrics.MixerFxButtonHeight; }

    private bool OnFxPart(PointerEventArgs e) => e.GetPosition(this).X < Bounds.Width * SplitShare;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var part = OnFxPart(e) ? 0 : 1;
        if (part != _hover) { _hover = part; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = -1; SetDown(false); InvalidateVisual(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        e.Handled = true;
        SetDown(false);
        if (OnFxPart(e)) OpenChain?.Invoke(this, EventArgs.Empty); else TogglePower?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) { e.Handled = true; SetDown(true); }

    protected override void Draw(IDrawContext d, double width, double height)
    {
        var split = width * SplitShare;
        UiColor Ink(string key) => key == "PanelBackground" ? this.BrushColour("Panel2Brush") : AvaloniaPalette.Get(key, Light);
        TrackRowCells.DrawFx(d, Ink, new Rect(0, 0, split, height), new Rect(split, 0, width - split, height), _chainOn, _pluginCount, _hover, Down);
    }
}

/// <summary>The Master row's "MON" button: an amber outline and a headphones glyph; the amber fills when the monitor chain is active.</summary>
public sealed class MonitorFxButton : MixerDrawnButton
{
    private const string Font = ChromeMetrics.UiFontFamily;
    private bool _hover;
    private bool _active;

    public bool Active { get => _active; set { _active = value; InvalidateVisual(); } }
    public event EventHandler? Click;

    public MonitorFxButton() { Width = ChromeMetrics.MonitorFxButtonWidth; Height = ChromeMetrics.MixerFxButtonHeight; }

    protected override void OnPointerEntered(PointerEventArgs e) { _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { _hover = false; SetDown(false); InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { e.Handled = true; SetDown(true); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { e.Handled = true; SetDown(false); Click?.Invoke(this, EventArgs.Empty); }

    protected override void Draw(IDrawContext d, double w, double h)
    {
        var amber = this.BrushColour("MeterHotBrush");
        var fg = _active ? AvaloniaPalette.Get("MonitorFxInkOnAmber", Light) : AvaloniaPalette.Get("TextPrimary", Light);
        d.Rect(_active ? amber : this.BrushColour("Panel2Brush"), new UiStroke(amber, 1.5), 0.5, 0.5, w - 1, h - 1, 4);
        if (_hover) d.Rect(AvaloniaPalette.Get(Down ? "PressSurface" : "HoverSurface", Light), null, 1, 1, w - 2, h - 2, 3);
        var pen = new UiStroke(fg, 1.4, RoundCaps: true);
        const double gx = 7, r = 4.5;
        var gy = h / 2 + 0.5;
        d.Path(null, pen, d.Paths.BuildPath(s =>
        {
            s.BeginFigure(new UiPoint(gx, gy + 1), false, false);
            s.ArcTo(new UiPoint(gx + 2 * r, gy + 1), r, r + 1, 0, false, true);
        }));
        d.Rect(fg, null, gx - 1.6, gy + 0.5, 3.2, 5, 1);
        d.Rect(fg, null, gx + 2 * r - 1.6, gy + 0.5, 3.2, 5, 1);
        var label = d.Text("MON", Font, 10.5, fg, d.PixelsPerDip, weight: 700);
        d.DrawText(label, gx + 2 * r + 5, (h - label.Height) / 2);
    }
}
