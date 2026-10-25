using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the draggable divider between the FX chain window's plug-in list and its plug-in pane: the width limits, the double-click reset, the hover
//   highlight and remembering the width (PluginSettings.FxListWidth). The pointer does not change shape over it.
// Does not own: the list or the pane, or fitting the plug-in editor to the pane (FxEditorArea follows the pane size as it does on a window resize).
// Tests: no dedicated test class.
public sealed class FxListSplitter : Border
{
    public const double Default = 290, Minimum = 180, PaneMinimum = 400, WindowShare = 0.45, WindowMargin = 30, BarCornerRadius = 2, BarMargin = 2;

    private readonly Grid _body;
    private readonly PluginSettings _settings;
    private readonly Action _save;
    private readonly Func<double> _windowWidth;
    private Point _from;
    private double _startWidth;
    private bool _dragging;

    /// <summary>The list width allowed in a window that wide: at least 180, at most 45% of the window, and the plug-in pane keeps a usable width.</summary>
    public static double Limit(double width, double windowWidth)
    {
        if (!double.IsFinite(width)) width = Default;
        var most = Math.Max(Minimum, Math.Min(windowWidth * WindowShare, windowWidth - PaneMinimum - WindowMargin));
        return Math.Round(Math.Clamp(width, Minimum, most));
    }

    /// <param name="body">The grid whose first column is the list and second column this divider.</param>
    /// <param name="save">Writes the settings after a drag or reset.</param>
    /// <param name="windowWidth">The window width the limit is computed against.</param>
    public FxListSplitter(Grid body, PluginSettings settings, Action save, Func<double> windowWidth)
    {
        (_body, _settings, _save, _windowWidth) = (body, settings, save, windowWidth);
        Name = "ListSplitter";
        Margin = new Thickness(BarMargin, 0);
        CornerRadius = new CornerRadius(BarCornerRadius);
        Background = Platform.AvaloniaPalette.TransparentBrush;
        Child = new Border { CornerRadius = new CornerRadius(BarCornerRadius), [!BackgroundProperty] = new DynamicResourceExtension("BorderSoftBrush") };
        ToolTip.SetTip(this, "Drag to resize the plug-in list (double-click: default width)");
        global::Avalonia.Automation.AutomationProperties.SetName(this, "Plug-in list width");
        PointerPressed += OnDown;
        PointerMoved += OnMove;
        PointerReleased += OnUp;
    }

    /// <summary>The list width now (DIPs).</summary>
    public double ListWidth => _body.ColumnDefinitions[0].Width.Value;

    /// <summary>Sets the list width within the limits for the current window width.</summary>
    public void Apply(double width) => _body.ColumnDefinitions[0].Width = new GridLength(Limit(width, _windowWidth()));

    /// <summary>A narrow window squeezes the list; widening gives the chosen width back.</summary>
    public void Reapply() => Apply(_settings.FxListWidth);

    /// <summary>Back to the default width (the double-click), remembered.</summary>
    public void Reset() { Apply(Default); Keep(); }

    private void Keep()
    {
        if (_settings.FxListWidth == ListWidth) return;
        _settings.FxListWidth = ListWidth;
        _save();
    }

    private void OnDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount > 1) { Reset(); e.Handled = true; return; }
        _dragging = true;
        _from = e.GetPosition(_body);
        _startWidth = ListWidth;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (_dragging) Apply(_startWidth + e.GetPosition(_body).X - _from.X);
    }

    private void OnUp(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        Keep();
    }

    /// <summary>Drags by <paramref name="delta"/> DIPs and remembers the result (the test and the keyboard route).</summary>
    public void DragBy(double delta) { Apply(ListWidth + delta); Keep(); }
}
