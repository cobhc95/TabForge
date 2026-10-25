using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform;
using TabForge.Services;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: the one colour setting control: a dropdown-looking button of the colour swatch, the colour's name (ColourNames) and a "▾", the hover and press
//   shades (HoverSurface, PressSurface), and opening ColourPickerPopup under itself on click, Enter or Space (Button's own key handling).
// Does not own: the colour rules and the popup (ColourPickerPresenter, ColourPickerPopup), the name rules (ColourNames) or what a picked colour is stored as (the caller).
// Tests: TabForge.Avalonia.Tests.ColourButtonTests.
public sealed class ColourButton : Button
{
    private const double SwatchCorner = 3;
    private const string BorderPartName = "Bd";
    private readonly Border _swatch;
    private readonly TextBlock _name = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private IBrush Rest() => this.TryFindResource("Panel2Brush", ActualThemeVariant, out var found) && found is IBrush brush ? brush : AvaloniaPalette.TransparentBrush;

    /// <param name="defaultHex">The colour the popup's Default button answers; the name "Default" when the value equals it.</param>
    /// <param name="recent">The recent colour list the popup shares with the other pickers.</param>
    public ColourButton(string defaultHex, IList<string> recent)
    {
        DefaultHex = defaultHex;
        Recent = recent;
        _swatch = new Border
        {
            Width = ChromeMetrics.SwatchSize, Height = ChromeMetrics.SwatchSize, CornerRadius = new CornerRadius(SwatchCorner),
            BorderThickness = DialogMetrics.SwatchOutline, VerticalAlignment = VerticalAlignment.Center,
        };
        _swatch[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        var arrow = new TextBlock { Text = "▾", VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_swatch, Dock.Left);
        DockPanel.SetDock(arrow, Dock.Right);
        _swatch.WithMargin(DialogMetrics.ColourButtonSwatchGap);
        arrow.WithMargin(DialogMetrics.ColourButtonArrowGap);
        line.Children.Add(_swatch);
        line.Children.Add(arrow);
        line.Children.Add(_name);
        Content = line;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
        BorderThickness = DialogMetrics.SwatchOutline;
        this[!BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        this[!BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        ToolTip.SetTip(this, "Open the colour picker.");
        // The one-border template paints the control's own fill, so the hover and press shades below show (the Fluent template's pointer-over fill would cover them).
        Template = AvaloniaButtonTemplates.SingleBorder(BorderPartName);
        _setKeyHeld = HoverShade.Wire(this, brush => Background = brush, Rest);
        Chooser = hex =>
        {
            ActivePicker = new ColourPickerPopup(hex, DefaultHex, Recent);
            ActivePicker.Applied += hex => { Show(hex); Previewed?.Invoke(hex); };   // a live preview only; Picked is raised once, from the answer
            return ActivePicker.AskAsync(this);
        };
        Click += (_, _) => UiTasks.Forget(OpenAsync(), "Opening the colour picker", text => Trace.Error(Trace.Ui, text));
        Show(defaultHex);
    }

    private readonly Action<bool> _setKeyHeld;
    private bool _focusRing;

    // Keyboard look: Tab or arrow focus shows the accent ring (a thicker accent border), and Enter or Space holds the pressed shade until released, as a pointer press does.
    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        if (e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional)) return;
        _focusRing = true;
        BorderThickness = DialogMetrics.SelectedOutline;
        this[!BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
    }

    protected override void OnLostFocus(global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!_focusRing) return;
        _focusRing = false;
        BorderThickness = DialogMetrics.SwatchOutline;
        this[!BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space) _setKeyHeld(true);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.Enter or Key.Space) _setKeyHeld(false);
    }

    /// <summary>The colour the popup's Default button answers.</summary>
    public string DefaultHex { get; set; }

    /// <summary>The recent colour list handed to the popup.</summary>
    public IList<string> Recent { get; }

    /// <summary>The colour text now shown.</summary>
    public string Value { get; private set; } = "";

    /// <summary>Opens the colour popup on the current value and answers the colour text, or null; tests replace it to answer without a popup.</summary>
    public Func<string, Task<string?>> Chooser { get; set; }

    /// <summary>The popup the default chooser opened last, for tests and captures.</summary>
    public ColourPickerPopup? ActivePicker { get; private set; }

    /// <summary>Raised with the colour text each time the open popup previews one (a click, the square, a code), and with the opening colour on Cancel; nothing is final yet.</summary>
    public event Action<string>? Previewed;

    /// <summary>Raised with the colour text after the user picks one (the button already shows it).</summary>
    public event Action<string>? Picked;

    /// <summary>The name shown beside the swatch.</summary>
    public string DisplayName => _name.Text ?? "";

    /// <summary>Shows <paramref name="hex"/>: its swatch and its name. Raises nothing.</summary>
    public void Show(string hex)
    {
        Value = hex;
        _swatch.Background = Color.TryParse(hex, out var colour) ? new SolidColorBrush(colour) : null;
        _name.Text = ColourNames.NameOf(hex, DefaultHex);
        AutomationProperties.SetHelpText(this, _name.Text);
    }

    /// <summary>Opens the popup; the colours it previews are shown, and the colour it answers is raised once through <see cref="Picked"/> (nothing for Cancel, which shows the opening colour again).</summary>
    public async Task OpenAsync()
    {
        var opening = Value;
        var chosen = await Chooser(opening);
        Show(chosen ?? opening);
        if (chosen is not null && chosen != opening) Picked?.Invoke(chosen);
    }
}
