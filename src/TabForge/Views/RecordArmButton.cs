using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>
/// A track's record-arm button: a round button with a record dot, lit red while armed (the audio
/// input is monitored through the track and recorded with Record). Drawn in code; redrawn only on changes.
/// </summary>
public sealed class RecordArmButton : FrameworkElement
{
    private bool _hover;

    public bool Armed { get => _armed; set { _armed = value; TooltipShortcuts.SetText(this, (value ? "Armed. " : "") + BaseTip); InvalidateVisual(); } }
    private const string BaseTip = "Arm for recording: monitors the audio input through this track (press Record on the transport to record)";
    private bool _armed;

    /// <summary>Some (not all) of a group's tracks are armed: drawn as a half-lit arm button.</summary>
    public bool Mixed { get => _mixed; set { _mixed = value; InvalidateVisual(); } }
    private bool _mixed;

    public event EventHandler? Toggled;

    public RecordArmButton()
    {
        Width = 20; Height = 20;
        Focusable = true; UseLayoutRounding = true; SnapsToDevicePixels = true; FocusVisualStyle = null;
        TooltipShortcuts.Bind(this, BaseTip, "Track.Arm");
        System.Windows.Automation.AutomationProperties.SetName(this, "Arm track for recording");
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Toggled?.Invoke(this, EventArgs.Empty); }
    }
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
    {
        public Peer(RecordArmButton o) : base(o) { }
        protected override string GetClassNameCore() => "RecordArmButton";
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Button;
        protected override string GetHelpTextCore() { var b = (RecordArmButton)Owner; return b._armed ? "Armed" : b._mixed ? "Partly armed" : "Not armed"; }
    }

    protected override void OnMouseEnter(MouseEventArgs e) { _hover = true; InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = false; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRender(DrawingContext dc)
    {
        try { RenderGuard.Inject("RecordArmButton"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "RecordArmButton", dc, ActualWidth, ActualHeight)) { } // Not logged: render path: runs per frame.
    }

    private void RenderCore(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = size / 2 - 1;
        var danger = TryFindResource("DangerBrush") as SolidColorBrush;
        var red = danger?.Color ?? Color.FromRgb(0xE0, 0x4A, 0x4A);
        if (IsKeyboardFocused) dc.DrawEllipse(null, new Pen(TryFindResource("TextBrush") as Brush ?? Brushes.White, 1.5) { DashStyle = DashStyles.Dot }, centre, r + 1.5, r + 1.5);
        var surface = TryFindResource("Panel2Brush") as Brush ?? Brushes.DimGray;
        var border = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        if (_mixed && !_armed)
        {
            dc.DrawEllipse(surface, new Pen(new SolidColorBrush(red), 1.4), centre, r, r);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xB0, red.R, red.G, red.B)), null, centre, r * 0.25, r * 0.25);
        }
        else if (_armed)
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, red.R, red.G, red.B)), null, centre, r + 1, r + 1); // glow
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb((byte)(red.R * 0.55), (byte)(red.G * 0.35), (byte)(red.B * 0.35))), new Pen(new SolidColorBrush(red), 1.4), centre, r, r);
            dc.DrawEllipse(new SolidColorBrush(red), null, centre, r * 0.45, r * 0.45);
        }
        else
        {
            dc.DrawEllipse(surface, new Pen(_hover ? new SolidColorBrush(red) : border, 1.2), centre, r, r);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(_hover ? (byte)0xE0 : (byte)0x90, red.R, red.G, red.B)), null, centre, r * 0.4, r * 0.4);
        }
    }
}
