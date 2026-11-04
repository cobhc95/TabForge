using System;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform.Theme;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the headphones icon button of the "Active on" rule (plug-in toolbar and container header): the toolbar button look, the outline icon
//   with no rule, the accent fill plus a small accent dot with a rule, the tooltip, and the disabled state with nothing selected.
// Does not own: the popup it opens (FxActiveOnPopup), the rule (ActiveOnRule) or which rule it shows (the window / the container pane call Update).
// Tests: FxActiveOnTests, FxActiveOnButtonBrushTests.
// Threads: UI thread.
internal sealed class FxActiveOnButton
{
    private readonly Path _icon;
    private readonly Path _dot;

    public FxActiveOnButton(string name)
    {
        _icon = new Path
        {
            Data = Geometry.Parse(IconGeometry.Headphones), StrokeThickness = DialogMetrics.ActiveOnIconStroke, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round, Stretch = Stretch.Uniform,
            Width = DialogMetrics.ActiveOnIconSize, Height = DialogMetrics.ActiveOnIconSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Name = name + "Icon", IsHitTestVisible = false,
        };
        _dot = new Path
        {
            Data = new EllipseGeometry(new global::Avalonia.Rect(0, 0, DialogMetrics.ActiveOnDotSize, DialogMetrics.ActiveOnDotSize)), Stretch = Stretch.Fill, StrokeThickness = DialogMetrics.ActiveOnDotStroke,
            Width = DialogMetrics.ActiveOnDotSize, Height = DialogMetrics.ActiveOnDotSize, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Name = name + "Dot", IsHitTestVisible = false, IsVisible = false,
            [!Shape.FillProperty] = new DynamicResourceExtension("AccentBrush"),
        }.WithMargin(DialogMetrics.ActiveOnDotMargin);
        var content = new Grid { Children = { _icon, _dot } };
        Button = new Button { Name = name, Content = content, Focusable = true }.WithPadding(new global::Avalonia.Thickness(0)).WithMargin(DialogMetrics.ActiveOnButtonMargin);
        Button.Width = Button.Height = DialogMetrics.ActiveOnButtonSize;
        Button.CornerRadius = new global::Avalonia.CornerRadius(DialogMetrics.ActiveOnButtonRadius);
        Button.BorderThickness = new global::Avalonia.Thickness(1);
        Button.HorizontalContentAlignment = HorizontalAlignment.Center;
        Button.VerticalContentAlignment = VerticalAlignment.Center;
        // Built after the window content is attached, so the default-look hook already ran: name the Core look here, as the neighbouring toolbar buttons do.
        Button.Classes.Add(AvaloniaDefaultLooks.ButtonLook);
        Button.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(Button, ActiveOnPresenter.ButtonName);
        ToolTip.SetShowOnDisabled(Button, true);
        Update(null, false);
    }

    public Button Button { get; }
    internal Path Icon => _icon;
    internal Path Dot => _dot;

    /// <summary>True while the button shows a rule (accent fill and dot).</summary>
    public bool HasRule { get; private set; }

    /// <summary>Shows the rule of the selected plug-in or container; <paramref name="hasTarget"/> false: nothing is selected (dimmed).</summary>
    public void Update(ActiveOnRule? rule, bool hasTarget, bool activeHere = true)
    {
        HasRule = hasTarget && rule is { IsAny: false };
        Button.IsEnabled = hasTarget;
        _dot.IsVisible = HasRule;
        ToolTip.SetTip(Button, ActiveOnPresenter.ButtonToolTip(rule, hasTarget));
        AutomationProperties.SetHelpText(Button, ActiveOnPresenter.ButtonToolTip(rule, hasTarget));
        _dot[!Shape.FillProperty] = new DynamicResourceExtension(activeHere ? "AccentBrush" : "Warning");
        if (HasRule && activeHere)
        {
            Button[!global::Avalonia.Controls.Button.BackgroundProperty] = new DynamicResourceExtension("SelectedTintBrush");
            Button[!global::Avalonia.Controls.Button.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
            _icon[!Shape.StrokeProperty] = new DynamicResourceExtension("AccentBrush");
        }
        else if (HasRule)
        {
            Button[!global::Avalonia.Controls.Button.BackgroundProperty] = new DynamicResourceExtension("FxInactiveRuleWash");
            Button[!global::Avalonia.Controls.Button.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
            _icon[!Shape.StrokeProperty] = new DynamicResourceExtension("TextPrimary");
        }
        else
        {
            Button.ClearValue(global::Avalonia.Controls.Button.BackgroundProperty);
            Button[!global::Avalonia.Controls.Button.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
            _icon[!Shape.StrokeProperty] = new DynamicResourceExtension("TextMuted");
        }
    }
}
