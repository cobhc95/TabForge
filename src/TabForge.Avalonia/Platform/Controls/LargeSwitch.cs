using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: the on/off pill switch (a 48x26 track, 20 thumb): on is the accent fill with the on-accent thumb, off the neutral strong outline fill with the thumb at the left;
//   Space and Enter toggle it, a keyboard focus shows the app's focus ring. Colours are theme brushes (AccentBrush, OnAccentBrush, CheckBoxBorderBrush, TextBrush, FocusRingBrush),
//   so the user's accent and both themes apply. The change is instant. No cursor change.
// Does not own: what the state means (the caller binds IsChecked), the caption beside it.
// Tests: no dedicated test class.
public sealed class LargeSwitch : ToggleButton
{
    public const double TrackWidth = ChromeMetrics.LargeSwitchTrackWidth, TrackHeight = ChromeMetrics.LargeSwitchTrackHeight, ThumbSize = ChromeMetrics.LargeSwitchThumbSize;
    private const double ThumbInset = (TrackHeight - ThumbSize) / 2;
    private const double RingThickness = 2;
    private const string TrackPart = "Track", ThumbPart = "Thumb";

    private Border? _track, _thumb;

    public LargeSwitch()
    {
        Width = TrackWidth;
        Height = TrackHeight;
        Padding = default;
        Focusable = true;
        Template = new FuncControlTemplate<LargeSwitch>((owner, scope) =>
        {
            _thumb = new Border
            {
                Name = ThumbPart, Width = ThumbSize, Height = ThumbSize, CornerRadius = new CornerRadius(ThumbSize / 2),
                Margin = new Thickness(ThumbInset, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            _track = new Border
            {
                Name = TrackPart, CornerRadius = new CornerRadius(TrackHeight / 2), BorderThickness = new Thickness(RingThickness), Child = _thumb,
            };
            _track.RegisterInNameScope(scope);
            _thumb.RegisterInNameScope(scope);
            Paint();
            return _track;
        });
        Styles.Add(new Style(x => x.OfType<LargeSwitch>().Class(":focus-visible").Template().OfType<Border>().Name(TrackPart))
        {
            Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("FocusRingBrush")) },
        });
    }

    /// <summary>A subclass has its own style key and would get no ToggleButton theme: use the stock one (the template paints everything).</summary>
    protected override Type StyleKeyOverride => typeof(ToggleButton);

    /// <summary>Sets the accessible name and tooltip.</summary>
    public LargeSwitch Named(string name, string tip)
    {
        AutomationProperties.SetName(this, name);
        ToolTip.SetTip(this, tip);
        return this;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty) Paint();
    }

    private void Paint()
    {
        if (_track is null || _thumb is null) return;
        var on = IsChecked == true;
        _track[!Border.BackgroundProperty] = new DynamicResourceExtension(on ? "AccentBrush" : "CheckBoxBorderBrush");
        _thumb[!Border.BackgroundProperty] = new DynamicResourceExtension(on ? "OnAccentBrush" : "TextBrush");
        _thumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
}
