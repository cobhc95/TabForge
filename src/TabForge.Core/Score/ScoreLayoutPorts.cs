namespace TabForge.Views.Score;

// Owns: what the score layout reads from the app: the spacing settings and the laid-out width of score text.
// Does not own: the settings themselves (ScoreAppearance) or text measuring and its caches (ScoreText, in the app).
// Tests: no dedicated test class.

/// <summary>The areas of a score that carry their own text style.</summary>
internal enum ScoreTextArea { General, Header, BarInfo, Fret, Technique, Chord, Lyrics }

/// <summary>Colour text from song files (hex or a named colour such as "Red"); the app parses it with its toolkit, so Core keeps no name table.</summary>
internal interface IColourText
{
    /// <returns>The colour, or null when the text does not parse.</returns>
    TabForge.Platform.UiColor? Parse(string? text);
}

/// <summary>The appearance settings the score layout spaces by.</summary>
internal interface IScoreLayoutSettings
{
    double ScoreSpacing { get; }
    double SystemVerticalSpacing { get; }
    double MeasureHorizontalSpacing { get; }
    bool ShowDynamics { get; }
    bool ShowSectionHeadings { get; }
}

/// <summary>Laid-out widths of score text, measured with the same style and cache the drawing uses.</summary>
internal interface IScoreTextWidths
{
    /// <param name="weight">OpenType weight, 400 normal, 700 bold.</param>
    double Width(ScoreTextArea area, string text, double size, int weight = 400, string font = "Segoe UI");
    /// <summary>Width of a dynamic's letters.</summary>
    double DynamicWidth(string name);
}
