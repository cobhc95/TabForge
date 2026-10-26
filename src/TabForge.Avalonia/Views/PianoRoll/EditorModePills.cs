using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

// Owns: the Piano / Tab pills in the main menu row (right side, left of the open / save quick buttons; MainWindowMenu docks them): the lit pill is the view
//       shown for the current track, a click on the other switches that track's view (the same command as the right-click rows). Hover and pressed
//       states, tooltips per pill, disabled (with a tooltip) while no track is shown, and hidden by the row when it is too narrow (the menus keep priority).
//       Drawn in theme tokens (Palette ModePill*, light and dark). Retired 26 Oct (tombstone): the copies that floated over the score and piano roll.
// Does not own: the switch itself (EditorPaneSwitch.SetView) or which view a track uses (PianoRollViewRules).
// Tests: TabForge.Avalonia.Tests.PianoRollPaneSwitchTests.
public sealed class EditorModePills : Control
{
    private const double PillSize = 46, PillTall = 20, Gap = 2, Radius = 10, TextSize = 11, Inset = 6;
    internal const string PianoTip = "Show this track as piano roll", TabTip = "Show this track as score/tab", NoTrackTip = "Select a track to switch its view";
    private readonly AvaloniaDrawContext _draw = new();
    private int _hover = -1;   // 0 Piano, 1 Tab, -1 none
    private bool _pressed;

    public EditorModePills()
    {
        Height = PillTall;
        this.WithMargin(ChromeMetrics.LiveStripInset, 0, ChromeMetrics.LiveStripInset, 0);
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        Focusable = false;
        IsEnabled = false;
        AutomationProperties.SetName(this, "Piano roll or tab view of this track");
        ToolTip.SetShowOnDisabled(this, true);
        ToolTip.SetTip(this, NoTrackTip);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(2 * PillSize + Gap, PillTall);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsEnabledProperty) return;
        Opacity = IsEnabled ? 1 : 0.45;
        _hover = -1;
        _pressed = false;
        ToolTip.SetTip(this, IsEnabled ? TipAt(_piano ? PillSize + Gap : 0) : NoTrackTip);
        InvalidateVisual();
    }

    private static string TipAt(double x) => PianoAt(x) ? PianoTip : TabTip;

    /// <summary>True when the piano roll is shown (the Piano pill is lit).</summary>
    public bool PianoShown
    {
        get => _piano;
        set { if (_piano == value) return; _piano = value; InvalidateVisual(); }
    }
    private bool _piano;

    /// <summary>A pill was clicked: true asks for the piano roll, false for the score / tab.</summary>
    public event Action<bool>? Picked;

    /// <summary>The pill at <paramref name="x"/>: true for Piano (left), false for Tab (right).</summary>
    internal static bool PianoAt(double x) => x < PillSize + Gap / 2;

    internal void Click(double x)
    {
        var piano = PianoAt(x);
        if (piano != _piano) Picked?.Invoke(piano);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        var side = PianoAt(x) ? 0 : 1;
        if (side == _hover) return;   // repaints only when the pill under the pointer changes
        _hover = side;
        ToolTip.SetTip(this, TipAt(x));
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        _pressed = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        InvalidateVisual();
        Click(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pressed = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var d = _draw.Use(context);
        var light = ActualThemeVariant == ThemeVariant.Light;
        Pill(d, 0, "Piano", _piano, light, _hover == 0);
        Pill(d, PillSize + Gap, "Tab", !_piano, light, _hover == 1);
    }

    private void Pill(IDrawContext d, double x, string label, bool on, bool light, bool hovered)
    {
        var fill = AvaloniaPalette.Get(on ? "ModePillOn" : hovered ? (_pressed ? "ModePillPressed" : "ModePillHover") : "ModePillOff", light);
        var ink = AvaloniaPalette.Get(on ? "ModePillOnText" : "ModePillOffText", light);
        d.Rect(fill, new UiStroke(AvaloniaPalette.Get("ModePillBorder", light), 1), x, 0, PillSize, PillTall, Radius);
        var text = d.Text(label, InstrumentPanelView.Font, TextSize, ink, d.PixelsPerDip, weight: on ? 600 : 400);
        d.DrawText(text, x + Math.Max(Inset, (PillSize - text.Width) / 2), (PillTall - text.Height) / 2);
    }
}
