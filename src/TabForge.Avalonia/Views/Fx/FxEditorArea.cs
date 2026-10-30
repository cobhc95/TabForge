using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using TabForge.Avalonia.Shell.Win32;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the plug-in area of the FX chain window: the notice and its button, the frame that holds the docked editor, the two scroll bars that move
//   the plug-in view when the editor is larger than the area, growing the window to the editor size, the BYPASSED stamp over the area, and the
//   joining of the native editor surface to the frame once the window is shown (DockSurface).
// Does not own: which state shows (FxEditorPlan), opening and closing editors (FxEditorController), the arithmetic (PluginEditorFitMath), the stamp drawing (FxBypassOverlay).
// Tests: no dedicated test class.
public sealed class FxEditorArea : Grid
{
    public const double NoticeMargin = 12, ScrollSmallChange = 40, WheelStep = 60, WheelNotch = 120;
    private const double ScrollBarThickness = 14;

    private readonly TextBlock _notice = new()
    {
        TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, TextAlignment = global::Avalonia.Media.TextAlignment.Center, Name = "DockMessage",
        Margin = new Thickness(NoticeMargin), HorizontalAlignment = HorizontalAlignment.Center,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted"),
    };
    private readonly Button _action = NoticeButton("DockHereButton");
    private readonly Button _second = NoticeButton("SecondActionButton");
    private readonly Border _frame = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsVisible = false, Name = "EditorFrame" };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, IsVisible = false, SmallChange = ScrollSmallChange, Name = "EditorScrollX" };
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, IsVisible = false, SmallChange = ScrollSmallChange, Name = "EditorScrollY" };
    private readonly FxBypassOverlay _overlay = new();
    private readonly Func<IntPtr> _popup;
    private readonly Action<IntPtr, bool> _setPopupVisible;
    private (int W, int H) _editorPixels;
    private bool _covered;

    /// <param name="popup">The engine editor popup of the window (zero when there is none); the scroll bars move its view.</param>
    /// <param name="setPopupVisible">Hides or shows the popup when the stamp changes; default: the Windows call.</param>
    public FxEditorArea(Func<IntPtr> popup, Action<IntPtr, bool>? setPopupVisible = null)
    {
        _popup = popup;
        _setPopupVisible = setPopupVisible ?? PluginEditorWin32.SetPopupVisible;
        Name = "EditorArea";
        ClipToBounds = true;
        Background = Platform.AvaloniaPalette.TransparentBrush;   // the wheel reaches the area between the editor and the bars
        ColumnDefinitions = ColumnDefinitions.Parse("*,Auto");
        RowDefinitions = RowDefinitions.Parse("*,Auto");
        var message = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _notice, _action, _second } };
        Children.Add(message);
        Children.Add(_frame);
        SetColumn(_vertical, 1);
        SetRow(_horizontal, 1);
        Children.Add(_vertical);
        Children.Add(_horizontal);
        SetColumnSpan(_overlay, 2);
        SetRowSpan(_overlay, 2);
        Children.Add(_overlay);
        _action.Click += (_, _) => ActionClicked?.Invoke();
        _second.Click += (_, _) => SecondActionClicked?.Invoke();
        _horizontal.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) MoveView(); };
        _vertical.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) MoveView(); };
        SizeChanged += (_, _) => Fit();
        PointerWheelChanged += (_, e) =>
        {
            if (!_vertical.IsVisible) return;
            _vertical.Value = Math.Clamp(_vertical.Value - e.Delta.Y * WheelStep, 0, _vertical.Maximum);
            e.Handled = true;
        };
    }

    private static Button NoticeButton(string name) => new() { Padding = new Thickness(10, 3), HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false, Name = name };

    /// <summary>The native window the engine parents the editor to.</summary>
    public PluginEditorSurface Surface { get; } = new() { Name = "EditorSurface" };
    /// <summary>The notice button (Allow, Allow again, Show it here) was clicked.</summary>
    public event Action? ActionClicked;
    /// <summary>The notice's second button (Open in separate window) was clicked.</summary>
    public event Action? SecondActionClicked;
    public bool EditorVisible => _frame.IsVisible;
    public string NoticeText => _notice.Text ?? "";
    public bool ActionVisible => _action.IsVisible;
    public string? ActionLabel => _action.Content as string;
    public string? SecondActionLabel => _second.Content as string;
    public bool OverlayVisible => _overlay.IsVisible;
    public bool HorizontalBarVisible => _horizontal.IsVisible && _horizontal.Maximum > 0;
    public bool VerticalBarVisible => _vertical.IsVisible && _vertical.Maximum > 0;
    /// <summary>The size the host frame has now (DIPs), set from the editor size and the area.</summary>
    public Size FrameSize => new(_frame.Width, _frame.Height);

    /// <summary>Shows a notice (and optionally its one or two buttons) in place of the editor.</summary>
    public void ShowNotice(string text, string? button, string? secondButton = null)
    {
        _frame.IsVisible = false;
        _notice.Text = text;
        _action.IsVisible = button is not null;
        _action.Content = button;
        _second.IsVisible = secondButton is not null;
        _second.Content = secondButton;
        Fit();
    }

    /// <summary>Shows the frame the engine docks the editor into; the notice is cleared.</summary>
    public void ShowEditor()
    {
        _notice.Text = "";
        _action.IsVisible = false;
        _second.IsVisible = false;
        _frame.IsVisible = true;
        Fit();
    }

    /// <summary>
    /// Joins the native editor surface to the frame and lays out at once, so a refusal by Windows is caught here. Called after the chain window is shown,
    /// not while it is shown: the native child window is then created in an ordinary layout pass. False when Windows refused it; the frame stays empty.
    /// </summary>
    public bool DockSurface()
    {
        if (_frame.Child is not null) return true;
        try
        {
            _frame.Child = Surface;
            UpdateLayout();
            return true;
        }
        catch (InvalidOperationException)
        {
            // NativeControlHost throws this when the child window cannot be created. The window stays open without a docked editor; the caller reports it.
            _frame.Child = null;
            return false;
        }
    }

    /// <summary>Shows or hides the BYPASSED stamp over the area; the colours follow the theme variant. The native surface hides while the stamp shows (a native
    /// window is drawn over any Avalonia control), and the editor popup is hidden or shown here, when the stamp changes, not when the engine next notices.</summary>
    public void ShowOverlay(bool show, bool light)
    {
        var changed = show != _overlay.IsVisible;
        _overlay.Update(show, light);
        ApplyEditorVisibility();
        if (changed) _setPopupVisible(_popup(), PluginEditorVisibility.EditorShows(show, _covered));
    }

    /// <summary>Hides the native editor and its engine popup while another window covers the FX window (the MIDI processing window), and restores them
    /// when it closes. The host area is hidden too, so the engine's own placement loop keeps the popup hidden instead of showing it again.</summary>
    public bool SetCovered(bool covered)
    {
        if (_covered == covered) return false;
        _covered = covered;
        ApplyEditorVisibility();
        _setPopupVisible(_popup(), PluginEditorVisibility.EditorShows(_overlay.IsVisible, covered));
        return true;
    }

    private void ApplyEditorVisibility() => Surface.IsVisible = PluginEditorVisibility.EditorShows(_overlay.IsVisible, _covered);

    /// <summary>The docked editor reported its size (pixels): grows the window to show it whole, then lays out.</summary>
    public void SetEditorSize(int widthPixels, int heightPixels)
    {
        _editorPixels = (widthPixels, heightPixels);
        _horizontal.Value = 0; _vertical.Value = 0;
        GrowWindow();
        Fit();
    }

    private double Scale => (this.GetVisualRoot() as TopLevel)?.RenderScaling ?? 1;

    private void GrowWindow()
    {
        if (this.GetVisualRoot() is not Window window || window.WindowState != WindowState.Normal || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var screen = window.Screens.ScreenFromWindow(window);
        if (screen is null) return;
        var scale = Scale;
        var area = screen.WorkingArea;
        var work = new PluginEditorFitMath.Rectangle(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
        var now = new PluginEditorFitMath.Rectangle(window.Position.X / scale, window.Position.Y / scale, window.Bounds.Width, window.Bounds.Height);
        var chrome = (Math.Max(0, now.Width - Bounds.Width), Math.Max(0, now.Height - Bounds.Height));
        var fit = PluginEditorFitMath.FitWindow(now, chrome, PluginEditorFitMath.ToDip(_editorPixels.W, _editorPixels.H, scale, scale), work);
        if (fit == now) return;
        window.Position = new PixelPoint((int)Math.Round(fit.Left * scale), (int)Math.Round(fit.Top * scale));
        window.Width = fit.Width; window.Height = fit.Height;
    }

    /// <summary>Sizes the host frame to the visible part of the editor and shows the bars the rest needs.</summary>
    public void Fit()
    {
        if (!_frame.IsVisible || _editorPixels.W <= 0)
        {
            _horizontal.IsVisible = _vertical.IsVisible = false;
            _horizontal.Maximum = _vertical.Maximum = 0;
            return;
        }
        var scale = Scale;
        var (w, h) = PluginEditorFitMath.ToDip(_editorPixels.W, _editorPixels.H, scale, scale);
        var layout = PluginEditorFitMath.Scroll(Bounds.Width, Bounds.Height, w, h, ScrollBarThickness);
        _frame.Width = layout.ViewWidth; _frame.Height = layout.ViewHeight;
        SetBar(_horizontal, layout.RangeX, layout.ViewWidth);
        SetBar(_vertical, layout.RangeY, layout.ViewHeight);
    }

    private static void SetBar(ScrollBar bar, double range, double view)
    {
        bar.IsVisible = range > 0;
        bar.ViewportSize = view;
        bar.LargeChange = Math.Max(1, view * 0.9);
        bar.Maximum = range;   // a smaller range pulls the value in, which moves the view
    }

    // The plug-in view is the first child of the engine editor popup; it belongs to the engine process, so the move is asynchronous.
    private void MoveView()
    {
        var scale = Scale;
        var (x, y) = PluginEditorFitMath.ViewOrigin(_horizontal.Value, _vertical.Value, scale, scale);
        PluginEditorWin32.MoveEditorView(_popup(), x, y);
    }
}
