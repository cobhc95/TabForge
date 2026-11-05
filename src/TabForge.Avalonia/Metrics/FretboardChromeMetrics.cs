using Avalonia;
using Avalonia.Controls;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia;

// Owns: the sizes of the fretboard's own chrome: the Customize Fretboard pill and its panel.
// Does not own: the fretboard drawing (FretboardArtist) or the panel's rows (FretboardSettingsWindow).
public static class FretboardChromeMetrics
{
    public const double PillWidth = 72, PillHeight = 24, PillIcon = 13, PillFontSize = 11, PillRadius = 4, PillStroke = 1.5;
    public const double PillPadX = 7, PillIconGap = 5;
    public const string PillIconPath = "M12,3 C7,3 3,7 3,12 C3,17 7,21 12,21 C13.4,21 14,20 14,19 C14,18.4 13.7,18 13.4,17.6 C13.1,17.2 13,16.8 13,16.4 C13,15.4 13.8,14.6 14.8,14.6 L18,14.6 C19.7,14.6 21,13.3 21,11.6 C21,6.7 17,3 12,3 Z";
    public static Thickness PillBorder => new(1);
    public const double PanelWidth = 380, PanelMaxHeight = 560, PanelTitleSize = 14;
    /// <summary>The Fretboard settings window: the gap between footer buttons and the small-note label size.</summary>
    public const double FooterGap = 8, PreviewLabelSize = 11, PageTitleSize = 20, PageCardGap = 12;
    /// <summary>The Fretboard settings footer band's top edge.</summary>
    public static Thickness FooterBandBorder => new(0, 1, 0, 0);
    /// <summary>The rail row's columns: the glyph, the label gap, the label.</summary>
    public static ColumnDefinitions NavColumns => new($"{ChromeMetrics.SettingsRailIconSize},{ChromeMetrics.SettingsRailLabelGap},*");
}
