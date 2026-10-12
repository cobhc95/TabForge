using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Views;

namespace TabForge.LiveInput;

// Owns: the square microphone button: its look (red while live input is on), its tooltip, and asking its window for the
//   controller it toggles, and opening the right-click menu.
// Does not own: what on and off do (LiveInputController), the command (LiveInputFeatureModule), where it sits (ArrangementPanel,
//   CompactColumnLayout).
// Tests: TestLiveInput, TestCompactMode.
public sealed class LiveInputButton : Button
{
    private readonly System.Windows.Shapes.Path _icon;
    private LiveInputController? _controller;

    public LiveInputButton()
    {
        Style = (Style)Application.Current.FindResource("TransportButton");
        Width = MinWidth = Height = 34;
        Padding = new Thickness(0);
        Margin = new Thickness(2, 0, 2, 0);
        BorderThickness = new Thickness(1);
        _icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M9,3.5 a3,3 0 0 1 6,0 V11 a3,3 0 0 1 -6,0 Z M6,10.5 a6,6 0 0 0 12,0 M12,16.5 V20 M8.5,20 H15.5"),
            Style = (Style)Application.Current.FindResource("IconPath"),
            Width = 20, Height = 20, IsHitTestVisible = false,
        };
        Content = _icon;
        System.Windows.Automation.AutomationProperties.SetName(this, "Live input");
        TooltipShortcuts.Bind(this, "Live input: hear your instrument through the live FX chain", LiveInputFeatureModule.CommandId);
        Click += (_, _) => { RequestAttach(); _controller?.Toggle(); };
        MouseRightButtonUp += (_, e) => { e.Handled = true; OpenMenu(); };
        Loaded += (_, _) => RequestAttach();
        ShowState();
    }

    /// <summary>True while the button shows live input as on.</summary>
    public bool IsLit { get; private set; }

    /// <summary>Gives the button the controller it toggles and follows.</summary>
    internal void Attach(LiveInputController controller)
    {
        if (ReferenceEquals(_controller, controller)) return;
        if (_controller is not null) { _controller.Changed -= ShowState; _controller.MenuRequested -= OpenMenu; }
        _controller = controller;
        controller.Changed += ShowState;
        controller.MenuRequested += OpenMenu;
        ShowState();
    }

    /// <summary>The right-click menu, under the button.</summary>
    private void OpenMenu()
    {
        if (_controller is null) return;
        ContextMenu = LiveInputMenu.Build(_controller);
        ContextMenu.PlacementTarget = this;
        ContextMenu.IsOpen = true;
    }

    private void RequestAttach() { if (LiveInputFeatureModule.ControllerFor(Window.GetWindow(this)) is { } controller) Attach(controller); }

    /// <summary>On: a red outline and icon over a faint red wash, with a soft red glow around the button. Off: the neutral panel with the outline the other toolbar buttons have.</summary>
    private void ShowState()
    {
        IsLit = _controller?.IsOn == true;
        // A light panel needs a deeper red than the dark theme's to stand out; the theme is read from the panel's own colour.
        var panel = (Application.Current.TryFindResource("Panel2Brush") as SolidColorBrush)?.Color ?? Colors.Black;
        var light = panel.R + panel.G + panel.B > 384;
        var red = light ? Color.FromRgb(0xC4, 0x12, 0x1A) : (Application.Current.TryFindResource("DangerBrush") as SolidColorBrush)?.Color ?? Colors.Red;
        var line = new SolidColorBrush(red);
        // On the light theme the microphone is drawn in a near-black red over a paler wash, so its shape stays sharp.
        if (IsLit) { Background = new SolidColorBrush(red) { Opacity = light ? 0.16 : 0.22 }; BorderBrush = line; _icon.Stroke = light ? new SolidColorBrush(Color.FromRgb(0x5A, 0x08, 0x0C)) : line; }
        else { SetResourceReference(BackgroundProperty, "Panel2Brush"); SetResourceReference(BorderBrushProperty, "BorderBrush"); _icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextBrush"); }
        // A static effect on one small button: it is rendered once per state change, not per frame.
        Effect = IsLit ? new System.Windows.Media.Effects.DropShadowEffect { Color = red, BlurRadius = light ? 16 : 12, ShadowDepth = 0, Opacity = light ? 1 : 0.85 } : null;
    }
}
