using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TabForge.Services;

namespace TabForge.Views.Fx;

// Owns: the draggable divider between the FX chain window's plug-in list and its plug-in pane: the width limits, the double-click
//   reset, the hover highlight and remembering the width (PluginSettings.FxListWidth).
// Does not own: the list or the pane, or fitting the plug-in editor to the pane (PluginEditorFit follows the pane's size as it does on
//   a window resize).
// Tests: TestFxListWidthLimits.
internal static class FxListSplitter
{
    internal const double Default = 290, Minimum = 180, PaneMinimum = 400;

    /// <summary>The list width allowed in a window that wide: at least 180, at most 45% of the window, and the plug-in pane keeps a usable width.</summary>
    internal static double Limit(double width, double windowWidth)
    {
        if (!double.IsFinite(width)) width = Default;
        var most = Math.Max(Minimum, Math.Min(windowWidth * 0.45, windowWidth - PaneMinimum - 30));
        return Math.Round(Math.Clamp(width, Minimum, most));
    }

    /// <summary>Docks the list and, right of it, the divider; the list takes the remembered width.</summary>
    internal static void Attach(Window window, DockPanel body, FrameworkElement list, PluginSettings settings, Action save)
    {
        list.Margin = new Thickness(0);
        var grip = new Thumb { Width = 6, Margin = new Thickness(2, 0, 2, 0), Cursor = Cursors.SizeWE, Focusable = false, Template = Bar(), ToolTip = "Drag to resize the plug-in list (double-click: default width)" };
        UiIds.Id(grip, "Fx.ListSplitter", "Plug-in list width");
        DockPanel.SetDock(grip, Dock.Left);
        body.Children.Add(list);
        body.Children.Add(grip);
        void Apply(double width) => list.Width = Limit(width, window.ActualWidth > 0 ? window.ActualWidth : window.Width);
        void Keep() { if (settings.FxListWidth == list.Width) return; settings.FxListWidth = list.Width; save(); }
        Apply(settings.FxListWidth);
        grip.DragDelta += (_, e) => Apply(list.Width + e.HorizontalChange);
        grip.DragCompleted += (_, _) => Keep();
        grip.MouseDoubleClick += (_, _) => { Apply(Default); Keep(); };
        window.SizeChanged += (_, _) => Apply(settings.FxListWidth);   // a narrow window squeezes the list; widening gives the chosen width back
    }

    /// <summary>A thin line that lights up under the pointer and while dragging.</summary>
    private static ControlTemplate Bar()
    {
        var line = new FrameworkElementFactory(typeof(Border), "line");
        line.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        line.SetResourceReference(Border.BackgroundProperty, "BorderSoftBrush");
        var template = new ControlTemplate(typeof(Thumb)) { VisualTree = line };
        foreach (var property in new[] { UIElement.IsMouseOverProperty, Thumb.IsDraggingProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("AccentBrush"), "line"));
            template.Triggers.Add(trigger);
        }
        return template;
    }
}
