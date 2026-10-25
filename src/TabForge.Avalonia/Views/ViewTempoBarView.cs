using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The View and tempo bar: one compact left-aligned row of four natural-size buttons (zoom, playback speed, tempo, time signature with key).
/// Owns: the buttons, their layout, showing the presenter's captions, names and tooltips, and running each button's left-click action (<see cref="SetPressed"/>), opening its fine-tune popup on right click, double click or the Menu key, and its wheel step.
/// Does not own: the texts (ViewTempoBarPresenter), the values (the caller passes them to <see cref="Show"/>), the actions themselves (the caller's commands), the preset menus (ViewTempoMenus) or the popups' content (ViewTempoPopups).
/// Tests: TabForge.Avalonia.Tests.PanelBarsTests, TabForge.Avalonia.Tests.ViewTempoBarButtonTests.
/// </summary>
public sealed class ViewTempoBarView : UserControl
{
    private readonly TextBlock[] _captions = new TextBlock[4];
    private readonly Button[] _buttons = new Button[4];
    private readonly Action?[] _pressed = new Action?[4];
    private readonly Func<Control>?[] _fineTune = new Func<Control>?[4];
    private readonly Action<bool>?[] _wheel = new Action<bool>?[4];

    public ViewTempoBarView()
    {
        Name = "ViewTempoBar";
        var grid = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 3) };
        for (var kind = 0; kind < 4; kind++)
        {
            var caption = _captions[kind] = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            caption[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
            var button = _buttons[kind] = new Button
            {
                Name = "ViewTempo" + kind, Content = Row(kind, caption), Height = 27, Margin = new Thickness(2, 0), Padding = new Thickness(6, 0),
                HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            button[!Button.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
            TabForge.Avalonia.Platform.HoverShade.Wire(button, fill => button.Background = fill,
                () => button.FindResource("PanelBackground") as global::Avalonia.Media.IBrush ?? global::Avalonia.Media.Brushes.Transparent);
            AutomationProperties.SetName(button, ViewTempoBarPresenter.Names[kind]);
            ToolTip.SetTip(button, ViewTempoBarPresenter.Tooltip(kind));
            var index = kind;
            button.Click += (_, _) => _pressed[index]?.Invoke();
            button.AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (e.InitialPressMouseButton != global::Avalonia.Input.MouseButton.Right) return;
                e.Handled = true;
                OpenFineTune(index);
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            button.DoubleTapped += (_, _) => OpenFineTune(index);
            button.KeyDown += (_, e) =>
            {
                var menuKey = e.Key == global::Avalonia.Input.Key.Apps || (e.Key == global::Avalonia.Input.Key.F10 && e.KeyModifiers == global::Avalonia.Input.KeyModifiers.Shift);
                if (!menuKey) return;
                e.Handled = true;
                OpenFineTune(index);
            };
            button.PointerWheelChanged += (_, e) =>
            {
                if (_wheel[index] is not { } wheel) return;
                e.Handled = true;
                wheel(e.Delta.Y > 0);
            };
            button.IsEnabled = false;
            grid.Children.Add(button);
        }
        Content = grid;
    }

    private static StackPanel Row(int kind, TextBlock caption)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        Control? mark = kind switch
        {
            ViewTempoBarPresenter.Zoom => new global::Avalonia.Controls.Shapes.Path
            {
                Data = global::Avalonia.Media.Geometry.Parse(global::TabForge.Platform.Theme.IconGeometry.ZoomGlass),
                StrokeThickness = 1.4, Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0),
                [!global::Avalonia.Controls.Shapes.Shape.StrokeProperty] = new DynamicResourceExtension("TextMuted"),
            },
            ViewTempoBarPresenter.Speed or ViewTempoBarPresenter.Tempo => new TextBlock
            {
                Text = kind == ViewTempoBarPresenter.Speed ? "▶" : "♩", FontSize = kind == ViewTempoBarPresenter.Speed ? 9 : 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0),
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted"),
            },
            _ => null,
        };
        if (mark is not null) row.Children.Add(mark);
        row.Children.Add(caption);
        return row;
    }

    public IReadOnlyList<Button> Buttons => _buttons;

    /// <summary>Sets what a left click on one button runs; null leaves that button disabled. Called once per button by whoever owns its command.</summary>
    public void SetPressed(int kind, Action? pressed)
    {
        _pressed[kind] = pressed;
        _buttons[kind].IsEnabled = pressed is not null;
    }

    /// <summary>Sets the fine-tune popup a right click, double click or Menu key opens on one button; null opens nothing.</summary>
    public void SetFineTune(int kind, Func<Control>? content) => _fineTune[kind] = content;

    /// <summary>Sets what a wheel step over one button runs (true is up); null leaves the wheel alone.</summary>
    public void SetWheel(int kind, Action<bool>? step) => _wheel[kind] = step;

    /// <summary>Opens one button's fine-tune popup below it; returns the flyout, or null when the button has none.</summary>
    public Flyout? OpenFineTune(int kind)
    {
        if (_fineTune[kind] is not { } content) return null;
        var flyout = new Flyout { Content = content(), Placement = PlacementMode.Bottom };
        flyout.ShowAt(_buttons[kind]);
        return flyout;
    }

    public string Caption(int kind) => _captions[kind].Text ?? "";

    /// <summary>Shows the values; called when one changes, never per frame.</summary>
    public void Show(string zoomText, double speedPercent, int tempo, string timeText, string keyText)
    {
        var captions = ViewTempoBarPresenter.Captions(zoomText, speedPercent, tempo, timeText, keyText);
        for (var i = 0; i < 4; i++) _captions[i].Text = captions[i];
        ToolTip.SetTip(_buttons[ViewTempoBarPresenter.TimeKey], ViewTempoBarPresenter.TimeKeyTooltip(timeText, keyText));
    }
}
