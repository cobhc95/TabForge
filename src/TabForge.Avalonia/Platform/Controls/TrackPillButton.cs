using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: one track's capsule in a header strip of track pills: a dot in the track's colour (the palette's fallback colour when the track has none) and
//   the track's name. Its shape and text size are here; its state colours come from the Core "TrackPill" look (the class this sets), and its idle
//   colours from IdleLook, a style so the look's hover, press and checked states still win over them.
// Does not own: the lit state (the strip sets IsChecked), what a click does, the strip that holds the capsules.
// Tests: TabForge.Avalonia.Tests.CompactTrackStripTests.
public sealed class TrackPillButton : ToggleButton
{
    /// <summary>The class the Core "TrackPill" look rules apply to.</summary>
    public const string LookClass = "TrackPill";

    /// <summary>The palette key of the dot colour for a track that has none.</summary>
    public const string FallbackColourKey = "TrackColourFallback";

    private const double PillRadius = 11;
    private const double MinimumHeight = 22;
    private const double OutlineWidth = 1;
    private const double DotSize = 8;
    private const double DotGap = 6;
    private const double NameSize = 12;
    private const double NameMaxWidth = 160;

    private static readonly Thickness CapsulePadding = new(8, 2, 10, 2);
    private static readonly Thickness CapsuleMargin = new(0, 0, 4, 4);

    /// <summary>The name of the capsule's one border part.</summary>
    private const string BorderPartName = "Bd";

    private readonly Ellipse _dot;

    /// <summary>The track's name, as the capsule shows it.</summary>
    public string TrackName { get; }

    /// <summary>The track's colour as the song stores it (a hex text).</summary>
    public string ColourHex { get; }

    /// <summary>The dot of the capsule, filled with the track's colour.</summary>
    public Ellipse Dot => _dot;

    public TrackPillButton(string name, string colourHex, string toolTip)
    {
        TrackName = name;
        ColourHex = colourHex;
        Classes.Add(LookClass);
        // The look sets only control properties, so the capsule carries its own one-border template that paints them (fill, outline, radius).
        Template = AvaloniaButtonTemplates.SingleBorder(BorderPartName);
        _dot = new Ellipse
        {
            Width = DotSize, Height = DotSize, Margin = new Thickness(0, 0, DotGap, 0), VerticalAlignment = VerticalAlignment.Center,
            // light: false is safe: the fallback key is a base token with one value for both themes.
            Fill = Color.TryParse(colourHex, out var colour) ? new SolidColorBrush(colour) : new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(FallbackColourKey, light: false))),
        };
        var label = new TextBlock { Text = name, FontSize = NameSize, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = NameMaxWidth, VerticalAlignment = VerticalAlignment.Center };
        // The name follows the capsule's foreground, which the look sets for each state (the app's TextBlock style would otherwise paint its own).
        label.Bind(TextBlock.ForegroundProperty, this.GetObservable(ForegroundProperty));
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dot, label } };
        Padding = CapsulePadding;
        MinHeight = MinimumHeight;
        BorderThickness = new Thickness(OutlineWidth);
        CornerRadius = new global::Avalonia.CornerRadius(PillRadius);
        Margin = CapsuleMargin;
        ToolTip.SetTip(this, toolTip);
    }

    /// <summary>True when the capsule still shows this track's name and colour, so the strip need not rebuild it.</summary>
    public bool Shows(string name, string colourHex) => TrackName == name && ColourHex == colourHex;

    /// <summary>The idle colours of a capsule (panel fill, soft outline, the normal text colour as WPF's idle capsules), for the strip that holds capsules to add to its styles; it
    /// matches only an idle capsule, so the look's hover, press and checked states (app styles, further from the capsule) still show.</summary>
    public static Style IdleLook()
    {
        var style = new Style(x => x.Is<ToggleButton>().Class(LookClass).Not(s => s.Class(":checked")).Not(s => s.Class(":pointerover")).Not(s => s.Class(":pressed")));
        style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("PanelBrush")));
        style.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
        style.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        return style;
    }
}
