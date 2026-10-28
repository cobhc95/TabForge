using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the on/off button of the built-in effect editors (Bypass, On, Ping-pong, Auto level ...): TabForge's own toggle look (the "TrackPill" Core look: idle panel fill, hover and press shades,
//   the selected tint when on), so it matches the app's other toggles. No cursor change.
// Does not own: the look rules (Core look table), the idle colours (TrackPillButton.IdleLook, added to the editor's styles), the parameter wiring (ParamSwitch).
// Tests: BuiltinEditorWindowTests.
public class BuiltinToggle : ToggleButton
{
    public BuiltinToggle()
    {
        Classes.Add(TrackPillButton.LookClass);
        Template = AvaloniaButtonTemplates.SingleBorder("Bd");   // the look sets control properties; this template paints them
        DialogMetrics.WithPadding(this, DialogMetrics.FxTogglePadding);
        MinHeight = DialogMetrics.FxToggleMinHeight;
        BorderThickness = DialogMetrics.FxToggleOutline;
        CornerRadius = AvaloniaFormControlStyles.ControlCornerRadius;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    protected override Type StyleKeyOverride => typeof(ToggleButton);
}
