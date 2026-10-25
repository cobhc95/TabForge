using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the colour picker popup under a colour swatch, the one way every colour row and preview entry picks a colour: the ready-made colours, the
//   recent colours, the More colours part (square, hue strip, opacity where the colour can be see-through, code box), the X close button and the
//   Default and Cancel buttons. Every pick applies at once (<see cref="Applied"/>). A double click on a colour, X, Enter and a click outside close and answer the
//   colour now shown; Default answers the default text; Cancel and Esc undo back to the opening colour and answer null.
// Does not own: the colour rules, the recent list and the messages (ColourPickerPresenter), or what the caller does with the answer.
// Tests: TabForge.Avalonia.Tests.ColourPickerPopupTests.
public sealed class ColourPickerPopup
{
    /// <summary>Control names, for the tests and the automation tree.</summary>
    public const string PresetsName = "ColourPresets", RecentName = "ColourRecent", MoreName = "ColourMore", SquareName = "ColourSquare", HueName = "ColourHue",
        OpacityName = "ColourOpacity", CloseName = "ColourClose", CodeName = "ColourCode", ErrorName = "ColourCodeError", MoreLinkName = "ColourMoreLink";

    private readonly ColourPickerPresenter _presenter;
    private readonly string _initial;
    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private EventHandler<VisualTreeAttachmentEventArgs>? _anchorDetached;   // this picker's handler on the anchor: removed when the popup closes
    private readonly Border _panel = new();
    private readonly WrapPanel _recent = new() { Name = RecentName };
    private readonly StackPanel _more = new() { Name = MoreName, IsVisible = false };
    private readonly Button _moreLink = new() { Name = MoreLinkName, Content = "More colours…", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ColourSquare _square = new() { Name = SquareName, Width = DialogMetrics.ColourSquareWidth, Height = DialogMetrics.ColourSquareHeight };
    private readonly HueStrip _hue = new() { Name = HueName, Width = DialogMetrics.HueStripWidth, Height = DialogMetrics.ColourSquareHeight };
    private readonly Slider _opacity = new() { Name = OpacityName, Minimum = 0, Maximum = 100, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _opacityRow = new();
    private readonly TextBox _code = new() { Name = CodeName, Width = DialogMetrics.CodeBoxWidth, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { Name = ErrorName, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly List<(Button Cell, Rgba Colour)> _cells = new();
    private Popup? _popup;
    private bool _updating, _done;

    /// <param name="initial">The colour text the picker opens with; white when it does not read.</param>
    /// <param name="defaultValue">The text the Default button answers.</param>
    /// <param name="recent">The recent colour texts, newest first; an answered colour joins the front of the list.</param>
    public ColourPickerPopup(string initial, string defaultValue, IList<string> recent)
    {
        _presenter = new ColourPickerPresenter(initial, defaultValue, recent);
        _initial = initial;
        _panel.Width = DialogMetrics.ColourPopupWidth;
        _panel.CornerRadius = new CornerRadius(DialogMetrics.ColourPopupCorner);
        _panel.BorderThickness = DialogMetrics.SwatchOutline;
        _panel[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        _panel[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        _panel[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _panel.Focusable = true;
        _error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");
        TextLinkButton.Apply(_moreLink);

        var presets = new UniformGrid { Name = PresetsName, Columns = DialogMetrics.ColourPerRow };
        foreach (var (name, hex, colour) in _presenter.Presets) presets.Children.Add(Cell(name, hex, colour));
        foreach (var (text, colour) in _presenter.RecentColours) _recent.Children.Add(Cell(text, text, colour));

        var content = new StackPanel().WithMargin(DialogMetrics.ColourPopupInset);
        content.Children.Add(Header());
        content.Children.Add(presets);
        if (_recent.Children.Count > 0)
        {
            content.Children.Add(Heading("Recent").WithMargin(DialogMetrics.ColourPartGap));
            content.Children.Add(_recent);
        }
        content.Children.Add(_moreLink.WithMargin(DialogMetrics.ColourPartGap));
        BuildMore();
        content.Children.Add(_more);
        content.Children.Add(ButtonRow().WithMargin(DialogMetrics.ColourPartGap));
        _panel.Child = content;

        _moreLink.Click += (_, _) => ShowMore();
        _square.Moved += (x, y) => Apply(_presenter.Hue, x * 100, (1 - y) * 100, _presenter.OpacityPercent);
        _hue.Moved += (_, y) => Apply(y * 359, _presenter.SaturationPercent, _presenter.BrightnessPercent, _presenter.OpacityPercent);
        _opacity.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_updating) Apply(_presenter.Hue, _presenter.SaturationPercent, _presenter.BrightnessPercent, _opacity.Value);
        };
        _code.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || _updating) return;
            _presenter.TypeCode(_code.Text);
            ShowState(keepCode: true);
            Publish();
        };
        _panel.AddHandler(InputElement.KeyDownEvent, KeyPressed, RoutingStrategies.Tunnel);
        ShowState();
    }

    /// <summary>The panel of the popup, for tests and captures.</summary>
    public Control Panel => _panel;

    /// <summary>
    /// The one shared way a caller that holds a colour (not colour text) picks one: opens the picker under <paramref name="anchor"/> on
    /// <paramref name="start"/> and answers the chosen colour, or null for Cancel or Esc; <paramref name="preview"/> gets each colour the open popup shows.
    /// </summary>
    public static async Task<Rgba?> PickRgbaAsync(Control anchor, Rgba start, IList<string> recent, Rgba? defaultColour = null, Action<Rgba>? preview = null)
    {
        var text = ColourHex.FormatAuto(start);
        var picker = new ColourPickerPopup(text, ColourHex.FormatAuto(defaultColour ?? start), recent);
        if (preview is not null) picker.Applied += hex => { if (ColourHex.TryParse(hex, out var shown)) preview(shown); };
        var chosen = await picker.AskAsync(anchor);
        return ColourHex.TryParse(chosen, out var colour) ? colour : null;
    }

    /// <summary>Raised with the colour text each time the colour changes (a pick, the square, hue, opacity or a readable code), and with the opening colour on Cancel: a live preview only, the caller commits the answer.</summary>
    public event Action<string>? Applied;

    /// <summary>The popup; null before <see cref="AskAsync"/>.</summary>
    public Popup? Popup => _popup;

    // The answer still pending on each anchor: a second picker on the same swatch (a double click) joins it and opens no second popup.
    private static readonly AttachedProperty<Task<string?>?> PendingAnswerProperty = AvaloniaProperty.RegisterAttached<ColourPickerPopup, Control, Task<string?>?>("PendingAnswer");

    /// <summary>
    /// Opens the picker at <paramref name="placement"/> of <paramref name="anchor"/> (under it by default) and answers when it closes: the colour text
    /// for Use colour, the default text for Default, null for Cancel, Esc or a click outside. An answered colour joins the recent list. One popup per picker: a second call while it is open (or after) answers the same result.
    /// </summary>
    public Task<string?> AskAsync(Control anchor, PlacementMode placement = PlacementMode.Bottom)
    {
        if (_popup is not null) return _result.Task;
        if (anchor.GetValue(PendingAnswerProperty) is { } pending) return pending;
        _popup = DialogMetrics.AnchoredPopup(anchor, _panel, placement);
        anchor.SetValue(PendingAnswerProperty, _result.Task);
        _popup.Opened += (_, _) => _panel.Focus();
        _popup.Closed += (_, _) =>
        {
            anchor.ClearValue(PendingAnswerProperty);
            anchor.DetachedFromVisualTree -= _anchorDetached;
            _anchorDetached = null;
            ((ISetLogicalParent)_popup).SetParent(null);
            Finish(_presenter.UseColourText);
        };
        // an anchor removed with its popup open never raises Closed: its next picker must not join a dead answer, and this one answers null
        _anchorDetached = (_, _) => { anchor.ClearValue(PendingAnswerProperty); Finish(null); };
        anchor.DetachedFromVisualTree += _anchorDetached;
        ((ISetLogicalParent)_popup).SetParent(anchor);
        _popup.IsOpen = true;
        return _result.Task;
    }

    /// <summary>Expands the popup with the square, hue strip, opacity (where it applies) and code box.</summary>
    public void ShowMore()
    {
        _more.IsVisible = true;
        _moreLink.IsVisible = false;
    }

    private static TextBlock Heading(string text)
    {
        var heading = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold };
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return heading;
    }

    private Control Header()
    {
        var close = new Button { Name = CloseName, Content = "✕", HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => Finish(_presenter.UseColourText);
        DockPanel.SetDock(close, Dock.Right);
        return new DockPanel { Children = { close, Heading("Colours") } };
    }

    private Button Cell(string name, string text, Rgba colour)
    {
        var cell = new Button { Width = DialogMetrics.ColourCellSize, Height = DialogMetrics.ColourCellSize, Background = new SolidColorBrush(AvaloniaPalette.ColorOf(colour)) }.WithMargin(DialogMetrics.ColourCellGap);
        // The one-border template (no Button look) keeps the swatch colour under the pointer; the hover shows as a ring instead (Outline).
        cell.Template = AvaloniaButtonTemplates.SingleBorder("Bd");
        cell.PointerEntered += (_, _) => Outline(cell, colour, hovered: true);
        cell.PointerExited += (_, _) => Outline(cell, colour, hovered: false);
        ToolTip.SetTip(cell, name);
        AutomationProperties.SetName(cell, name);
        cell.Click += (_, _) => { _presenter.ChoosePreset(text); ShowState(); Publish(); };
        cell.DoubleTapped += (_, _) => Finish(_presenter.UseColourText);
        _cells.Add((cell, colour));
        return cell;
    }

    private void BuildMore()
    {
        AutomationProperties.SetName(_square, "Saturation and brightness");
        AutomationProperties.SetName(_hue, "Hue");
        AutomationProperties.SetName(_opacity, "Opacity");
        AutomationProperties.SetName(_code, "Code");
        _opacityRow.Children.Add(new TextBlock { Text = "Opacity" });
        _opacityRow.Children.Add(_opacity);
        _opacityRow.IsVisible = _presenter.SupportsOpacity;
        var surfaces = new StackPanel { Orientation = Orientation.Horizontal, Children = { _square.WithMargin(DialogMetrics.ColourSquareGap), _hue } };
        var code = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "Code", VerticalAlignment = VerticalAlignment.Center }.WithMargin(DialogMetrics.ColourButtonGap), _code } };
        _more.Children.Add(surfaces.WithMargin(DialogMetrics.ColourPartGap));
        _more.Children.Add(_opacityRow.WithMargin(DialogMetrics.ColourPartGap));
        _more.Children.Add(code.WithMargin(DialogMetrics.ColourPartGap));
        _more.Children.Add(_error);
    }

    private StackPanel ButtonRow()
    {
        var reset = new Button { Content = "Default" }.WithMargin(DialogMetrics.ColourButtonGap);
        var cancel = new Button { Content = "Cancel" }.WithMargin(DialogMetrics.ColourButtonGap);
        reset.Click += (_, _) => Finish(_presenter.DefaultValue);
        cancel.Click += (_, _) => Cancel();
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { reset, cancel } };
    }

    private void KeyPressed(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel();
        else if (e.Key == Key.Enter && e.Source is not Button) Use();
        else return;
        e.Handled = true;
    }

    /// <summary>Undoes every change back to the opening colour and closes, answering null.</summary>
    private void Cancel()
    {
        if (_done) return;
        Applied?.Invoke(_initial);
        Finish(null);
    }

    /// <summary>Tells the caller the colour now shown, unless the code box holds text that is not a colour.</summary>
    private void Publish()
    {
        if (_presenter.CodeError is null) Applied?.Invoke(_presenter.UseColourText);
    }

    /// <summary>Closes keeping the colour now shown, unless the code box holds text that is not a colour (the message stays up instead).</summary>
    private void Use()
    {
        if (_presenter.CodeError is null) Finish(_presenter.UseColourText);
    }

    private void Finish(string? answer)
    {
        if (_done) return;
        _done = true;
        // Recent gets only a colour that was kept and differs from the opening one.
        if (answer is not null && !(ColourHex.TryParseStrict(answer, out var kept) && ColourHex.TryParseStrict(_initial, out var opening) && kept == opening)) _presenter.Remember(answer);
        if (_popup is { IsOpen: true }) _popup.IsOpen = false;
        _result.TrySetResult(answer);
    }

    private void Apply(double hue, double saturation, double brightness, double opacity)
    {
        _presenter.Apply(hue, saturation, brightness, opacity);
        ShowState();
        Publish();
    }

    /// <summary>Copies the presenter state to the controls. The change handlers are muted while it runs; the code box keeps its text when the change came from it.</summary>
    private void ShowState(bool keepCode = false)
    {
        var wasUpdating = _updating;
        _updating = true;
        _square.ShowHue(_presenter.Hue);
        _square.Place(_presenter.SaturationPercent / 100, 1 - _presenter.BrightnessPercent / 100);
        _hue.Place(0.5, _presenter.Hue / 359);
        _opacity.Value = _presenter.OpacityPercent;
        if (!keepCode) _code.Text = _presenter.CodeText;
        _error.Text = _presenter.CodeError;
        _error.IsVisible = _presenter.CodeError is not null;
        foreach (var (cell, colour) in _cells) Outline(cell, colour, cell.IsPointerOver);
        _updating = wasUpdating;
    }

    // The selected swatch has the thick text-colour ring; a hovered one the accent outline (the Button look's hover border); the others the soft outline.
    private void Outline(Button cell, Rgba colour, bool hovered)
    {
        var current = colour == _presenter.Colour;
        cell.BorderThickness = current ? DialogMetrics.SelectedOutline : DialogMetrics.SwatchOutline;
        cell[!TemplatedControl.BorderBrushProperty] = new DynamicResourceExtension(current ? "TextPrimary" : hovered ? "AccentBrush" : "BorderSoftBrush");
    }
}
