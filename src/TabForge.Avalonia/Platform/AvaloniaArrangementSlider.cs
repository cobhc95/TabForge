using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform;

// Owns: ArrangementSlider control template and its sizes.
// Does not own: the colours (palette keys) or the state rule (Core look).

/// <summary>
/// The template of sliders with the class "ArrangementSlider" (the mixer faders): App.xaml's compact slider, an 8 DIP track with a 6 DIP blue fill
/// up to an 18x14 DIP thumb that prints the value. The thumb carries the class of the Core look "ArrangementSliderThumb" and names its face "ThumbFace",
/// so the look's hover rule recolours it. Owns: this template and its sizes. Does not own: the colours (palette keys) or the state rule (Core look).
/// Tests: TabForge.Avalonia.Tests.CheckSliderTemplateTests.
/// </summary>
public static class AvaloniaArrangementSlider
{
    public const string ClassName = "ArrangementSlider";
    private const string ThumbLook = "ArrangementSliderThumb";
    private const double SliderHeight = 24, TrackHeight = 8, TrackCorner = 4, FillHeight = 6, FillCorner = 3, ThumbWidth = 18, ThumbHeight = 14, FaceMinWidth = 12, FaceCorner = 2, ValueFontSize = 9;
    private const double ThumbGlowBlur = 3, ThumbGlowOpacity = 0.4;
    private const string ThumbGlowPaletteKey = "SliderThumbGlow";

    public static Style Build() => new(x => x.OfType<Slider>().Class(ClassName).Class(":horizontal"))
    {
        Setters =
        {
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Slider>(Content)),
            new Setter(Layoutable.HeightProperty, SliderHeight),
            new Setter(Layoutable.MinHeightProperty, SliderHeight),
            new Setter(InputElement.FocusableProperty, false),
        },
    };

    private static Control Content(Slider slider, INameScope scope)
    {
        var trackBar = new Border { Name = "TrackBar", Height = TrackHeight, CornerRadius = new CornerRadius(TrackCorner), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        Bind(trackBar, Border.BackgroundProperty, slider, "TrackSliderTrack");
        Bind(trackBar, Border.BorderBrushProperty, slider, "TrackSliderTrackBorder");
        var thumb = new Thumb { Name = "thumb", Width = ThumbWidth, Height = ThumbHeight, Template = new FuncControlTemplate<Thumb>((t, s) => ThumbFace(t, s, slider)) };
        thumb.Classes.Add(ThumbLook);
        var less = new RepeatButton { Name = "PART_DecreaseButton", Focusable = false, HorizontalAlignment = HorizontalAlignment.Stretch, Template = Fill("SliderTrackFill") };
        var more = new RepeatButton { Name = "PART_IncreaseButton", Focusable = false, HorizontalAlignment = HorizontalAlignment.Stretch, Template = Fill("TrackSliderTrack") };
        var bar = new Track { Name = "PART_Track", Orientation = Orientation.Horizontal, Thumb = thumb, DecreaseButton = less, IncreaseButton = more, VerticalAlignment = VerticalAlignment.Center };
        bar.Bind(RangeBase.MinimumProperty, slider.GetObservable(RangeBase.MinimumProperty), BindingPriority.Template);
        bar.Bind(RangeBase.MaximumProperty, slider.GetObservable(RangeBase.MaximumProperty), BindingPriority.Template);
        bar.Bind(RangeBase.ValueProperty, slider.GetObservable(RangeBase.ValueProperty), BindingPriority.Template);
        bar.RegisterInNameScope(scope); thumb.RegisterInNameScope(scope); less.RegisterInNameScope(scope); more.RegisterInNameScope(scope); trackBar.RegisterInNameScope(scope);
        return new Grid { Height = SliderHeight, Background = Brushes.Transparent, Children = { trackBar, bar } };
    }

    // A pill that fills the track side of the thumb.
    private static FuncControlTemplate<RepeatButton> Fill(string brushKey) => new((button, _) =>
    {
        var pill = new Border { Height = FillHeight, CornerRadius = new CornerRadius(FillCorner), VerticalAlignment = VerticalAlignment.Center };
        Bind(pill, Border.BackgroundProperty, button, brushKey);
        return pill;
    });

    private static Control ThumbFace(Thumb thumb, INameScope scope, Slider slider)
    {
        var value = new TextBlock
        {
            FontSize = ValueFontSize, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, -1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Bind(value, TextBlock.ForegroundProperty, thumb, "SliderThumbText");
        value.Text = slider.Value.ToString("0", CultureInfo.CurrentCulture);
        slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) value.Text = slider.Value.ToString("0", CultureInfo.CurrentCulture); };
        var face = new Border
        {
            Name = "ThumbFace", MinWidth = FaceMinWidth, CornerRadius = new CornerRadius(FaceCorner), BorderThickness = new Thickness(1), Child = value,
        };
        Bind(face, Border.BackgroundProperty, thumb, "SliderThumbFace");
        Bind(face, Border.BorderBrushProperty, thumb, "SliderThumbBorder");
        // The thumb glows in the palette glow colour: a 3 DIP blur at 40 percent opacity with no offset, in the colour of the theme the face sits in.
        void Glow()
        {
            var glow = AvaloniaPalette.Get(ThumbGlowPaletteKey, face.ActualThemeVariant == ThemeVariant.Light);
            var glowColour = AvaloniaPalette.ColorOf(glow.WithOpacity(ThumbGlowOpacity));
            face.BoxShadow = BoxShadows.Parse($"0 0 {ThumbGlowBlur.ToString(CultureInfo.InvariantCulture)} 0 {glowColour}");
        }
        Glow();
        face.FollowTheme(Glow);
        face.RegisterInNameScope(scope);
        return face;
    }

    // Template priority, so the look's state rule (a style) can recolour the part.
    private static void Bind(AvaloniaObject target, AvaloniaProperty<IBrush?> property, Control host, string key) =>
        target.Bind(property, host.GetResourceObservable(key), BindingPriority.Template);
}
