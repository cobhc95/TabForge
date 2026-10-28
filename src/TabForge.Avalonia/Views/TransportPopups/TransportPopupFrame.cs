using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the look and the opening of one transport settings popup (the metronome, count-in and loop popups): a rounded panel above its
//     transport button with a shadow, a caps title, an × close button, and the row builders the three popups share (a labelled slider with its
//     percent on the right, a labelled drop-down, a check box, muted helper text). It closes on a press outside it (StaysOpen=false), on Escape or on ×.
// Does not own: what the rows change (the content classes and their change callbacks), the settings file, the transport buttons.
// Tests: TabForge.Avalonia.Tests.TransportSettingsPopupTests.
// Threads: UI thread only.
public sealed class TransportPopupFrame
{
    /// <summary>The popup widths of the popups: the metronome's and the loop's, and the narrower count-in's.</summary>
    public const double WideWidth = 276;
    public const double NarrowWidth = 250;

    private const double PanelPadding = 12;
    private const double PanelCornerRadius = 8;
    private const double TitleBottomMargin = 8;
    private const double CloseButtonSize = 23;
    private const double SliderHeaderTopMargin = 3;
    private const double ChoiceLabelTopMargin = 6;
    private const double ChoiceLabelBottomMargin = 2;
    private const double CheckTopMargin = 6;
    private const double HelperTopMargin = 7;
    /// <summary>The small caps title and helper text size.</summary>
    public const double SmallFontSize = 11;
    private const int PercentMaximum = 100;
    private const double SliderTickFrequency = 10;
    // The drop shadow (SettingsPopupShadow, blur 16, depth 3, opacity 0.52); the panel keeps a margin so the shadow is not clipped.
    private const string ShadowKey = "SettingsPopupShadow";
    private const double ShadowBlur = 16;
    private const double ShadowDepth = 3;
    private const double ShadowOpacity = 0.52;
    private static readonly Thickness ShadowRoom = new(ShadowBlur, 0, ShadowBlur, ShadowBlur);
    private const string CloseGlyph = "×";
    private const double CrossSize = 8, CrossStroke = 1.5;
    private const string PowerOnText = "On", PowerOffText = "Off";

    private readonly Popup _popup;
    private readonly Border _panel;
    private readonly DockPanel _titleRow;

    /// <param name="title">The caps title ("METRONOME SETTINGS").</param>
    /// <param name="closeTip">The × button's tooltip.</param>
    /// <param name="width">The panel width (WideWidth or NarrowWidth).</param>
    public TransportPopupFrame(string title, string closeTip, double width)
    {
        // The × is centred in its box; the box itself sits at the title row's right edge, so it lines up with the rows' right padding (the percent labels and the check boxes).
        var close = new Button { Content = CloseGlyph, Width = CloseButtonSize, Height = CloseButtonSize, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        close.ContentTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<string>((_, _) => CrossShape());   // a drawn cross: the glyph sits off-centre in its line box
        ToolTip.SetTip(close, closeTip);
        AutomationProperties.SetName(close, closeTip);
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        var heading = new TextBlock { Text = title, FontSize = SmallFontSize, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        _titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, TitleBottomMargin), Children = { close, heading } };
        Rows.Children.Add(_titleRow);
        _panel = new Border
        {
            Width = width, Padding = new Thickness(PanelPadding), CornerRadius = new CornerRadius(PanelCornerRadius), BorderThickness = new Thickness(1),
            Margin = ShadowRoom, Child = Rows,
        };
        _panel[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        _panel[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        _panel[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _panel.ActualThemeVariantChanged += (_, _) => PaintShadow();
        AutomationProperties.SetName(_panel, title);
        _popup = new Popup { Placement = PlacementMode.Top, IsLightDismissEnabled = true, Child = _panel };
        _panel.Focusable = true;
        _panel.KeyDown += (_, e) =>
        {
            if (e.Key != global::Avalonia.Input.Key.Escape) return;
            e.Handled = true;
            Close();
        };
        _popup.Opened += (_, _) => _panel.Focus();
        _popup.Closed += (_, _) =>
        {
            ((ISetLogicalParent)_popup).SetParent(null);
            Closed?.Invoke();
        };
        PaintShadow();
    }

    /// <summary>Adds the On/Off switch at the right of the title, showing <paramref name="isOn"/>; a change calls <paramref name="set"/> with the new state, then <paramref name="changed"/>.</summary>
    public void AddPower(string name, bool isOn, Action<bool> set, Action changed)
    {
        var power = new ToggleSwitch { IsChecked = isOn, OnContent = PowerOnText, OffContent = PowerOffText, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(power, name);
        AutomationProperties.SetName(power, name);
        DockPanel.SetDock(power, Dock.Right);
        _titleRow.Children.Insert(1, power);   // after the × (first), before the title (last, filling)
        power.IsCheckedChanged += (_, _) => { set(power.IsChecked == true); changed(); };
    }

    /// <summary>The rows under the title; the content classes add theirs here.</summary>
    public StackPanel Rows { get; } = new();

    /// <summary>The rounded panel (the popup's only child), for tests and captures.</summary>
    public Border Panel => _panel;

    public bool IsOpen => _popup.IsOpen;

    /// <summary>Raised once the popup has closed (a press outside, ×, or Close).</summary>
    public event Action? Closed;

    /// <summary>Opens the popup above <paramref name="anchor"/>, centred on it; it takes the anchor's theme and resources, as a flyout does.</summary>
    public void OpenAbove(Control anchor)
    {
        _popup.PlacementTarget = anchor;
        _popup.HorizontalOffset = 0;
        ((ISetLogicalParent)_popup).SetParent(anchor);
        _popup.IsOpen = true;
    }

    public void Close() => _popup.IsOpen = false;

    // Two diagonal strokes in a square box, centred by the button's content alignment.
    private static global::Avalonia.Controls.Shapes.Path CrossShape()
    {
        var cross = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse($"M0,0 L{CrossSize},{CrossSize} M{CrossSize},0 L0,{CrossSize}"), StrokeThickness = CrossStroke,
            Width = CrossSize, Height = CrossSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            StrokeLineCap = PenLineCap.Round,
        };
        cross[!global::Avalonia.Controls.Shapes.Shape.StrokeProperty] = new DynamicResourceExtension("TextPrimary");
        return cross;
    }

    private void PaintShadow()
    {
        var shadow = AvaloniaPalette.ColorOf(Palette.Get(ShadowKey, _panel.ActualThemeVariant == ThemeVariant.Light));
        _panel.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetY = ShadowDepth, Blur = ShadowBlur, Color = Color.FromArgb((byte)Math.Round(255 * ShadowOpacity), shadow.R, shadow.G, shadow.B),
        });
    }

    // ---- rows ----

    /// <summary>A label with its live percent on the right (muted), over a 0-100 slider; each move stores the rounded value and reports
    /// the change through a shared drag coalescer: the label and the stored value follow at once, while the heavy change (engine push,
    /// menu checks, settings save) runs at most every settings-preview interval with a trailing run, and once more when the drag ends.</summary>
    /// <param name="defaultValue">The value a double-click restores; null for none.</param>
    public Slider AddSlider(string label, int value, Action<int> store, Action changed, double topMargin = SliderHeaderTopMargin, int? defaultValue = null)
    {
        var amount = new TextBlock { Text = PercentText(value), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        amount[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        DockPanel.SetDock(amount, Dock.Right);
        Rows.Children.Add(new DockPanel { Margin = new Thickness(0, topMargin, 0, 0), Children = { amount, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } } });
        var slider = new Slider { Orientation = Orientation.Horizontal, Minimum = 0, Maximum = PercentMaximum, Value = value, TickFrequency = SliderTickFrequency, IsSnapToTickEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, label);
        var coalesced = new SliderChangeCoalescer(changed);
        slider.ValueChanged += (_, e) =>
        {
            var rounded = (int)Math.Round(e.NewValue);
            amount.Text = PercentText(rounded);
            store(rounded);
            coalesced.SliderChanged();
        };
        // A drag's last move may only have armed the trailing run: release flushes it so the final value always lands.
        slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => coalesced.Flush(),
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel | global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        if (defaultValue is { } reset) AvaloniaSliderReset.OnDoubleTap(slider, reset);
        Rows.Children.Add(slider);
        return slider;
    }

    /// <summary>A label over a drop-down of <paramref name="items"/>; a pick calls <paramref name="select"/> with its index.</summary>
    public ComboBox AddChoice(string label, IEnumerable<string> items, int selectedIndex, Action<int> select, double labelTopMargin = ChoiceLabelTopMargin)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = selectedIndex, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(combo, label);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) select(combo.SelectedIndex); };
        Rows.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, labelTopMargin, 0, ChoiceLabelBottomMargin), VerticalAlignment = VerticalAlignment.Center });
        Rows.Children.Add(combo);
        return combo;
    }

    /// <summary>A check box; a click stores its state and calls <paramref name="changed"/>. Long text wraps to the panel, so no label is clipped.</summary>
    public CheckBox AddCheck(string text, bool isChecked, Action<bool> store, Action changed, double topMargin = CheckTopMargin)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        var box = new CheckBox { Content = label, IsChecked = isChecked, Margin = new Thickness(0, topMargin, 0, 0) };
        AutomationProperties.SetName(box, text);
        ToolTip.SetTip(box, text);
        box.IsCheckedChanged += (_, _) => { store(box.IsChecked == true); changed(); };
        Rows.Children.Add(box);
        return box;
    }

    /// <summary>Muted helper text at the small size, wrapped to the panel.</summary>
    public TextBlock AddHelper(string text, double topMargin = HelperTopMargin)
    {
        var helper = Helper(text);
        helper.Margin = new Thickness(0, topMargin, 0, 0);
        Rows.Children.Add(helper);
        return helper;
    }

    /// <summary>A muted, small, wrapping text block.</summary>
    public static TextBlock Helper(string text)
    {
        var helper = new TextBlock { Text = text, FontSize = SmallFontSize, TextWrapping = TextWrapping.Wrap };
        helper[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return helper;
    }

    private static string PercentText(int value) => $"{value}%";
}
