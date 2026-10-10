using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>
/// Split FX button: two buttons in one. The left part ("FX") opens the track's FX chain; the right part
/// (power symbol) switches the track between its plug-in chain (on) and plain Windows MIDI (off).
/// Drawn in code with theme brushes; redrawn only when its state or hover changes.
/// </summary>
public sealed class FxSplitButton : FrameworkElement
{
    private int _hover = -1; // -1 none, 0 FX part, 1 power part

    /// <summary>The track plays through its plug-in chain.</summary>
    public bool ChainOn { get => _chainOn; set { _chainOn = value; InvalidateVisual(); } }
    private bool _chainOn;

    /// <summary>Number of plug-ins in the chain (the label brightens when there are any).</summary>
    public int PluginCount { get => _count; set { _count = value; InvalidateVisual(); } }
    private int _count;

    /// <summary>The track's plug-in chain crashed and was switched off.</summary>
    public bool Faulted
    {
        get => _faulted;
        set
        {
            if (_faulted == value) return;
            _faulted = value;
            TooltipShortcuts.SetText(this, value ? "A plug-in in this chain crashed and was switched off. Open the chain to review it." : DefaultTip);
            InvalidateVisual();
        }
    }
    private const string DefaultTip = "FX: open this track's plug-in chain · power: play through the chain (on) or plain MIDI (off)";
    private bool _faulted;

    /// <summary>The FX part opens something (off for a group header: the group effects bus is not built yet).</summary>
    public bool FxPartEnabled { get => _fxEnabled; set { _fxEnabled = value; InvalidateVisual(); } }
    private bool _fxEnabled = true;

    /// <summary>The power part does something (off when there is nothing to switch).</summary>
    public bool PowerEnabled { get => _powerEnabled; set { _powerEnabled = value; InvalidateVisual(); } }
    private bool _powerEnabled = true;

    public event EventHandler? OpenChain;
    public event EventHandler? TogglePower;

    public FxSplitButton()
    {
        Width = 52; Height = 22;
        Focusable = true; UseLayoutRounding = true; SnapsToDevicePixels = true; FocusVisualStyle = null;
        TooltipShortcuts.Bind(this, DefaultTip, "Track.FxChain");
        System.Windows.Automation.AutomationProperties.SetName(this, "Track FX chain");
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Space or Key.Enter) { e.Handled = true; if (_fxEnabled) OpenChain?.Invoke(this, EventArgs.Empty); }
        else if (e.Key == Key.P) { e.Handled = true; if (_powerEnabled) TogglePower?.Invoke(this, EventArgs.Empty); }
    }
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
    {
        public Peer(FxSplitButton o) : base(o) { }
        protected override string GetClassNameCore() => "FxSplitButton";
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Button;
        protected override string GetHelpTextCore() { var b = (FxSplitButton)Owner; return (b._chainOn ? "Chain on" : "Chain off") + (b._faulted ? ", faulted" : "") + ". Enter opens the chain, P toggles power."; }
    }

    private double Split => ActualWidth * 0.58;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var part = e.GetPosition(this).X < Split ? 0 : 1;
        if (part != _hover) { _hover = part; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e) { _hover = -1; InvalidateVisual(); }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.GetPosition(this).X < Split) { if (_fxEnabled) OpenChain?.Invoke(this, EventArgs.Empty); }
        else if (_powerEnabled) TogglePower?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnRender(DrawingContext dc)
    {
        try { RenderGuard.Inject("FxSplitButton"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "FxSplitButton", dc, ActualWidth, ActualHeight)) { } // Not logged: render path: runs per frame.
    }

    private void RenderCore(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        Brush Res(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
        var surface = Res("Panel2Brush", Color.FromRgb(0x2A, 0x2F, 0x37));
        var border = Res("BorderBrush", Color.FromRgb(0x48, 0x50, 0x5C));
        var text = Res("TextBrush", Colors.White);
        var muted = Res("MutedBrush", Colors.Gray);
        var accent = Res("AccentBrush", Color.FromRgb(0x4C, 0x9A, 0xFF));
        var hover = Res("HoverBrush", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        var split = Split;

        var outer = new Rect(0.5, 0.5, w - 1, h - 1);
        dc.DrawRoundedRectangle(surface, new Pen(border, 1), outer, 4, 4);
        if (_hover == 0 && _fxEnabled) dc.DrawRoundedRectangle(hover, null, new Rect(1, 1, split - 1, h - 2), 3, 3);
        if (_hover == 1 && _powerEnabled) dc.DrawRoundedRectangle(hover, null, new Rect(split, 1, w - split - 1, h - 2), 3, 3);
        if (IsKeyboardFocused) dc.DrawRoundedRectangle(null, new Pen(text, 1.5) { DashStyle = DashStyles.Dot }, new Rect(2, 2, w - 4, h - 4), 3, 3);
        dc.DrawLine(new Pen(border, 1), new Point(split, 3), new Point(split, h - 3));

        // "FX": accent when the chain holds plug-ins, muted when empty.
        var label = new FormattedText("FX", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Draw.Bold, 11, _fxEnabled && _count > 0 ? (_chainOn ? accent : text) : muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point((split - label.Width) / 2, (h - label.Height) / 2));
        if (_faulted)
        {
            // Not colour alone: a "!" badge beside the label marks a chain whose plug-in crashed.
            var badge = new FormattedText("!", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Draw.Bold, 11, Res("DangerBrush", Colors.IndianRed), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(badge, new Point(Math.Min(split - badge.Width - 1, (split + label.Width) / 2 + 1), (h - badge.Height) / 2));
        }

        // Power symbol, in theme colours: "audible" green when on, danger red when the chain crashed, muted when off.
        var powerBrush = !_powerEnabled ? muted : _faulted ? Res("DangerBrush", Colors.IndianRed) : _chainOn ? Res("TrackAudibleBrush", Colors.SeaGreen) : muted;
        var pen = new Pen(powerBrush, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var cx = split + (w - split) / 2; var cy = h / 2 + 0.5; var r = Math.Min(h, w - split) * 0.28;
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            var a0 = -60 * Math.PI / 180; var a1 = 240 * Math.PI / 180;
            g.BeginFigure(new Point(cx + r * Math.Sin(a0), cy - r * Math.Cos(a0)), false, false);
            g.ArcTo(new Point(cx + r * Math.Sin(a1), cy - r * Math.Cos(a1)), new Size(r, r), 0, true, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        dc.DrawGeometry(null, pen, arc);
        dc.DrawLine(pen, new Point(cx, cy - r - 1.2), new Point(cx, cy - r * 0.15));
    }
}
