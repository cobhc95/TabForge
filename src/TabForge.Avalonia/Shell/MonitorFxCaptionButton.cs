using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Shell;

/// <summary>
/// The title bar's Monitor FX button, between Record video and Settings: a headphones icon (the mixer's MON glyph), grey while the monitor chain is
/// off or empty and amber while it is on with plug-ins (a pale amber tint with an amber outline in the light theme). Click opens the Monitor FX window;
/// the right-click menu switches the chain on or off, picks the program-wide or this song's chain, and opens the window. It reads the chain again
/// only when the binding says it changed.
/// Owns: the button, its tooltip, menu and look. Does not own: the chain or the window (the binding's owner) or the caption row (CaptionButtons).
/// Tests: TabForge.Avalonia.Tests.MonitorFxTitleButtonTests.
/// </summary>
public static class MonitorFxCaptionButton
{
    public const string ButtonName = "MonitorFx";
    private const string Headphones = "M 2,10 A 6,6 0 0 1 14,10 M 2,10 L 4.5,10 L 4.5,14 L 2,14 Z M 14,10 L 11.5,10 L 11.5,14 L 14,14 Z";

    public static Button Build(MonitorFxBinding? monitor, Func<string, bool>? nativeOwnsClick)
    {
        var glyph = new Path
        {
            Data = Geometry.Parse(Headphones), Width = ChromeMetrics.MonitorFxGlyphWidth, Height = ChromeMetrics.MonitorFxGlyphHeight, Stretch = Stretch.Uniform, StrokeThickness = ChromeMetrics.MonitorFxGlyphStroke,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false,
        };
        var button = CaptionButtons.CaptionButton(ButtonName, "Monitor FX", "ChromeCaptionButton", ChromeMetrics.CaptionIconButtonWidth, glyph, () => monitor?.Open(), nativeOwnsClick);
        if (monitor is null) { glyph[!Shape.StrokeProperty] = new DynamicResourceExtension("ChromeGlyph"); return button; }

        var onItem = new MenuItem { Header = "Monitor FX on", ToggleType = MenuItemToggleType.CheckBox };
        var globalItem = new MenuItem { Header = "Use program-wide chain", ToggleType = MenuItemToggleType.Radio, GroupName = "MonitorFxChain" };
        var songItem = new MenuItem { Header = "This song only", ToggleType = MenuItemToggleType.Radio, GroupName = "MonitorFxChain" };
        var openItem = new MenuItem { Header = "Open Monitor FX..." };
        onItem.Click += (_, _) => monitor.SetOn(!monitor.Read().On);
        globalItem.Click += (_, _) => monitor.SetGlobal(true);
        songItem.Click += (_, _) => monitor.SetGlobal(false);
        openItem.Click += (_, _) => monitor.Open();
        button.ContextMenu = new ContextMenu { Items = { onItem, new Separator(), globalItem, songItem, openItem } };

        (bool On, int Plugins, bool Global)? shown = null;
        bool? shownLight = null;
        void Update()
        {
            var state = monitor.Read();
            var light = button.ActualThemeVariant == ThemeVariant.Light;
            if (shown == state && shownLight == light) return;
            shown = state;
            shownLight = light;
            var lit = state.On && state.Plugins > 0;
            glyph[!Shape.StrokeProperty] = new DynamicResourceExtension(lit ? "MeterHotBrush" : "ChromeGlyph");
            var amber = lit && light && button.TryFindResource("MeterHotBrush", button.ActualThemeVariant, out var found) && found is ISolidColorBrush solid ? solid : null;
            button.Background = amber is null ? AvaloniaPalette.TransparentBrush : new SolidColorBrush(amber.Color, 0.2);
            button.BorderBrush = amber;
            button.BorderThickness = amber is null ? default : new(1);
            onItem.IsChecked = state.On;
            globalItem.IsChecked = state.Global;
            songItem.IsChecked = !state.Global;
            ToolTip.SetTip(button, $"Monitor FX - live only, not exported\n{(state.Global ? "Program-wide" : "This song")} · {state.Plugins} plug-in{(state.Plugins == 1 ? "" : "s")} · {(state.On ? "on" : "off")}");
        }
        monitor.Changed += Update;
        button.ActualThemeVariantChanged += (_, _) => Update();
        button.AttachedToVisualTree += (_, _) => Update();
        Update();
        return button;
    }
}
