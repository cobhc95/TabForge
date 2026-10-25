// Owns: the score text style window's logic without UI types: the seven text areas with their labels and style keys, the area being
//       edited, the style entry of each area (created on first use), the values each row shows (font, size, colours, outline), and the
//       rules that write a row's change back (typing "(inherit)" clears the font, zero outline shows as off, picking an outline colour
//       turns the outline on at 1px).
// Does not own: the window, its controls, the system font list, the colour chooser dialog or the live preview on the score.
// Tests: no dedicated test class.
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Presenters;

/// <summary>
/// Score text style state for the window. Each area's style is <see cref="ScoreTextAreaStyle"/> in the dictionary the caller passes in;
/// an area with no entry yet gets one the first time it is shown or edited. Area keys are the score's text area names, as
/// <c>ScoreText.ConfigureAreas</c> reads them.
/// Thread: UI thread only (it is driven by the window).
/// </summary>
public sealed class ScoreTextStylePresenter
{
    /// <summary>The areas in list order: the style key of each and the label the window shows.</summary>
    public static IReadOnlyList<(string Key, string Label)> Areas { get; } =
    [
        (nameof(ScoreTextArea.Fret), "Fret numbers (TAB)"), (nameof(ScoreTextArea.Technique), "Techniques (P.M., accents, …)"),
        (nameof(ScoreTextArea.Chord), "Chord names"), (nameof(ScoreTextArea.Lyrics), "Text & lyrics"),
        (nameof(ScoreTextArea.BarInfo), "Bar numbers, tempo, sections"), (nameof(ScoreTextArea.Header), "Title & header"),
        (nameof(ScoreTextArea.General), "Other score text"),
    ];

    private readonly IDictionary<string, ScoreTextAreaStyle> _styles;
    private int _selectedArea;

    public ScoreTextStylePresenter(IDictionary<string, ScoreTextAreaStyle> styles) => _styles = styles;

    /// <summary>Index into <see cref="Areas"/> of the area being edited.</summary>
    public int SelectedArea
    {
        get => _selectedArea;
        set => _selectedArea = Math.Clamp(value, 0, Areas.Count - 1);
    }

    /// <summary>The style of the selected area, created when the area has no entry yet.</summary>
    public ScoreTextAreaStyle Current
    {
        get
        {
            var key = Areas[SelectedArea].Key;
            if (!_styles.TryGetValue(key, out var style) || style is null) _styles[key] = style = new ScoreTextAreaStyle();
            return style;
        }
    }

    /// <summary>The font box text: "(inherit)" when the area has no font of its own.</summary>
    public string FontText => string.IsNullOrWhiteSpace(Current.Font) ? "(inherit)" : Current.Font!;

    /// <summary>The size slider value: the area's percentage, 100 when it inherits.</summary>
    public double SizeValue => Current.SizePercent <= 0 ? 100 : Current.SizePercent;

    /// <summary>The size readout under the size slider, e.g. "120%".</summary>
    public static string SizeLabel(double percent) => $"{percent:0}%";

    /// <summary>The outline readout, e.g. "off" or "1.5px".</summary>
    public static string OutlineLabel(double thickness) => thickness <= 0 ? "off" : $"{thickness:0.##}px";

    /// <summary>The text colour swatch: the area's colour, else the default score text colour.</summary>
    public Rgba TextSwatch => ColourHex.TryParse(Current.Colour, out var colour) ? colour : PaletteColour("ScoreTextDefaultColour");

    /// <summary>The default score text colour, what the text colour chooser's Default button answers and what an area without a colour shows.</summary>
    public Rgba TextDefault => PaletteColour("ScoreTextDefaultColour");

    /// <summary>The default outline colour.</summary>
    public Rgba OutlineDefault => PaletteColour("ScoreTextDefaultOutline");

    /// <summary>The outline colour swatch: the area's outline colour, else the default outline colour.</summary>
    public Rgba OutlineSwatch => ColourHex.TryParse(Current.OutlineColour, out var colour) ? colour : PaletteColour("ScoreTextDefaultOutline");

    /// <summary>The colour the text colour chooser opens with when the area has none: white.</summary>
    public Rgba TextPickerStart => ColourHex.TryParse(Current.Colour, out var colour) ? colour : PaletteColour("White");

    /// <summary>The colour the outline chooser opens with when the area has none: black.</summary>
    public Rgba OutlinePickerStart => ColourHex.TryParse(Current.OutlineColour, out var colour) ? colour : PaletteColour("Black");

    public void SetFont(string? typed)
    {
        var font = typed?.Trim();
        Current.Font = string.IsNullOrEmpty(font) || font == "(inherit)" ? null : font;
    }

    public void SetSize(double percent) => Current.SizePercent = percent;

    public void SetBold(bool? bold) => Current.Bold = bold;

    public void SetItalic(bool? italic) => Current.Italic = italic;

    public void SetOutlineThickness(double thickness) => Current.OutlineThickness = thickness;

    public void SetColour(Rgba colour) => Current.Colour = ColourHex.FormatAuto(colour);

    public void ClearColour() => Current.Colour = null;

    /// <summary>Sets the outline colour; an outline that was off turns on at 1px so the colour shows.</summary>
    public void SetOutlineColour(Rgba colour)
    {
        Current.OutlineColour = ColourHex.FormatAuto(colour);
        if (Current.OutlineThickness <= 0) Current.OutlineThickness = 1;
    }

    /// <summary>Removes the selected area's override so it inherits the global score text style again.</summary>
    public void ResetArea() => _styles.Remove(Areas[SelectedArea].Key);

    private static Rgba PaletteColour(string name)
    {
        var colour = Palette.Get(name);
        return new Rgba(colour.R, colour.G, colour.B, colour.A);
    }
}
