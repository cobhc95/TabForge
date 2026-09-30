using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>
/// The Master row's "MON" button: opens the monitoring effects chain (speaker / room calibration; heard live only, never rendered).
/// Drawn in code with theme brushes, redrawn only when its state changes. Looks different from the FX buttons on purpose: an amber
/// outline and a headphones glyph; the amber fills when the chain has enabled plug-ins.
/// </summary>
public sealed class MonitorFxButton : FrameworkElement
{
    private bool _hover;

    /// <summary>The monitor chain has enabled plug-ins.</summary>
    public bool Active { get => _active; set { if (_active == value) return; _active = value; InvalidateVisual(); } }
    private bool _active;

    public event EventHandler? Click;

    public MonitorFxButton()
    {
        Width = 56; Height = 22;
        Focusable = true; UseLayoutRounding = true; SnapsToDevicePixels = true; FocusVisualStyle = null;
        System.Windows.Automation.AutomationProperties.SetName(this, "Monitor FX");
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Click?.Invoke(this, EventArgs.Empty); }
    }
    protected override void OnMouseEnter(MouseEventArgs e) { _hover = true; InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = false; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => e.Handled = true;
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { e.Handled = true; Click?.Invoke(this, EventArgs.Empty); }
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
    {
        public Peer(MonitorFxButton o) : base(o) { }
        protected override string GetClassNameCore() => "MonitorFxButton";
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Button;
        protected override string GetHelpTextCore() => ((MonitorFxButton)Owner)._active ? "Monitor effects on. Enter opens the chain." : "No monitor effects. Enter opens the chain.";
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        Brush Res(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
        var surface = Res("Panel2Brush", Color.FromRgb(0x2A, 0x2F, 0x37));
        var text = Res("TextBrush", Colors.White);
        var amber = Res("MeterHotBrush", Color.FromRgb(0xE6, 0xB9, 0x3A));
        var hover = Res("HoverBrush", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        var ink = new SolidColorBrush(Color.FromRgb(0x1B, 0x16, 0x05));   // dark text on the amber fill (both themes)

        var outer = new Rect(0.5, 0.5, w - 1, h - 1);
        dc.DrawRoundedRectangle(_active ? amber : surface, new Pen(amber, 1.5), outer, 4, 4);
        if (_hover) dc.DrawRoundedRectangle(hover, null, new Rect(1, 1, w - 2, h - 2), 3, 3);
        if (IsKeyboardFocused) dc.DrawRoundedRectangle(null, new Pen(_active ? ink : text, 1.5) { DashStyle = DashStyles.Dot }, new Rect(2.5, 2.5, w - 5, h - 5), 3, 3);

        var fg = _active ? ink : text;
        // Headphones glyph: band and two ear cups.
        var pen = new Pen(fg, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var gx = 7.0; var gy = h / 2 + 0.5; var r = 4.5;
        var band = new StreamGeometry();
        using (var g = band.Open())
        {
            g.BeginFigure(new Point(gx, gy + 1), false, false);
            g.ArcTo(new Point(gx + 2 * r, gy + 1), new Size(r, r + 1), 0, false, SweepDirection.Clockwise, true, false);
        }
        band.Freeze();
        dc.DrawGeometry(null, pen, band);
        dc.DrawRoundedRectangle(fg, null, new Rect(gx - 1.6, gy + 0.5, 3.2, 5), 1, 1);
        dc.DrawRoundedRectangle(fg, null, new Rect(gx + 2 * r - 1.6, gy + 0.5, 3.2, 5), 1, 1);

        var label = new FormattedText("MON", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Draw.Bold, 10.5, fg, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(gx + 2 * r + 5, (h - label.Height) / 2));
    }
}
