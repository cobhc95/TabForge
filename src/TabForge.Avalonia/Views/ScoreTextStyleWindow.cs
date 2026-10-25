using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Score text styling per area: the area list on the left, and font, size, bold, italic, colour and outline rows for the selected area.
/// The colour swatches open <see cref="ColourPickerPopup"/>. OK closes with true, Cancel and Esc with null.
/// Owns: the window's controls, their layout and keeping them in step with the presenter. Does not own: the style rules
/// (<see cref="ScoreTextStylePresenter"/>), the styles dictionary (the caller's) or restoring it on Cancel (the caller keeps a copy).
/// Sample state: the preview has no score, so <c>preview</c> is optional and nothing is drawn.
/// Tests: TabForge.Avalonia.Tests.ScoreTextStyleAndInstrumentPickerTests.
/// </summary>
public sealed class ScoreTextStyleWindow : Window
{
    private readonly ScoreTextStylePresenter _presenter;
    private readonly Action? _preview;
    private readonly ListBox _areas = new() { Width = DialogMetrics.TextStyleAreasWidth, Margin = DialogMetrics.TextStyleAreasGap };
    private readonly ComboBox _font = new() { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _size = new() { Minimum = 50, Maximum = 250, TickFrequency = 5, IsSnapToTickEnabled = true, Width = DialogMetrics.TextStyleSizeWidth };
    private readonly TextBlock _sizeText = ReadOut();
    private readonly CheckBox _bold = new() { Content = "Bold", IsThreeState = true };
    private readonly CheckBox _italic = new() { Content = "Italic", IsThreeState = true, Margin = DialogMetrics.TextStyleItalicGap };
    private readonly ColourButton _colour, _outlineColour;
    private readonly Slider _outline = new() { Minimum = 0, Maximum = 4, TickFrequency = 0.25, IsSnapToTickEnabled = true, Width = DialogMetrics.TextStyleOutlineWidth, Margin = DialogMetrics.TextStyleOutlineGap };
    private readonly TextBlock _outlineText = ReadOut();
    private bool _syncing;

    /// <param name="styles">The per-area styles, edited in place.</param>
    /// <param name="fonts">The font names the font box lists after "(inherit)".</param>
    /// <param name="preview">Called after each change, the way the WPF window redraws the score.</param>
    /// <param name="recentColours">The persisted recent colour list the pickers share with Preferences; a window-only list when null.</param>
    public ScoreTextStyleWindow(IDictionary<string, ScoreTextAreaStyle> styles, IEnumerable<string> fonts, Action? preview = null, IList<string>? recentColours = null)
    {
        _recentColours = recentColours ?? new List<string>();
        _presenter = new ScoreTextStylePresenter(styles);
        _colour = new ColourButton(ColourHex.FormatAuto(_presenter.TextDefault), _recentColours) { Chooser = _ => Choose(text: true) };
        AutomationProperties.SetName(_colour, "Text colour");
        _outlineColour = new ColourButton(ColourHex.FormatAuto(_presenter.OutlineDefault), _recentColours) { Chooser = _ => Choose(text: false) };
        AutomationProperties.SetName(_outlineColour, "Outline colour");
        _preview = preview;
        PickAsync = ChooseColour;
        Title = "Score text & fonts";
        Width = DialogMetrics.TextStyleWindowWidth; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        foreach (var (_, label) in ScoreTextStylePresenter.Areas) _areas.Items.Add(label);
        _font.Items.Add("(inherit)");
        foreach (var name in fonts.OrderBy(n => n)) _font.Items.Add(name);
        ToolTip.SetTip(_bold, "Filled = inherit");
        ToolTip.SetTip(_italic, "Filled = inherit");

        var clear = new Button { Content = "Inherit", Margin = DialogMetrics.TextStyleInheritGap };
        var reset = new Button { Content = "Reset this area", HorizontalAlignment = HorizontalAlignment.Left, Margin = DialogMetrics.TextStyleResetGap };
        var form = new StackPanel
        {
            Children =
            {
                Row("Font", _font), Row("Size", Line(_size, _sizeText)), Row("Style", Line(_bold, _italic)),
                Row("Colour", Line(_colour, clear)), Row("Outline", Line(_outlineColour, _outline, _outlineText)), reset, ButtonRow(),
            },
        };
        DockPanel.SetDock(_areas, Dock.Left);
        Content = new DockPanel { Margin = DialogMetrics.TextStyleInset, Children = { _areas, form } };

        _areas.SelectionChanged += (_, _) => { _presenter.SelectedArea = Math.Max(0, _areas.SelectedIndex); Load(); };
        _font.SelectionChanged += (_, _) => { if (!_syncing && _font.SelectedItem is string chosen) { _font.Text = chosen; Changed(); } };
        _font.LostFocus += (_, _) => Changed();
        _size.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Changed(); };
        _outline.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Changed(); };
        // Double-click restores the default: 100 percent size, outline off.
        AvaloniaSliderReset.OnDoubleTap(_size, 100);
        AvaloniaSliderReset.OnDoubleTap(_outline, 0);
        _bold.IsCheckedChanged += (_, _) => Changed();
        _italic.IsCheckedChanged += (_, _) => Changed();
        _colour.Picked += hex => Apply(hex, c => _presenter.SetColour(c));
        _outlineColour.Picked += hex => Apply(hex, c => _presenter.SetOutlineColour(c));
        clear.Click += (_, _) => { _presenter.ClearColour(); Refresh(); };
        reset.Click += (_, _) => { _presenter.ResetArea(); Refresh(); };
        _areas.SelectedIndex = 0;
    }

    /// <summary>Opens the colour picker under the clicked swatch; tests replace it to answer without a popup.</summary>
    public Func<Control, Rgba, Task<Rgba?>> PickAsync { get; set; }

    private readonly IList<string> _recentColours;

    // The Default button answers the area's real default colour, not the start colour; an area without a colour opens on white (text) or black (outline).
    private Task<Rgba?> ChooseColour(Control anchor, Rgba start) =>
        ColourPickerPopup.PickRgbaAsync(anchor, start, _recentColours, anchor == _outlineColour ? _presenter.OutlineDefault : _presenter.TextDefault,
            colour => { if (anchor == _outlineColour) _presenter.SetOutlineColour(colour); else _presenter.SetColour(colour); Refresh(); });

    private async Task<string?> Choose(bool text)
    {
        // The popup previews each colour on the score text; Cancel puts the area's colour and outline back as they were.
        var style = _presenter.Current;
        var (colour0, outline0, thickness0) = (style.Colour, style.OutlineColour, style.OutlineThickness);
        var chosen = await PickAsync(text ? _colour : _outlineColour, text ? _presenter.TextPickerStart : _presenter.OutlinePickerStart);
        if (chosen is null)
        {
            (style.Colour, style.OutlineColour, style.OutlineThickness) = (colour0, outline0, thickness0);
            Refresh();
        }
        return chosen is { } colour ? ColourHex.FormatAuto(colour) : null;
    }

    private void Apply(string hex, Action<Rgba> apply)
    {
        if (!ColourHex.TryParse(hex, out var colour)) return;
        apply(colour);
        Refresh();
    }

    private static TextBlock ReadOut() => new() { MinWidth = DialogMetrics.TextStyleReadOutWidth, Margin = DialogMetrics.TextStyleInheritGap, VerticalAlignment = VerticalAlignment.Center };

    private static StackPanel Line(params Control[] parts)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.AddRange(parts);
        return line;
    }

    private static DockPanel Row(string label, Control control) => new()
    {
        Margin = DialogMetrics.TextStyleRowGap,
        Children = { new TextBlock { Text = label, Width = DialogMetrics.TextStyleLabelWidth, VerticalAlignment = VerticalAlignment.Center }, control },
    };

    private StackPanel ButtonRow()
    {
        var ok = new Button { Content = "OK", Width = DialogMetrics.TextStyleButtonWidth, IsDefault = true, Margin = DialogMetrics.TextStyleOkGap };
        var cancel = new Button { Content = "Cancel", Width = DialogMetrics.TextStyleButtonWidth, IsCancel = true };
        ok.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(null);
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = DialogMetrics.TextStyleButtonsGap, Children = { ok, cancel } };
    }

    private void Refresh() { Load(); _preview?.Invoke(); }

    private void Load()
    {
        _syncing = true;
        var style = _presenter.Current;
        _font.Text = _presenter.FontText;
        _size.Value = _presenter.SizeValue;
        _sizeText.Text = ScoreTextStylePresenter.SizeLabel(_size.Value);
        _bold.IsChecked = style.Bold; _italic.IsChecked = style.Italic;
        Paint(_colour, _presenter.TextSwatch, _presenter.TextDefault);
        Paint(_outlineColour, _presenter.OutlineSwatch, _presenter.OutlineDefault);
        _outline.Value = style.OutlineThickness;
        _outlineText.Text = ScoreTextStylePresenter.OutlineLabel(style.OutlineThickness);
        _syncing = false;
    }

    private void Changed()
    {
        if (_syncing) return;
        _presenter.SetFont(_font.Text);
        _presenter.SetSize(_size.Value);
        _presenter.SetBold(_bold.IsChecked); _presenter.SetItalic(_italic.IsChecked);
        _presenter.SetOutlineThickness(_outline.Value);
        _sizeText.Text = ScoreTextStylePresenter.SizeLabel(_size.Value);
        _outlineText.Text = ScoreTextStylePresenter.OutlineLabel(_outline.Value);
        _preview?.Invoke();
    }

    private static void Paint(ColourButton button, Rgba shown, Rgba defaultColour)
    {
        button.DefaultHex = ColourHex.FormatAuto(defaultColour);
        button.Show(ColourHex.FormatAuto(shown));
    }
}
